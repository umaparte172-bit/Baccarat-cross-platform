import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { checkTable } from '../src/live/inspect.js';
import { listPages } from '../src/live/cdp.js';
import { TableBettor } from '../src/live/bettor.js';
import { PageSession } from '../src/live/page.js';
import { parseProfile, type BettingProfile } from '../src/live/profile.js';
import { findChrome, openTab, startChrome, type TestChrome } from './chrome.js';
import { MOCK_BETTING_PROFILE, startMockBetting, type MockBetting } from './mockBetting.js';

const chrome = findChrome();
const profile = parseProfile(MOCK_BETTING_PROFILE);
if ('error' in profile || !profile.betting) throw new Error('the mock profile must be valid');
const betting: BettingProfile = profile.betting;

describe('the betting block of a profile', () => {
  it('is optional, validated, and keeps the table read-only when absent', () => {
    expect(parseProfile({ ...MOCK_BETTING_PROFILE, betting: undefined })).toMatchObject({ betting: null });
    for (const [patch, text] of [
      [{ statusSelector: '' }, 'statusSelector'],
      [{ chipSelector: 5 }, 'chipSelector'],
      [{ openText: '' }, 'openText'],
      [{ chipValueFrom: 'bad value!' }, 'chipValueFrom'],
      [{ betTotalSelector: undefined }, 'betTotalSelector'],
    ] as const) {
      const r = parseProfile({ ...MOCK_BETTING_PROFILE, betting: { ...MOCK_BETTING_PROFILE.betting, ...patch } });
      expect('error' in r && r.error, JSON.stringify(patch)).toContain(text);
    }
    expect(parseProfile({ ...MOCK_BETTING_PROFILE, betting: 'yes' })).toMatchObject({ error: expect.stringContaining('betting') });
  });
});

describe.skipIf(!chrome)('placing bets in a real Chrome', () => {
  let browser: TestChrome;
  let table: MockBetting;
  let page: PageSession;

  beforeAll(async () => {
    browser = await startChrome(chrome!);
    table = await startMockBetting();
    await openTab(browser.port, table.pageUrl);
    await new Promise((r) => setTimeout(r, 600));
    const target = (await listPages(browser.port)).find((p) => p.url.startsWith(table.pageUrl));
    if (!target) throw new Error('the mock table did not open');
    page = await PageSession.attach(target, 800);
  }, 30_000);

  afterAll(async () => {
    page?.close();
    await table?.stop();
    await browser?.stop();
  });

  beforeEach(async () => {
    const s = table.state;
    s.open = true;
    s.status = 'PLACE YOUR BETS';
    s.total = 0;
    s.selected = null;
    s.clicks = [];
    s.placedOn = [];
    s.mode = 'normal';
    s.coverBanker = false;
    s.coverPlayer = false;
    await table.waitForUi();
  });

  const bettor = (extra: ConstructorParameters<typeof TableBettor>[2] = {}) =>
    // Exercise correctness, not sub-frame input throughput. Use the production
    // minimum gap deterministically so hosted browser compositors can process input.
    new TableBettor(page, betting, { clickGapMs: [60, 60], confirmMs: 2500, ...extra });
  const clickLog = () => table.state.clicks.map((c) => (c.value ? `${c.target}:${c.value}` : c.target));

  it('reads the window in a frame inside a frame from another site', async () => {
    const snap = await bettor().readWindow();
    expect(snap?.reading).toMatchObject({ found: true, open: true, text: 'PLACE YOUR BETS', hasPlayer: true, hasBanker: true, total: 0 });
    expect(snap?.reading.chips.map((c) => c.value)).toEqual([0.1, 1, 5, 25, 100]);
  });

  it('builds the stake from the fewest chips, clicks each with a real mouse and waits for the table to show it', async () => {
    const result = await bettor().place('P', 7);
    expect(result).toEqual({ kind: 'accepted', chips: [5, 1, 1], total: 7 });
    expect(clickLog()).toEqual(['chip:5', 'player', 'chip:1', 'player', 'player']);
    expect(table.state.clicks.every((c) => c.trusted)).toBe(true);
    expect(table.state.total).toBe(7);
  });

  it('puts a Banker bet on the Banker spot (the click lands where the element is, through two frames)', async () => {
    const result = await bettor().place('B', 125);
    expect(result, JSON.stringify({ result, clicks: table.state.clicks, total: table.state.total })).toMatchObject({ kind: 'accepted', total: 125 });
    expect(clickLog()).toEqual(['chip:100', 'banker', 'chip:25', 'banker']);
    expect(table.state.placedOn).toEqual(['banker', 'banker']);
  });

  it('works with a small stake, down to the smallest chip', async () => {
    expect(await bettor().place('P', 0.3)).toMatchObject({ kind: 'accepted', total: 0.3 });
    expect(clickLog()).toEqual(['chip:0.1', 'player', 'player', 'player']);
  });

  it('does not touch the table when the window is closed', async () => {
    table.state.open = false;
    table.state.status = 'BETS CLOSED';
    await table.waitForUi();
    const r = await bettor().place('P', 5);
    expect(r).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('not open') });
    expect(table.state.clicks).toHaveLength(0);
  });

  it('does not place a second bet on top of one already on the table', async () => {
    table.state.total = 5;
    await table.waitForUi();
    const r = await bettor().place('P', 5);
    expect(r).toEqual({ kind: 'rejected', reason: 'A bet is already on the table' });
    expect(table.state.clicks).toHaveLength(0);
  });

  it('refuses a stake the chips cannot make, and one that needs too many clicks, without clicking', async () => {
    expect(await bettor().place('P', 0.05)).toMatchObject({ kind: 'rejected' });
    table.state.clicks = [];
    expect(await bettor({ maxClicks: 2 }).place('P', 7)).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('chip clicks') });
    expect(table.state.clicks).toHaveLength(0);
  });

  it('reports nothing placed when a spot cannot be clicked (covered by something else)', async () => {
    table.state.coverBanker = true;
    await table.waitForUi();
    const r = await bettor().place('B', 5);
    expect(r).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('covered') });
    expect(table.state.placedOn).toEqual([]);
  });

  it('says "unconfirmed", and does not retry, when the table never shows the bet', async () => {
    table.state.mode = 'noconfirm';
    const r = await bettor().place('P', 5);
    expect(r).toMatchObject({ kind: 'unconfirmed', reason: expect.stringContaining('did not show the bet') });
    expect(table.state.placedOn).toEqual(['player']); // exactly one attempt
  });

  it('says "unconfirmed" when the table shows more than was asked', async () => {
    table.state.mode = 'double';
    const r = await bettor().place('P', 5);
    expect(r).toMatchObject({ kind: 'unconfirmed', reason: expect.stringContaining('shows 10') });
  });

  it('says "unconfirmed" when the window closes with only part of the bet placed', async () => {
    table.state.mode = 'closeafter1';
    const r = await bettor().place('P', 7);
    expect(r.kind).toBe('unconfirmed');
    expect(table.state.total).toBe(5);
  });

  it('says "unconfirmed" when a click fails after part of the bet is on the table', async () => {
    table.state.mode = 'coverafter1';
    const r = await bettor().place('P', 7); // chips 5, 1, 1: the 5 lands, then the Player spot gets covered
    expect(r).toMatchObject({ kind: 'unconfirmed', reason: expect.stringContaining('after part of the bet was placed') });
    expect(table.state.total).toBe(5);
    expect(table.state.placedOn).toEqual(['player']);
  });

  it('a click that is ignored leaves nothing placed and is not repeated', async () => {
    table.state.mode = 'reject';
    const r = await bettor().place('P', 5);
    expect(r.kind).toBe('unconfirmed'); // the click landed but the table took no chip: money state unknown
    expect(clickLog()).toEqual(['chip:5', 'player']);
  });

  it('stops between clicks when told to, and says what is left on the table', async () => {
    let calls = 0;
    const r = await bettor({ shouldAbort: () => ++calls > 2 }).place('P', 7);
    expect(r).toMatchObject({ kind: 'unconfirmed', reason: expect.stringContaining('Stopped') });
    expect(table.state.clicks.length).toBeLessThan(5);
  });

  it('a stop before the first click places nothing', async () => {
    const r = await bettor({ shouldAbort: () => true }).place('P', 7);
    expect(r).toMatchObject({ kind: 'rejected' });
    expect(table.state.clicks).toHaveLength(0);
  });

  it('--check-table shows what can be read, and clicks nothing', async () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-check-'));
    try {
      writeFileSync(join(dir, 'live-profile.json'), JSON.stringify(MOCK_BETTING_PROFILE));
      const lines = (await checkTable(browser.port, dir)).join(' | ');
      expect(lines).toContain('with betting settings');
      expect(lines).toContain('bets OPEN');
      expect(lines).toContain('Chips: 0.1, 1, 5, 25, 100');
      expect(lines).toContain('Player found, Banker found');
      expect(lines).toContain('Total on the table: 0');
      expect(table.state.clicks).toHaveLength(0);

      writeFileSync(join(dir, 'live-profile.json'), JSON.stringify({ ...MOCK_BETTING_PROFILE, betting: { ...MOCK_BETTING_PROFILE.betting, chipSelector: '.nothing' } }));
      expect((await checkTable(browser.port, dir)).join(' | ')).toContain('none found');
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
