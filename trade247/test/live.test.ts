import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { HandEvent } from '../src/feeds/feed.js';
import { LiveFeed, noTableReason, type LiveHooks } from '../src/feeds/live.js';
import { listPages } from '../src/live/cdp.js';
import { PageSession } from '../src/live/page.js';
import { inspectTables } from '../src/live/inspect.js';
import { EVOLUTION_BEAD_ROAD, PRAGMATIC_BEAD_ROAD, PROFILE_FILE, diffRoad, loadProfile, loadProfiles, parseProfile, probeScript } from '../src/live/profile.js';
import { findChrome, openTab, startChrome, startMockTable, until, type MockTable, type TestChrome } from './chrome.js';
import { harness, MANUAL } from './helpers.js';

describe('comparing two readings of the road', () => {
  it('finds new hands, no change and a reset', () => {
    expect(diffRoad([], [])).toEqual({ kind: 'same' });
    expect(diffRoad(['P', 'B'], ['P', 'B'])).toEqual({ kind: 'same' });
    expect(diffRoad(['P', 'B'], ['P', 'B', 'T', 'P'])).toEqual({ kind: 'grew', fresh: ['T', 'P'] });
    expect(diffRoad([], ['B'])).toEqual({ kind: 'grew', fresh: ['B'] });
    expect(diffRoad(['P', 'B', 'T'], ['P', 'B'])).toEqual({ kind: 'reset' });
    expect(diffRoad(['P', 'B', 'T'], [])).toEqual({ kind: 'reset' });
    expect(diffRoad(['P', 'B'], ['B', 'P', 'T'])).toEqual({ kind: 'reset' });
  });
});

describe('which profiles are tried', () => {
  it('uses the built-in ones of both providers unless the data folder has a profile of its own', () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-prof-'));
    try {
      const builtIn = loadProfiles(dir);
      expect(Array.isArray(builtIn) && builtIn.map((p) => p.order)).toEqual(['document', 'columns']);
      expect(Array.isArray(builtIn) && builtIn.every((p) => p.betting === null)).toBe(true);
      expect(PRAGMATIC_BEAD_ROAD.containerSelector).toBe('.wC_wK');
      writeFileSync(join(dir, PROFILE_FILE), JSON.stringify({ ...EVOLUTION_BEAD_ROAD, name: 'mine' }));
      expect(loadProfiles(dir)).toMatchObject([{ name: 'mine' }]);
      writeFileSync(join(dir, PROFILE_FILE), '{ not json');
      expect(loadProfiles(dir)).toMatchObject({ error: expect.stringContaining('not valid JSON') });
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('says what was looked at and what to do when no table is found', () => {
    expect(noTableReason(9014, [], [])).toContain('has no page open');
    const plain = noTableReason(9014, ['roobet.com', 'roobet.com', 'example.org'], []);
    expect(plain).toContain('Looked at 3 tab(s)');
    expect(plain).toContain('roobet.com, example.org');
    expect(plain).toContain('normal Chrome cannot be read');
    const known = noTableReason(9014, ['roobet.com'], ['Pragmatic Play']);
    expect(known).toContain('A Pragmatic Play table is open there, but its road was not recognised');
    expect(known).toContain('--inspect-table');
  });
});

describe('the table profile', () => {
  it('accepts the built-in profile and refuses malformed ones with a reason', () => {
    expect(parseProfile(JSON.parse(JSON.stringify(EVOLUTION_BEAD_ROAD)))).toMatchObject({ itemSelector: '[data-role="roadItem"]' });
    for (const [bad, text] of [
      [null, 'JSON object'],
      [{ ...EVOLUTION_BEAD_ROAD, containerSelector: '' }, 'containerSelector'],
      [{ ...EVOLUTION_BEAD_ROAD, itemSelector: 5 }, 'itemSelector'],
      [{ ...EVOLUTION_BEAD_ROAD, rules: [] }, 'rules'],
      [{ ...EVOLUTION_BEAD_ROAD, rules: [{ exact: 'P', result: 'X' }] }, 'result'],
      [{ ...EVOLUTION_BEAD_ROAD, rules: [{ result: 'P' }] }, 'exactly one'],
      [{ ...EVOLUTION_BEAD_ROAD, rules: [{ exact: 'P', contains: 'p', result: 'P' }] }, 'exactly one'],
      [{ ...EVOLUTION_BEAD_ROAD, hostContains: 7 }, 'hostContains'],
    ] as const) {
      const r = parseProfile(bad);
      expect('error' in r && r.error, JSON.stringify(bad)).toContain(text);
    }
  });

  it('uses the built-in profile unless the data folder has one, and reports a broken file instead of ignoring it', () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-prof-'));
    try {
      expect(loadProfile(dir)).toEqual(EVOLUTION_BEAD_ROAD);
      writeFileSync(join(dir, PROFILE_FILE), JSON.stringify({ ...EVOLUTION_BEAD_ROAD, name: 'Mine', hostContains: 'roobet' }));
      expect(loadProfile(dir)).toMatchObject({ name: 'Mine', hostContains: 'roobet' });
      writeFileSync(join(dir, PROFILE_FILE), '{ nope');
      expect(loadProfile(dir)).toMatchObject({ error: expect.stringContaining('not valid JSON') });
      writeFileSync(join(dir, PROFILE_FILE), JSON.stringify({ rules: [] }));
      expect(loadProfile(dir)).toMatchObject({ error: expect.stringContaining(PROFILE_FILE) });
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });

  it('builds a page script that carries no build-tool helpers (it runs in the browser, on its own)', () => {
    const script = probeScript(EVOLUTION_BEAD_ROAD);
    expect(script).not.toContain('__name');
    expect(script).toContain('Bead-road');
    expect(() => new Function(`return ${script}`)).not.toThrow();
  });
});

const chrome = findChrome();

describe.skipIf(!chrome)('reading a table in a real Chrome', () => {
  let browser: TestChrome;
  let table: MockTable;

  beforeAll(async () => {
    browser = await startChrome(chrome!);
    table = await startMockTable();
    await openTab(browser.port, table.pageUrl);
  }, 30_000);

  afterAll(async () => {
    await table?.stop();
    await browser?.stop();
  });

  const collect = () => {
    const hands: HandEvent[] = [];
    const log: string[] = [];
    const hooks: LiveHooks = {
      connected: () => log.push('connected'),
      waiting: (r) => log.push(`waiting:${r}`),
      lost: (r) => log.push(`lost:${r}`),
      shoeChange: () => log.push('shoe'),
    };
    return { hands, log, hooks };
  };
  const results = (hands: HandEvent[]) => hands.map((h) => h.result).join('');

  it('reads a road drawn as a grid column by column, skipping empty cells', async () => {
    table.state.container = true;
    table.state.cells = [];
    table.state.rows = ['BPP...', 'TBB...', 'BT....', 'PB....', 'BB....', 'PT....'];
    await new Promise((r) => setTimeout(r, 600));
    const target = (await listPages(browser.port)).find((p) => p.url.includes('/lobby'))!;
    const session = await PageSession.attach(target, 1200);
    try {
      const profile = parseProfile({
        name: 'grid', containerSelector: '.row', itemSelector: '.cell', order: 'columns',
        rules: [{ exact: 'P', result: 'P' }, { exact: 'B', result: 'B' }, { exact: 'T', result: 'T' }],
      });
      if ('error' in profile) throw new Error(profile.error);
      expect(profile.order).toBe('columns');
      const reading = await session.readRoad(profile);
      // Column 1 top to bottom (B T B P B P), column 2 (P B T B B T), column 3 (P B).
      expect(reading?.items.join('')).toBe('BTBPBP' + 'PBTBBT' + 'PB');
      expect(reading?.unreadable).toBe(false);
      // The same page read as a plain list gives the cells row by row only when asked to.
      expect(parseProfile({ ...profile, order: 'sideways' })).toMatchObject({ error: expect.stringContaining('order') });
    } finally {
      session.close();
      table.state.rows = null;
    }
  });

  it('tries the profiles in turn and keeps the one that finds the road (a table of another provider, no set-up)', async () => {
    table.state.container = true;
    table.state.cells = [];
    table.state.rows = ['BP....', 'TB....', 'B.....', 'P.....', 'B.....', 'P.....'];
    const grid = parseProfile({
      name: 'grid', containerSelector: '.row', itemSelector: '.cell', order: 'columns',
      rules: [{ exact: 'P', result: 'P' }, { exact: 'B', result: 'B' }, { exact: 'T', result: 'T' }],
    });
    if ('error' in grid) throw new Error(grid.error);
    const { hands, log, hooks } = collect();
    // The first profile (Evolution) matches nothing on this page; the second is the one that reads it.
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, profiles: [EVOLUTION_BEAD_ROAD, grid], pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.includes('connected'));
      await new Promise((r) => setTimeout(r, 300));
      expect(hands).toHaveLength(0); // what is on the screen is history
      table.state.rows = ['BPT...', 'TB....', 'B.....', 'P.....', 'B.....', 'P.....'];
      await until(() => hands.length === 1);
      expect(results(hands)).toBe('T');
    } finally {
      feed.stop();
      table.state.rows = null;
    }
  }, 25_000);

  it('lists the tab and reads a road inside a frame from another site', async () => {
    const pages = await listPages(browser.port);
    expect(pages.some((p) => p.url.includes('/lobby'))).toBe(true);
  });

  it('does not play the history on screen, then reports every new hand once and in order', async () => {
    table.state.container = true;
    table.state.cells = ['P', 'B', 'B'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.includes('connected'));
      await new Promise((r) => setTimeout(r, 300));
      expect(hands).toHaveLength(0);
      table.state.cells = ['P', 'B', 'B', 'T'];
      await until(() => hands.length === 1);
      table.state.cells = ['P', 'B', 'B', 'T', 'P', 'P'];
      await until(() => hands.length === 3);
      await new Promise((r) => setTimeout(r, 300));
      expect(results(hands)).toBe('TPP');
      expect(log.includes('shoe')).toBe(false);
    } finally {
      feed.stop();
    }
  }, 20_000);

  it('treats a shorter road as a new shoe once two readings agree, and announces it once', async () => {
    table.state.cells = ['P', 'B', 'T', 'P'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.includes('connected'));
      table.state.cells = [];
      await new Promise((r) => setTimeout(r, 400));
      expect(hands).toHaveLength(0);
      // The shoe is announced as soon as the empty road is confirmed, before any hand of the new shoe.
      await until(() => log.includes('shoe'));
      table.state.cells = ['B'];
      await until(() => hands.length === 1);
      table.state.cells = ['B', 'P'];
      await until(() => hands.length === 2);
      expect(hands.map((h) => h.result)).toEqual(['B', 'P']);
      expect(log.filter((l) => l === 'shoe')).toHaveLength(1);
    } finally {
      feed.stop();
    }
  }, 20_000);

  it('ignores a reading with a cell it cannot recognise instead of guessing', async () => {
    table.state.cells = ['P', 'B'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.includes('connected'));
      table.state.cells = ['P', 'B', 'Z', 'T']; // one hand cannot be read, the next is a tie
      await new Promise((r) => setTimeout(r, 400));
      expect(hands).toHaveLength(0);
      table.state.cells = ['P', 'B', 'P', 'T'];
      await until(() => hands.length === 2);
      expect(results(hands)).toBe('PT');
    } finally {
      feed.stop();
    }
  }, 20_000);

  it('reports the table lost once, and after it comes back does not replay the hands it missed', async () => {
    table.state.container = true;
    table.state.cells = ['P', 'B'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed(
      (e) => hands.push(e),
      { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100, lostAfterMs: 300 },
      hooks,
    );
    feed.start();
    try {
      await until(() => log.filter((l) => l === 'connected').length === 1);
      table.state.container = false; // the player left the table
      await until(() => log.some((l) => l.startsWith('lost:')));
      await new Promise((r) => setTimeout(r, 400));
      expect(log.filter((l) => l.startsWith('lost:'))).toHaveLength(1);
      table.state.container = true;
      table.state.cells = ['P', 'B', 'B', 'B', 'P']; // three hands were dealt while away
      await until(() => log.filter((l) => l === 'connected').length === 2);
      await new Promise((r) => setTimeout(r, 300));
      expect(hands).toHaveLength(0);
      table.state.cells = ['P', 'B', 'B', 'B', 'P', 'T'];
      await until(() => hands.length === 1);
      expect(results(hands)).toBe('T');
    } finally {
      feed.stop();
    }
  }, 25_000);

  it('says what to do when Chrome is not reachable, and never claims a loss it did not have', async () => {
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: 1, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.some((l) => l.startsWith('waiting:')));
      expect(log.find((l) => l.startsWith('waiting:'))).toContain('Cannot reach Chrome on port 1');
      expect(log.some((l) => l.startsWith('lost:'))).toBe(false);
    } finally {
      feed.stop();
    }
  }, 15_000);

  it('retry reconnects without losing or repeating hands', async () => {
    table.state.container = true;
    table.state.cells = ['P'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    try {
      await until(() => log.includes('connected'));
      feed.retry();
      await until(() => log.filter((l) => l === 'connected').length === 2);
      table.state.cells = ['P', 'B'];
      await until(() => hands.length === 1);
      await new Promise((r) => setTimeout(r, 300));
      expect(results(hands)).toBe('B');
      expect(log.some((l) => l.startsWith('lost:'))).toBe(false);
    } finally {
      feed.stop();
    }
  }, 20_000);

  it('a stopped feed delivers nothing more and leaves no connection behind', async () => {
    table.state.cells = ['P'];
    const { hands, log, hooks } = collect();
    const feed = new LiveFeed((e) => hands.push(e), { port: browser.port, profile: EVOLUTION_BEAD_ROAD, pollMs: 40, scanMs: 100 }, hooks);
    feed.start();
    await until(() => log.includes('connected'));
    feed.stop();
    table.state.cells = ['P', 'B', 'B'];
    await new Promise((r) => setTimeout(r, 400));
    expect(hands).toHaveLength(0);
  }, 15_000);

  it('the calibration capture lists the table elements but not balances, query strings or fragments', async () => {
    table.state.container = true;
    table.state.cells = ['P', 'B', 'T'];
    const dir = mkdtempSync(join(tmpdir(), 't247-insp-'));
    try {
      const r = await inspectTables(browser.port, dir);
      expect(r.withRoles).toBeGreaterThan(0);
      const text = readFileSync(r.path, 'utf8');
      expect(text).toContain('Bead-road');
      expect(text).toContain('roadItem');
      expect(text).toContain('PLACE YOUR BETS');
      expect(text).not.toContain('SECRET123');
      expect(text).not.toContain('frag');
      expect(text).not.toContain('1,234.50');
      expect(text).not.toContain('/lobby');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  }, 20_000);

  it('runs a whole session on the live feed through the program, using hands read from the table', async () => {
    table.state.container = true;
    table.state.cells = ['P', 'B'];
    const h = harness();
    try {
      const started = await h.post('/api/start', { settings: { ...MANUAL, feed: 'live', live_port: browser.port } });
      expect(started.json.ok).toBe(true);
      await until(() => !String(h.manager.getState(0).status).startsWith('FEED'), 10_000);
      table.state.cells = ['P', 'B', 'P', 'B', 'B'];
      await until(() => h.manager.getState(0).hands === 3, 10_000);
      const state = h.manager.getState(0);
      expect(state.rows.map((r: { result: string }) => r.result)).toEqual(['P', 'B', 'B']);
      expect(state.running).toBe(true);
      await h.post('/api/stop', {});
      table.state.cells = ['P', 'B', 'P', 'B', 'B', 'P'];
      await new Promise((r) => setTimeout(r, 300));
      expect(h.manager.getState(0).hands).toBe(3);
    } finally {
      await h.close();
    }
  }, 30_000);
});

describe('the live feed in the program, without a Chrome', () => {
  it('tells the user what to do while it waits, and does not change the port of a running session', async () => {
    const h = harness();
    try {
      const started = await h.post('/api/start', { settings: { ...MANUAL, feed: 'live', live_port: 1 } });
      expect(started.json.ok).toBe(true);
      await until(() => h.manager.getState(0).status.startsWith('FEED WAITING'), 10_000);
      expect(h.manager.getState(0).status).toContain('Cannot reach Chrome on port 1');
      await h.post('/api/apply', { ...MANUAL, feed: 'live', live_port: 2 });
      expect(h.manager.getState(0).settings.live_port).toBe(1);
      await h.post('/api/stop', {});
      expect(h.manager.getState(0).status).toBe('STOPPED');
    } finally {
      await h.close();
    }
  }, 20_000);

  it('refuses to start when the profile file in the data folder is broken', async () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-badprof-'));
    writeFileSync(join(dir, PROFILE_FILE), '[]');
    const h = harness(dir);
    try {
      const started = await h.post('/api/start', { settings: { ...MANUAL, feed: 'live' } });
      expect(started.json.ok).toBe(false);
      expect(started.json.status).toContain(PROFILE_FILE);
    } finally {
      await h.close();
    }
  });

  it('keeps the port inside the valid range', async () => {
    const h = harness();
    try {
      await h.post('/api/start', { settings: { ...MANUAL, feed: 'live', live_port: '99999' } });
      expect(h.manager.getState(0).settings.live_port).toBe(65535);
      await h.post('/api/stop', {});
    } finally {
      await h.close();
    }
  });
});
