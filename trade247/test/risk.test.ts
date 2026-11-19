import { mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { IhgEngine, type IhgConfig } from '../src/engine/ihg.js';
import { toMicro } from '../src/engine/money.js';
import { RiskGovernor, dailyLockReason, keepAliveBet, riskConfig, type RiskConfig } from '../src/engine/risk.js';
import { DEFAULT_SETTINGS, normalizeSettings } from '../src/engine/settings.js';
import { cleanup, harness, type Harness } from './helpers.js';

afterEach(cleanup);

const BASE = { unit_value: 1, min_chip: 0, start_balance: 1000 };
const cfg = (over: Partial<RiskConfig> = {}): RiskConfig => ({ ...riskConfig(DEFAULT_SETTINGS), ...over });

describe('keep-alive bet (pure)', () => {
  it('follows the pick, pays Player 1:1 and Banker 0.95, and pushes on a tie', () => {
    const c = cfg({ keepAliveMicro: toMicro(0.4), chipMicro: 0 });
    expect(keepAliveBet('P', 'P', c)).toEqual({ outcome: 'WIN', stakeMicro: toMicro(0.4), pnlMicro: toMicro(0.4) });
    expect(keepAliveBet('B', 'B', c)).toMatchObject({ outcome: 'WIN', pnlMicro: toMicro(0.38) });
    expect(keepAliveBet('P', 'B', c)).toMatchObject({ outcome: 'LOSS', pnlMicro: toMicro(-0.4) });
    expect(keepAliveBet('P', 'T', c)).toMatchObject({ outcome: 'TIE', pnlMicro: 0 });
  });

  it('rounds the stake up to the minimum chip', () => {
    expect(keepAliveBet('P', 'P', cfg({ keepAliveMicro: toMicro(0.1), chipMicro: toMicro(0.25) })).stakeMicro).toBe(toMicro(0.25));
  });
});

describe('daily lock reason (pure)', () => {
  it('reports a reached daily limit and nothing otherwise', () => {
    const c = cfg({ dailyStopLossMicro: toMicro(5), dailyTakeProfitMicro: toMicro(8) });
    expect(dailyLockReason(c, toMicro(-4.99))).toBeNull();
    expect(dailyLockReason(c, toMicro(-5))).toMatch(/Daily stop loss already reached today \(-5\.00\)/);
    expect(dailyLockReason(c, toMicro(8))).toMatch(/Daily take profit already reached today \(\+8\.00\)/);
    expect(dailyLockReason(cfg(), toMicro(-999))).toBeNull();
  });
});

describe('governor decisions (pure)', () => {
  const U = 1_000_000;
  const ihg: IhgConfig = { unitMicro: U, chipMicro: 0, stepUnitsMicro: U, runEndUnits: 45, hardCapUnits: null, singlesLimit: 7, shoePolicy: 'continue' };

  it('prefers a locked daily limit over a session limit and a loss stop', () => {
    const g = new RiskGovernor(cfg({ stopLossMicro: toMicro(1), dailyStopLossMicro: toMicro(2), stopAfterLosses: 1, stopAction: 'STOP BET' }));
    const e = new IhgEngine(ihg);
    const hand = g.prepare(e, false);
    const res = e.settle('B', false); // Player bet loses
    const verdict = g.settled({ res, hand, manualPause: false, keepAlive: false, sessionPnlMicro: toMicro(-1), dayPnlMicro: toMicro(-2) });
    expect(verdict.stop).toMatchObject({ status: 'LOCKED (daily stop loss)', locked: true });
  });

  it('counts hands without a bet and asks for the keep-alive on the Nth, then starts counting again', () => {
    const g = new RiskGovernor(cfg({ keepAliveEnabled: true, keepAliveMicro: toMicro(0.1), keepAliveIdle: 3, stopAfterLosses: 1, stopAction: 'WAIT FOR PATTERN' }));
    const e = new IhgEngine(ihg);
    // One real loss puts the governor into "waiting", so the following hands are not bet.
    let hand = g.prepare(e, false);
    g.settled({ res: e.settle('B', false), hand, manualPause: false, keepAlive: false, sessionPnlMicro: 0, dayPnlMicro: 0 });
    const flags: boolean[] = [];
    for (let i = 0; i < 7; i++) {
      hand = g.prepare(e, false);
      flags.push(hand.keepAlive);
      // Feed results the waiting pick would lose, so the wait does not end during the test.
      const losing = e.plan(true).side === 'P' ? 'B' : 'P';
      g.settled({ res: e.settle(losing, true), hand, manualPause: false, keepAlive: hand.keepAlive, sessionPnlMicro: 0, dayPnlMicro: 0 });
    }
    expect(flags).toEqual([false, false, true, false, false, true, false]);
  });

  it('a manual pause freezes the idle count and never places a keep-alive', () => {
    const g = new RiskGovernor(cfg({ keepAliveEnabled: true, keepAliveMicro: toMicro(0.1), keepAliveIdle: 1 }));
    const e = new IhgEngine(ihg);
    const hand = g.prepare(e, true);
    expect(hand.keepAlive).toBe(false);
    g.settled({ res: e.settle('P', true), hand, manualPause: true, keepAlive: false, sessionPnlMicro: 0, dayPnlMicro: 0 });
    expect(g.status().idle_hands).toBe(0);
  });

  it('prepare() changes nothing', () => {
    const g = new RiskGovernor(cfg({ endShoePolicy: 'cut_run', endShoeHand: 1 }));
    const e = new IhgEngine(ihg);
    const before = JSON.stringify([g.status(), e.snapshot()]);
    g.prepare(e, false);
    g.prepare(e, false);
    expect(JSON.stringify([g.status(), e.snapshot()])).toBe(before);
  });
});

describe('client-reference virtual entry gate', () => {
  it('holds JJ in virtual mode until the requested number of virtual losses', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, strategy: 'JJ', feed: 'manual', entry_loss_filter: 2 } });
    for (let i = 0; i < 2; i++) {
      const before = (await h.get('/api/state')).json;
      expect(before.next.mode).toBe('VIRTUAL');
      const loss = before.next.pick === 'PLAYER' ? 'B' : 'P';
      await h.post('/api/manual', { result: loss });
    }
    const ready = (await h.get('/api/state?since=0')).json;
    expect(ready.rows.map((r: { mode: string }) => r.mode)).toEqual(['VIRTUAL', 'VIRTUAL']);
    expect(ready.next.mode).toBe('REAL');
    expect(ready.strategy_state).toMatchObject({ entry_waiting: false, entry_losses: 0 });
  });
});

describe('settings validation for the risk layer', () => {
  it('needs a pause length when a pause trigger is set, and a positive keep-alive amount', () => {
    expect(normalizeSettings({ pause_consec_loss_count: 3, pause_bet_count: 0 }).errors[0]).toMatch(/how many hands to pause/);
    expect(normalizeSettings({ keepalive_enabled: true, keepalive_amount: 0 }).errors[0]).toMatch(/Keep-Alive amount must be above 0/);
    expect(normalizeSettings({ pause_consec_loss_count: 3, pause_bet_count: 5, keepalive_enabled: true, keepalive_amount: 0.1 }).errors).toEqual([]);
  });
});

// ---------------------------------------------------------------------------------------------
// End to end: the same wiring the page uses, driven by deterministic replays and manual results.

const replay = async (h: Harness, letters: string, extra: Record<string, unknown> = {}) => {
  const started = (await h.post('/api/start', { settings: { ...BASE, feed: 'csv', csv_text: letters, ...extra } })).json;
  expect(started.ok, started.status).toBe(true);
  return vi.waitFor(async () => {
    const s = (await h.get('/api/state?since=0')).json;
    expect(s.running).toBe(false);
    return s;
  }, { timeout: 5000, interval: 10 });
};
const modes = (s: any): string[] => s.rows.map((r: any) => r.mode);

describe('take profit and stop loss', () => {
  it('stops the session as soon as the take profit is reached', async () => {
    const h = harness();
    const s = await replay(h, 'PPPPP', { take_profit: 3 });
    expect(s.status).toBe('STOPPED (take profit)');
    expect(s.rows).toHaveLength(3);
    expect(s.pnl).toBe(3);
  });

  it('stops the session as soon as the stop loss is reached', async () => {
    const h = harness();
    const s = await replay(h, 'BPPPP', { stop_loss: 3 });
    expect(s.status).toBe('STOPPED (stop loss)');
    expect(s.rows.map((r: any) => r.outcome)).toEqual(['LOSS', 'LOSS', 'LOSS']);
    expect(s.pnl).toBe(-3);
  });

  it('a limit changed with Apply takes effect on the running session', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, feed: 'manual' } });
    await h.post('/api/manual', { result: 'P' }); // +1
    expect((await h.post('/api/apply', { take_profit: 2 })).json).toEqual({ ok: true, live: true });
    await h.post('/api/manual', { result: 'P' }); // +2 reaches it
    const s = (await h.get('/api/state')).json;
    expect(s).toMatchObject({ running: false, status: 'STOPPED (take profit)' });
  });
});

describe('daily limits', () => {
  it('locks the day at the daily stop loss and refuses to start again, even after a restart', async () => {
    const h = harness();
    const s = await replay(h, 'BPPPP', { daily_stop_loss: 2 });
    expect(s.status).toBe('LOCKED (daily stop loss)');
    expect(s.rows).toHaveLength(2);
    expect(s.day_pnl).toBe(-2);

    const again = (await h.post('/api/start', { settings: { ...BASE, feed: 'manual', daily_stop_loss: 2 } })).json;
    expect(again).toMatchObject({ ok: false });
    expect(again.status).toMatch(/Daily stop loss already reached today \(-2\.00\)/);

    const dir = h.dir;
    await h.close();
    const restarted = harness(dir);
    expect((await restarted.get('/api/state')).json.day_pnl).toBe(-2);
    expect((await restarted.post('/api/start', { settings: { ...BASE, feed: 'manual', daily_stop_loss: 2 } })).json.ok).toBe(false);
    // Raising the limit unlocks the day.
    expect((await restarted.post('/api/start', { settings: { ...BASE, feed: 'manual', daily_stop_loss: 5 } })).json.ok).toBe(true);
  });

  it('adds up every session of the day', async () => {
    const h = harness();
    await replay(h, 'B', { daily_stop_loss: 2 }); // -1
    expect((await h.get('/api/state')).json.day_pnl).toBe(-1);
    await h.post('/api/reset');
    const second = await replay(h, 'BP', { daily_stop_loss: 2 }); // first hand -1 reaches -2 for the day
    expect(second.status).toBe('LOCKED (daily stop loss)');
    expect(second.rows).toHaveLength(1);
    expect(second.day_pnl).toBe(-2);
  });

  it('locks the day at the daily take profit', async () => {
    const h = harness();
    const s = await replay(h, 'PPPP', { daily_take_profit: 2 });
    expect(s.status).toBe('LOCKED (daily take profit)');
    expect(s.rows).toHaveLength(2);
    const again = (await h.post('/api/start', { settings: { ...BASE, feed: 'manual', daily_take_profit: 2 } })).json;
    expect(again.status).toMatch(/Daily take profit already reached today \(\+2\.00\)/);
  });

  it('forgets a total saved on an earlier day', async () => {
    const dir = mkdtempSync(join(tmpdir(), 't247-day-'));
    writeFileSync(join(dir, 'day.json'), JSON.stringify({ day: '2000-01-01', pnlMicro: -50_000_000 }));
    const h = harness(dir);
    expect((await h.get('/api/state')).json.day_pnl).toBe(0);
    expect((await h.post('/api/start', { settings: { ...BASE, feed: 'manual', daily_stop_loss: 1 } })).json.ok).toBe(true);
  });

  it('saves the day total to disk when the session ends', async () => {
    const h = harness();
    await replay(h, 'PP');
    expect(JSON.parse(readFileSync(join(h.dir, 'day.json'), 'utf8'))).toMatchObject({ pnlMicro: 2_000_000 });
  });
});

describe('consecutive losses', () => {
  it('STOP BET ends the session at N real losses in a row, and a win in between resets the count', async () => {
    const h = harness();
    const s = await replay(h, 'BBPPPP', { stop_consec_loss_count: 2, stop_consec_loss_action: 'STOP BET' });
    // L, W (resets), L, L -> stop on hand 4.
    expect(s.rows.map((r: any) => r.outcome)).toEqual(['LOSS', 'WIN', 'LOSS', 'LOSS']);
    expect(s.status).toBe('STOPPED (2 consecutive losses)');
  });

  it('pauses for the chosen number of hands, counting down, then resumes', async () => {
    const h = harness();
    const s = await replay(h, 'BPPPPPP', { pause_consec_loss_count: 2, pause_bet_count: 3 });
    expect(modes(s)).toEqual(['REAL', 'REAL', 'VIRTUAL', 'VIRTUAL', 'VIRTUAL', 'REAL', 'REAL']);
    expect(s.rows.slice(2, 5).map((r: any) => r.note)).toEqual([
      'Paused after 2 losses - 3 hand(s) left',
      'Paused after 2 losses - 2 hand(s) left',
      'Paused after 2 losses - 1 hand(s) left',
    ]);
    // No money moves while paused.
    expect(s.rows[2].session_pnl).toBe(s.rows[1].session_pnl);
    expect(s.rows[4].session_pnl).toBe(s.rows[1].session_pnl);
  });

  it('WAIT FOR PATTERN watches with virtual picks and starts again from scratch after 2 winning picks', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, feed: 'manual', stop_consec_loss_count: 2, stop_consec_loss_action: 'WAIT FOR PATTERN' } });
    await h.post('/api/manual', { result: 'B' }); // L
    await h.post('/api/manual', { result: 'P' }); // L -> waiting
    let s = (await h.get('/api/state?since=0')).json;
    expect(s.next).toMatchObject({ mode: 'VIRTUAL' });
    expect(s.next.reason).toBe('Waiting for 2 winning picks in a row (0/2)');
    expect(s.strategy_state.waiting).toBe(true);
    const pnl = s.pnl;

    for (const left of ['1/2', '2/2']) {
      s = (await h.get('/api/state')).json;
      await h.post('/api/manual', { result: s.next.pick[0] }); // the pick wins
      s = (await h.get('/api/state')).json;
      if (left === '1/2') expect(s.next.reason).toBe('Waiting for 2 winning picks in a row (1/2)');
    }
    s = (await h.get('/api/state?since=0')).json;
    expect(s.pnl).toBe(pnl);
    expect(modes(s)).toEqual(['REAL', 'REAL', 'VIRTUAL', 'VIRTUAL']);
    expect(s.strategy_state).toMatchObject({ waiting: false, run_net_units: 0, run_open: false });
    expect(s.next).toMatchObject({ mode: 'REAL', pick: 'PLAYER', units: 1, pattern: 'First bet' });
  });

  it('WAIT FOR PATTERN needs the winning picks to be consecutive: a losing pick starts the count over', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, feed: 'manual', stop_consec_loss_count: 2, stop_consec_loss_action: 'WAIT FOR PATTERN' } });
    await h.post('/api/manual', { result: 'B' });
    await h.post('/api/manual', { result: 'P' }); // waiting
    const pick = async (wins: boolean): Promise<any> => {
      const next = (await h.get('/api/state')).json.next;
      const winner = next.pick[0];
      await h.post('/api/manual', { result: wins ? winner : winner === 'P' ? 'B' : 'P' });
      return (await h.get('/api/state')).json;
    };
    expect((await pick(true)).next.reason).toBe('Waiting for 2 winning picks in a row (1/2)');
    expect((await pick(false)).next.reason).toBe('Waiting for 2 winning picks in a row (0/2)');
    expect((await pick(true)).next.reason).toBe('Waiting for 2 winning picks in a row (1/2)');
    const done = await pick(true);
    expect(done.next).toMatchObject({ mode: 'REAL', pattern: 'First bet' });
    expect(done.strategy_state.waiting).toBe(false);
  });

  it('a manual PAUSE freezes a running loss pause', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, feed: 'manual', pause_consec_loss_count: 2, pause_bet_count: 3 } });
    await h.post('/api/manual', { result: 'B' });
    await h.post('/api/manual', { result: 'P' }); // pause of 3 hands begins
    await h.post('/api/pause');
    for (let i = 0; i < 4; i++) await h.post('/api/manual', { result: 'P' });
    await h.post('/api/resume');
    const s = (await h.get('/api/state')).json;
    expect(s.next.reason).toBe('Paused after 2 losses - 3 hand(s) left');
    expect(s.strategy_state.pause_left).toBe(3);
  });
});

describe('keep-alive', () => {
  it('places the minimum bet on every Nth hand the strategy sits out, with real money', async () => {
    const h = harness();
    const s = await replay(h, 'BPBPBP', {
      keepalive_enabled: true, keepalive_amount: 0.4, keepalive_idle: 2, pause_consec_loss_count: 1, pause_bet_count: 4,
    });
    expect(modes(s)).toEqual(['REAL', 'VIRTUAL', 'MIN', 'VIRTUAL', 'MIN', 'REAL']);
    expect(s.virtual_hands).toBe(2);

    const pnlAfter = (i: number): number => Number(s.rows[i].session_pnl);
    for (const i of [2, 4]) {
      const row = s.rows[i];
      expect(row.stake).toBe('0.40');
      const delta = Math.round((pnlAfter(i) - pnlAfter(i - 1)) * 100) / 100;
      const expected = row.outcome === 'LOSS' ? -0.4 : row.outcome === 'TIE' ? 0 : row.pick === 'B' ? 0.38 : 0.4;
      expect(delta, `row ${i + 1} ${row.outcome} on ${row.pick}`).toBe(expected);
      expect(row.note).toMatch(/keep-alive minimum bet/);
    }
    // Only strategy bets count towards the win rate.
    expect(s.win_rate).toBeGreaterThanOrEqual(0);
  });

  it('does nothing when every hand is a real bet', async () => {
    const h = harness();
    const s = await replay(h, 'PPPPPP', { keepalive_enabled: true, keepalive_amount: 0.4, keepalive_idle: 2 });
    expect(modes(s)).toEqual(Array(6).fill('REAL'));
  });

  it('shows the keep-alive as the next bet before it happens', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...BASE, feed: 'manual', keepalive_enabled: true, keepalive_amount: 0.4, keepalive_idle: 1, pause_consec_loss_count: 1, pause_bet_count: 5 } });
    await h.post('/api/manual', { result: 'B' }); // loss starts a 5-hand pause
    const s = (await h.get('/api/state')).json;
    expect(s.next).toMatchObject({ mode: 'REAL', stake: 0.4, pattern: 'keep-alive' });
  });
});

describe('near the end of a shoe', () => {
  const shoe = { end_shoe_hand: 3, shoe_change_policy: 'reset_all' };

  it('"no new runs" lets an open run finish and then only watches until the next shoe', async () => {
    const h = harness();
    // Hand 1 loses (run open), hand 2 wins partway, hand 3 (>= 3) still plays because the run is open and
    // wins it back; hand 4 would start a new run, so it is only watched.
    const s = await replay(h, 'BBBB', { ...shoe, end_shoe_policy: 'no_new_runs' });
    expect(modes(s)).toEqual(['REAL', 'REAL', 'REAL', 'VIRTUAL']);
    expect(s.rows[3].note).toBe('Near shoe end (hand 4) - no new runs');
  });

  it('"no new runs" with no open run watches from the chosen hand, and a new shoe starts betting again', async () => {
    const h = harness();
    const s = await replay(h, 'PPPP|PP', { ...shoe, end_shoe_policy: 'no_new_runs' });
    expect(modes(s)).toEqual(['REAL', 'REAL', 'VIRTUAL', 'VIRTUAL', 'REAL', 'REAL']);
    expect(s.rows[2].note).toBe('Near shoe end (hand 3) - no new runs');
  });

  it('"cut open run" drops the open run at the chosen hand and then only watches', async () => {
    const h = harness();
    const s = await replay(h, 'BBBB', { ...shoe, end_shoe_policy: 'cut_run' });
    expect(modes(s)).toEqual(['REAL', 'REAL', 'VIRTUAL', 'VIRTUAL']);
  });

  it('does nothing before the chosen hand and when the policy is off', async () => {
    const h = harness();
    expect(modes(await replay(h, 'PPPPP', { ...shoe, end_shoe_policy: 'off' }))).toEqual(Array(5).fill('REAL'));
  });
});

describe('what is still refused', () => {
  it('rejects a pause trigger without a pause length and a zero keep-alive amount at Start', async () => {
    const h = harness();
    expect((await h.post('/api/start', { settings: { ...BASE, feed: 'manual', pause_consec_loss_count: 3, pause_bet_count: 0 } })).json.status).toMatch(/how many hands to pause/);
    expect((await h.post('/api/start', { settings: { ...BASE, feed: 'manual', keepalive_enabled: true, keepalive_amount: 0 } })).json.status).toMatch(/Keep-Alive amount must be above 0/);
  });
});
