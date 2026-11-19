import { afterEach, describe, expect, it } from 'vitest';
import { runTrial } from '../src/backtest/trial.js';
import { ClassicEngine, classicConfig } from '../src/engine/classic.js';
import { PlayCore } from '../src/engine/play.js';
import { CLASSIC_STRATEGIES, DEFAULT_SETTINGS, normalizeSettings, parseSides, type Settings } from '../src/engine/settings.js';
import { unsupportedReason } from '../src/engine/factory.js';
import type { Result } from '../src/engine/types.js';
import { cleanup, harness } from './helpers.js';

afterEach(cleanup);

const cfg = (over: Record<string, unknown> = {}): Settings =>
  normalizeSettings({ unit_value: 1, min_chip: 0, start_balance: 1000, hard_cap_mode: 'off', ...over }, DEFAULT_SETTINGS).settings;
const make = (over: Record<string, unknown> = {}): ClassicEngine => new ClassicEngine(classicConfig(cfg(over)));
const play = (e: ClassicEngine, results: string, paused = false) => [...results].map((r) => e.settle(r as Result, paused));
const stakes = (e: ClassicEngine) => Math.round(e.plan(false).units * 1e6) / 1e6;

describe('watching only (pause, wait, end of shoe)', () => {
  it('freezes the progression: no money, no stake or counter change, and says so', () => {
    const e = make({ strategy: 'GRINDING', first_side: 'P' });
    play(e, 'B'); // lose 1, now follows B at 1
    const before = JSON.stringify(e.snapshot());
    const rows = play(e, 'PBPPB', true);
    expect(rows.every((r) => r.pnlMicro === 0 && r.plan.mode === 'VIRTUAL')).toBe(true);
    expect(rows.map((r) => r.outcome)).toEqual(['V-LOSS', 'V-WIN', 'V-LOSS', 'V-LOSS', 'V-WIN']);
    expect(JSON.stringify({ ...(e.snapshot() as object), hands: 0 })).toBe(JSON.stringify({ ...JSON.parse(before), hands: 0 }));
  });

  it('Martingale still remembers who won while watching, so it follows the table when it bets again', () => {
    const e = make({ strategy: 'MARTINGALE', first_side: 'P' });
    play(e, 'B'); // lost 1 on P: next B at 2
    play(e, 'PP', true);
    expect(e.plan(false).side).toBe('P');
    expect(stakes(e)).toBe(2);
  });

  it('an ODD strategy that has no pick stays out and is reported as no bet, not as a virtual bet', () => {
    const e = make({ strategy: 'ODD_DIFF', progression: 'list', chain: [1, 2, 3] });
    expect(e.plan(false).mode).toBe('NONE');
    expect(e.plan(true).mode).toBe('NONE');
    const [r] = play(e, 'B', true);
    expect(r!.outcome).toBe('NO BET');
    expect(e.plan(false).mode).toBe('NONE'); // the paused result did not count
  });
});

describe('ties and bets that push', () => {
  it('a tie never moves a stake, a side or a counter, in any strategy', () => {
    for (const strategy of CLASSIC_STRATEGIES) {
      const a = make({ strategy, progression: 'list', chain: [1, 2, 3] });
      const b = make({ strategy, progression: 'list', chain: [1, 2, 3] });
      play(a, 'BPB');
      play(b, 'BPB');
      const r = play(a, 'TTT');
      expect(r.every((x) => x.pnlMicro === 0 && x.outcome === (x.plan.mode === 'NONE' ? 'NO BET' : 'TIE')), strategy).toBe(true);
      expect(JSON.stringify(a.snapshot()), strategy).toBe(JSON.stringify({ ...(b.snapshot() as object), hands: 6 }));
    }
  });
});

describe('the stake cap', () => {
  it('drops a runaway progression back to the base stake and counts the run as given up', () => {
    const e = make({ strategy: 'MARTINGALE', first_side: 'P', hard_cap_mode: 'custom', hard_cap_units: 5 });
    const rows = play(e, 'BPB'); // lose 1, 2, 4 -> next would be 8 (> 5)
    expect(rows.map((r) => r.plan.units)).toEqual([1, 2, 4]);
    expect(rows[2]!.note).toMatch(/cap exceeded/);
    expect(rows[2]!.runClosed).toBe(true);
    expect(e.counters()).toEqual({ runsClosed: 0, runsCut: 1 });
    expect(stakes(e)).toBe(1);
  });

  it('a huge multiplier cannot run away even with no cap set', () => {
    const e = make({ strategy: 'MARTINGALE', martingale_multiplier: 100, first_side: 'P' });
    for (let i = 0; i < 12; i++) e.settle(e.plan(false).side === 'P' ? 'B' : 'P', false);
    expect(e.plan(false).units).toBeLessThanOrEqual(1_000_000);
  });
});

describe('shoe changes', () => {
  const lost = (e: ClassicEngine) => {
    e.settle(e.plan(false).side === 'P' ? 'B' : 'P', false);
  };

  it('reset_all cuts the progression and the pattern (the PRD: never carry a losing sequence into a new shoe)', () => {
    const e = make({ strategy: 'MARTINGALE', first_side: 'P', shoe_change_policy: 'reset_all' });
    lost(e); lost(e);
    expect(stakes(e)).toBe(4);
    e.newShoe();
    expect([e.plan(false).side, stakes(e)]).toEqual(['P', 1]);
    expect(e.counters().runsCut).toBe(1);
  });

  it('reset_pattern keeps the stake but forgets the pattern', () => {
    const e = make({ strategy: 'MARTINGALE', first_side: 'P', shoe_change_policy: 'reset_pattern' });
    lost(e); lost(e);
    e.newShoe();
    expect([e.plan(false).side, stakes(e)]).toEqual(['P', 4]);
  });

  it('continue changes nothing, and the first shoe of a session is never a "change"', () => {
    const e = make({ strategy: 'GRINDING', first_side: 'P', shoe_change_policy: 'continue' });
    const fresh = JSON.stringify(e.snapshot());
    e.newShoe();
    expect(JSON.stringify(e.snapshot())).toBe(fresh);
    play(e, 'B');
    const mid = JSON.stringify(e.snapshot());
    e.newShoe();
    expect(JSON.stringify(e.snapshot())).toBe(mid);
    const reset = make({ strategy: 'GRINDING', shoe_change_policy: 'reset_all' });
    reset.newShoe();
    expect(JSON.stringify(reset.snapshot())).toBe(JSON.stringify(make({ strategy: 'GRINDING' }).snapshot()));
  });

  it('ODD strategies forget their balance in a new shoe and have no pick again until the first odd result', () => {
    const e = make({ strategy: 'ODD_IS_GOD', progression: 'list', chain: [1, 2], shoe_change_policy: 'reset_pattern' });
    play(e, 'B');
    expect(e.plan(false).side).toBe('B');
    e.newShoe();
    expect(e.plan(false).mode).toBe('NONE');
  });
});

describe('cutting a run and the status the risk layer reads', () => {
  it('cutRun returns the stake to its base and keeps the pattern; run_open follows the progression', () => {
    const e = make({ strategy: 'SEQUENCE', progression: 'list', chain: [1, 2, 4], sequence_sides: 'B,P,P' });
    expect(e.status().run_open).toBe(false);
    play(e, 'P'); // B loses
    expect(e.status().run_open).toBe(true);
    expect([e.plan(false).side, stakes(e)]).toEqual(['P', 2]);
    e.cutRun();
    expect(e.status().run_open).toBe(false);
    expect([e.plan(false).side, stakes(e)]).toEqual(['B', 1]);
  });

  it('snapshot and restore give a copy that behaves identically from then on', () => {
    const a = make({ strategy: 'ODD_LOOSER', progression: 'list', chain: [1, 2, 3] });
    play(a, 'BPPBPBBTP');
    const b = make({ strategy: 'ODD_LOOSER', progression: 'list', chain: [1, 2, 3] });
    b.restore(JSON.parse(JSON.stringify(a.snapshot())));
    const rest = 'PBBPTPBPPB';
    expect(play(b, rest).map((r) => [r.plan.side, r.plan.units, r.pnlMicro])).toEqual(play(a, rest).map((r) => [r.plan.side, r.plan.units, r.pnlMicro]));
  });

  it('applying new settings to a running session keeps its place and never points past a shorter list', () => {
    const e = make({ strategy: 'SEQUENCE', progression: 'list', chain: [1, 2, 3, 4], sequence_sides: 'B,P,P,B' });
    play(e, 'PPP'); // three losses on B, P?, ...
    e.configure(cfg({ strategy: 'SEQUENCE', progression: 'list', chain: [1, 2], sequence_sides: 'B,P' }));
    expect(() => e.plan(false)).not.toThrow();
    expect(e.plan(false).units).toBeLessThanOrEqual(2);
  });
});

describe('through the shared play core (risk layer, keep-alive, money)', () => {
  it('keep-alive on a hand with no pick bets the minimum on Player and is real money', () => {
    const settings = cfg({ strategy: 'ODD_DIFF', progression: 'list', chain: [1, 2, 3], keepalive_enabled: true, keepalive_amount: 0.5, keepalive_idle: 1, first_side: 'B' });
    const made = PlayCore.create(settings);
    if ('error' in made) throw new Error(made.error);
    const core = made.core;
    // Hand 1: the strategy has no pick, so the minimum bet rides on Player (and loses to a Banker win).
    const first = core.play({ result: 'B' }, false, 0);
    expect([first.mode, first.pick, first.stakeMicro, first.outcome, first.pnlMicro]).toEqual(['MIN', 'P', 500_000, 'LOSS', -500_000]);
    // Hand 2: the strategy now picks Banker and bets for real; no keep-alive is needed.
    const second = core.play({ result: 'B' }, false, 0);
    expect([second.mode, second.pick, second.stakeMicro]).toEqual(['REAL', 'B', 1_000_000]);
    expect(core.virtualHands).toBe(0);
    // Without keep-alive the same first hand is a plain "no bet".
    const plain = PlayCore.create({ ...settings, keepalive_enabled: false });
    if ('error' in plain) throw new Error(plain.error);
    const none = plain.core.play({ result: 'B' }, false, 0);
    expect([none.mode, none.pick, none.stakeMicro, none.outcome]).toEqual(['NONE', '', 0, 'NO BET']);
  });

  it('the consecutive-loss stop counts only real losses, not hands with no bet', () => {
    const settings = cfg({ strategy: 'MARTINGALE', first_side: 'P', stop_consec_loss_count: 3 });
    const made = PlayCore.create(settings);
    if ('error' in made) throw new Error(made.error);
    const core = made.core;
    let stop = null;
    for (const r of ['B', 'T', 'P', 'T', 'B']) stop = core.play({ result: r as Result }, false, 0).verdict.stop;
    expect(stop?.code).toBe('consecutive_losses'); // bets: P lost, B(follow) lost, P(follow) lost
  });
});

describe('settings the classic strategies accept and refuse', () => {
  it('reads the sides list and refuses a bad one', () => {
    expect(parseSides('b, P;p')).toEqual(['B', 'P', 'P']);
    expect(parseSides('')).toBeNull();
    expect(parseSides('B,X')).toBeNull();
    expect(parseSides(Array(65).fill('P').join(','))).toBeNull();
    expect(normalizeSettings({ strategy: 'SEQUENCE', sequence_sides: 'B,Q' }).errors[0]).toMatch(/Sequence sides/);
    expect(normalizeSettings({ strategy: 'MARTINGALE', sequence_sides: 'B,Q' }).errors).toEqual([]);
  });

  it('refuses what it cannot honour instead of ignoring it', () => {
    expect(unsupportedReason(cfg({ strategy: 'MARTINGALE', progression: 'list', chain: [1, 2] }))).toMatch(/one Base Unit/);
    expect(unsupportedReason(cfg({ strategy: 'GRINDING', stop_consec_loss_count: 3, stop_consec_loss_action: 'WAIT FOR PATTERN' }))).toMatch(/IHG feature/);
    expect(unsupportedReason(cfg({ strategy: 'ODD_EVEN', entry_loss_filter: 3 }))).toBeNull();
    expect(unsupportedReason(cfg({ strategy: 'SEQUENCE', progression: 'list', chain: [1, 2] }))).toBeNull();
    expect(unsupportedReason(cfg({ strategy: 'ODD_DIFF' }))).toBeNull();
    expect(normalizeSettings({ strategy: 'NOPE' }).settings.strategy).toBe('IHG');
  });
});

describe('every classic strategy keeps the books straight over thousands of hands', () => {
  it.each(CLASSIC_STRATEGIES)('%s: profit equals the sum of the bets, nothing is NaN, stakes stay positive', (strategy) => {
    const settings = cfg({ strategy, progression: 'list', chain: [1, 2, 3, 5], first_side: 'B', hard_cap_mode: 'custom', hard_cap_units: 400, take_profit: 0 });
    const useList = strategy === 'SEQUENCE' || strategy.startsWith('ODD');
    const s = useList ? settings : cfg({ strategy, hard_cap_mode: 'custom', hard_cap_units: 400 });
    const made = PlayCore.create(s);
    if ('error' in made) throw new Error(made.error);
    const core = made.core;
    let seed = 99;
    const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32);
    let sum = 0;
    for (let i = 0; i < 6000; i++) {
      const r: Result = rnd() < 0.1 ? 'T' : rnd() < 0.5 ? 'P' : 'B';
      const hand = core.play({ result: r, newShoe: i % 70 === 0 }, false, 0);
      sum += hand.pnlMicro;
      expect(Number.isFinite(hand.pnlMicro)).toBe(true);
      expect(hand.stakeMicro).toBeGreaterThanOrEqual(0);
      if (hand.mode === 'NONE') expect(hand.stakeMicro).toBe(0);
    }
    expect(core.pnlMicro).toBe(sum);
    expect(core.hands).toBe(6000);
  });
});

describe('in a backtest and through the program', () => {
  it('a backtest runs every classic strategy on simulated shoes', () => {
    for (const strategy of CLASSIC_STRATEGIES) {
      const useList = strategy === 'SEQUENCE' || strategy.startsWith('ODD');
      const settings = cfg(useList ? { strategy, progression: 'list', chain: [1, 2, 4] } : { strategy });
      const r = runTrial({ settings, shoes: { kind: 'sim', count: 6, seed: 3 }, bankrollUnits: 0, curvePoints: 20 });
      expect(Number.isFinite(r.pnlUnits), strategy).toBe(true);
      expect(r.curve.length, strategy).toBeGreaterThan(0);
    }
  });

  it('starts from the page contract, shows hands with no pick as no bet, and writes them to the log', async () => {
    const h = harness();
    const settings = { feed: 'manual', strategy: 'ODD_DIFF', unit_value: 1, min_chip: 0, start_balance: 1000, progression: 'list', chain: [1, 2, 3] };
    expect((await h.post('/api/start', { settings })).json.ok).toBe(true);
    const state0 = (await h.get('/api/state?since=0')).json;
    expect(state0.next).toMatchObject({ mode: 'NONE', pick: '' });
    for (const r of ['B', 'P', 'P']) await h.post('/api/manual', { result: r });
    const state = (await h.get('/api/state?since=0')).json;
    expect(state.rows.map((x: { pick: string; mode: string; outcome: string }) => [x.pick, x.mode, x.outcome])).toEqual([
      ['', 'NONE', 'NO BET'], ['B', 'REAL', 'LOSS'], ['B', 'REAL', 'LOSS'],
    ]);
    expect(state.pnl).toBe(-3);
    const csv = (await h.get('/api/report?fmt=csv')).text;
    expect(csv).toContain('NO BET');
    expect(csv.split('\r\n')[1]).toContain('ODD_DIFF');
  });

  it('refuses at start, with the reason, what a classic strategy cannot do', async () => {
    const h = harness();
    const r = await h.post('/api/start', { settings: { feed: 'manual', strategy: 'MARTINGALE', progression: 'list', chain: [1, 2] } });
    expect(r.json.ok).toBe(false);
    expect(r.json.status).toMatch(/one Base Unit/);
  });
});
