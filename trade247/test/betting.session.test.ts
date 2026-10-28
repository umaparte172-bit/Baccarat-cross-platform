import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { cleanup, harness, type Harness } from './helpers.js';
import { findChrome, openTab, startChrome, until, type TestChrome } from './chrome.js';
import { MOCK_BETTING_PROFILE, startMockBetting, type MockBetting } from './mockBetting.js';

afterEach(cleanup);

const chrome = findChrome();
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

describe('refusing to bet when it is not safe to', () => {
  const start = (h: Harness, settings: Record<string, unknown>) =>
    h.post('/api/start', { settings: { feed: 'manual', unit_value: 1, min_chip: 0, start_balance: 1000, ...settings } });

  it('needs the live feed, a table profile with betting, and both safety limits', async () => {
    let h = harness();
    expect((await start(h, { bet_mode: 'auto' })).json.status).toMatch(/Live table feed/);
    expect((await start(h, { feed: 'live', bet_mode: 'confirm', bet_max_stake: 5, bet_max_session: 50 })).json.status).toMatch(/no "betting" settings/);

    const dir = mkdtempSync(join(tmpdir(), 't247-bet-'));
    writeFileSync(join(dir, 'live-profile.json'), JSON.stringify(MOCK_BETTING_PROFILE));
    h = harness(dir);
    expect((await start(h, { feed: 'live', bet_mode: 'auto' })).json.status).toMatch(/largest allowed stake/);
    expect((await start(h, { feed: 'live', bet_mode: 'auto', bet_max_stake: 10, bet_max_session: 5 })).json.status).toMatch(/at least the largest stake/);
  });

  it('keeps paper trading as the default, and the mode of a running session cannot be switched on the fly', async () => {
    const h = harness();
    expect((await h.get('/api/state')).json.settings.bet_mode).toBe('paper');
    expect((await h.get('/api/state')).json.bet).toMatchObject({ mode: 'paper', pending: null, on_table: false });
    expect((await start(h, {})).json.ok).toBe(true);
    await h.post('/api/apply', { bet_mode: 'auto', bet_max_stake: 5, bet_max_session: 50 });
    expect(h.manager.currentSettings().bet_mode).toBe('paper');
  });

  it('there is nothing to approve when no bet is waiting', async () => {
    const h = harness();
    expect((await h.post('/api/bet/approve', {})).json).toEqual({ ok: false, error: 'No bet is waiting for approval' });
    expect((await h.post('/api/bet/skip', {})).json).toEqual({ ok: false, error: 'No bet is waiting for approval' });
  });
});

describe.skipIf(!chrome)('betting through the program, on a mock table in a real Chrome', () => {
  let browser: TestChrome;
  let table: MockBetting;

  beforeAll(async () => {
    browser = await startChrome(chrome!);
    table = await startMockBetting();
    await openTab(browser.port, table.pageUrl);
    await sleep(600);
  }, 30_000);

  afterAll(async () => {
    await table?.stop();
    await browser?.stop();
  });

  beforeEach(async () => {
    const s = table.state;
    Object.assign(s, { open: true, status: 'PLACE YOUR BETS', total: 0, selected: null, clicks: [], placedOn: [], mode: 'normal', coverBanker: false, cells: [] });
    await sleep(250);
  });

  const session = () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-betlive-'));
    writeFileSync(join(dir, 'live-profile.json'), JSON.stringify(MOCK_BETTING_PROFILE));
    return harness(dir);
  };
  const settings = (over: Record<string, unknown> = {}) => ({
    feed: 'live', live_port: browser.port, bet_mode: 'auto', bet_max_stake: 50, bet_max_session: 500, bet_delay_min: 0, bet_delay_max: 0,
    strategy: 'IHG', unit_value: 1, min_chip: 1, start_balance: 1000, ...over,
  });
  const state = (h: Harness) => h.manager.getState(0);
  /** The table plays a round: bets close, the result goes on the road, the next window opens. */
  const deal = async (result: 'P' | 'B' | 'T') => {
    const s = table.state;
    s.open = false;
    s.status = 'BETS CLOSED';
    await sleep(250);
    s.cells.push(result);
    s.total = 0;
    await sleep(350);
    s.open = true;
    s.status = 'PLACE YOUR BETS';
  };

  it('places each bet the strategy wants, with real clicks, and books the results against what was placed', async () => {
    const h = session();
    expect((await h.post('/api/start', { settings: settings() })).json.ok).toBe(true);
    await until(() => table.state.total === 1, 15_000); // IHG: first bet is Player, one unit
    expect(table.state.placedOn).toEqual(['player']);
    expect(state(h).bet.on_table).toBe(true);

    await deal('B'); // the Player bet loses
    await until(() => table.state.total === 1, 15_000); // next bet: one round -> the same as the last result, Banker, one unit
    expect(table.state.placedOn).toEqual(['player', 'banker']);

    await deal('B'); // Banker wins 0.95; run net -0.05 -> next stake ceil(0.05)+1 = 2, made of two 1-chips
    await until(() => table.state.total === 2, 15_000);
    expect(table.state.placedOn).toEqual(['player', 'banker', 'banker', 'banker']);

    await until(() => state(h).hands === 2);
    const rows = state(h).rows;
    expect(rows.map((r: { pick: string; mode: string; outcome: string; stake: string }) => [r.pick, r.mode, r.outcome, r.stake])).toEqual([
      ['P', 'REAL', 'LOSS', '1.00'], ['B', 'REAL', 'WIN', '1.00'],
    ]);
    expect(state(h).pnl).toBe(-0.05);
    await until(() => state(h).bet.staked === 4, 5_000); // 1 + 1 + 2
    expect(table.state.clicks.every((c) => c.trusted)).toBe(true);
    await h.post('/api/stop', {});
  }, 60_000);

  it('confirm mode waits for the click on "Place bet", and skipping books the hand as only watched', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings({ bet_mode: 'confirm', bet_confirm_seconds: 30 }) });
    await until(() => state(h).bet.pending !== null, 15_000);
    expect(state(h).bet.pending).toMatchObject({ pick: 'PLAYER', stake: 1, keep_alive: false });
    await sleep(500);
    expect(table.state.clicks).toHaveLength(0); // nothing is clicked without the user

    expect((await h.post('/api/bet/skip', {})).json.ok).toBe(true);
    expect(state(h).bet.pending).toBeNull();
    await deal('P');
    await until(() => state(h).hands === 1, 15_000);
    const row = state(h).rows[0]!;
    expect([row.mode, row.stake]).toEqual(['VIRTUAL', '0.00']);
    expect(row.note).toContain('bet skipped by you');
    expect(state(h).pnl).toBe(0);

    // The next bet is approved and placed.
    await until(() => state(h).bet.pending !== null, 15_000);
    expect((await h.post('/api/bet/approve', {})).json.ok).toBe(true);
    await until(() => table.state.total > 0, 15_000);
    expect(table.state.placedOn).toHaveLength(1);
    await h.post('/api/stop', {});
  }, 60_000);

  it('a bet that is not approved in time is skipped', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings({ bet_mode: 'confirm', bet_confirm_seconds: 3 }) });
    await until(() => state(h).bet.pending !== null, 15_000);
    await until(() => state(h).bet.pending === null, 8_000);
    expect(state(h).bet.note).toContain('not approved in time');
    expect(table.state.clicks).toHaveLength(0);
    await h.post('/api/stop', {});
  }, 40_000);

  it('stops the session, and says so, when the table does not confirm the bet', async () => {
    const h = session();
    table.state.mode = 'noconfirm';
    await h.post('/api/start', { settings: settings() });
    await until(() => !state(h).running, 20_000);
    expect(state(h).status).toContain('bet not confirmed');
    expect(state(h).error).toContain('did not confirm');
    expect(table.state.placedOn).toEqual(['player']); // never retried
  }, 40_000);

  it('never places a bet above the safety limit: it stops instead', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings({ bet_max_stake: 1 }) });
    await until(() => table.state.total === 1, 15_000);
    await deal('B');
    await until(() => table.state.total === 1, 15_000); // 1 on Banker
    await deal('B'); // the win makes the next stake 2, above the limit of 1
    await until(() => !state(h).running, 20_000);
    expect(state(h).status).toContain('safety limit');
    expect(table.state.placedOn).toEqual(['player', 'banker']); // the 2-unit bet was never placed
  }, 60_000);

  it('stops at the session staking limit', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings({ bet_max_stake: 2, bet_max_session: 2 }) });
    await until(() => table.state.total === 1, 15_000);
    await deal('B');
    await until(() => table.state.total === 1 && table.state.placedOn.length === 2, 15_000);
    await deal('B'); // next stake 2 would take the session to 4
    await until(() => !state(h).running, 20_000);
    expect(state(h).status).toContain('staking limit');
    expect(table.state.placedOn).toHaveLength(2);
  }, 60_000);

  it('stops when what is on the table is not what the strategy would bet (a shoe change after the bet)', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings({ shoe_change_policy: 'reset_all' }) });
    await until(() => table.state.total === 1, 15_000);
    await deal('B');
    await until(() => table.state.total === 1 && table.state.placedOn.length === 2, 15_000);
    await deal('B'); // 2 units next
    await until(() => table.state.total === 2, 15_000);
    // A new shoe starts while that bet is on the table: the strategy resets to one unit, the table still holds two.
    table.state.cells = [];
    await sleep(500);
    table.state.cells = ['P'];
    await until(() => !state(h).running, 20_000);
    expect(state(h).status).toContain('differs from the strategy');
    expect(state(h).hands).toBe(2); // the mismatched hand is not booked
  }, 60_000);

  it('pausing while a bet is on the table waits for its result', async () => {
    const h = session();
    await h.post('/api/start', { settings: settings() });
    await until(() => table.state.total === 1, 15_000);
    await h.post('/api/pause', {});
    expect(state(h).status).toContain('PAUSING');
    expect(state(h).paused).toBe(false);
    await deal('P');
    await until(() => state(h).paused, 15_000);
    expect(state(h).hands).toBe(1);
    expect(state(h).rows[0]).toMatchObject({ mode: 'REAL', outcome: 'WIN' });
    await sleep(2000);
    expect(table.state.placedOn).toHaveLength(1); // no new bet while paused
    await h.post('/api/stop', {});
  }, 60_000);

  it('a bet the table refuses to take (window closed at the moment of clicking) is booked as a hand that was only watched', async () => {
    const h = session();
    table.state.open = false;
    table.state.status = 'BETS CLOSED';
    await sleep(300);
    await h.post('/api/start', { settings: settings() });
    await sleep(1500);
    expect(table.state.clicks).toHaveLength(0); // nothing is clicked while bets are closed
    await deal('P'); // the result arrives without a bet: the strategy wanted one, none was placed
    await until(() => state(h).hands === 1, 15_000);
    expect(state(h).rows[0]).toMatchObject({ mode: 'VIRTUAL', stake: '0.00' });
    expect(state(h).pnl).toBe(0);
    await h.post('/api/stop', {});
  }, 60_000);
});
