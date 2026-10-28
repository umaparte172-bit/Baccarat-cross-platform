import { readFileSync } from 'node:fs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { aggregate, histogram, percentile } from '../src/backtest/stats.js';
import { curveSteps, parsePastedShoes, runTrial, trialSeed, type TrialResult, type TrialSpec } from '../src/backtest/trial.js';
import { Shoe, dealHand, dealResult, mulberry32 } from '../src/engine/dealer.js';
import { IhgEngine, ihgConfig } from '../src/engine/ihg.js';
import { DEFAULT_SETTINGS, normalizeSettings, type Settings } from '../src/engine/settings.js';
import type { Result } from '../src/engine/types.js';
import { cleanup, harness } from './helpers.js';

afterEach(cleanup);

const settings = (over: Record<string, unknown> = {}): Settings =>
  normalizeSettings({ unit_value: 1, min_chip: 0, start_balance: 1000, ...over }, DEFAULT_SETTINGS).settings;

const spec = (over: Record<string, unknown> = {}, shoes: TrialSpec['shoes'] = { kind: 'sim', count: 10, seed: 7 }, bankrollUnits = 0): TrialSpec => ({
  settings: settings(over), shoes, bankrollUnits, curvePoints: 120,
});

const paste = (...shoes: Result[][]): TrialSpec['shoes'] => ({ kind: 'paste', shoes });

/** A shoe in which every hand goes against the strategy's own pick, so every bet loses one unit. */
function losingShoe(length: number): Result[] {
  const engine = new IhgEngine(ihgConfig(settings()));
  const out: Result[] = [];
  for (let i = 0; i < length; i++) {
    const result: Result = engine.plan(false).side === 'P' ? 'B' : 'P';
    engine.settle(result, false);
    out.push(result);
  }
  return out;
}

/** Results of `count` simulated shoes, as the text a Replay or a pasted backtest takes ("|" between shoes). */
function shoeText(seed: number, count: number): { text: string; shoes: Result[][] } {
  const rng = mulberry32(seed);
  const shoes: Result[][] = [];
  for (let i = 0; i < count; i++) {
    const shoe = new Shoe(rng);
    const hands: Result[] = [];
    do hands.push(dealHand(shoe).result);
    while (!shoe.cutReached);
    shoes.push(hands);
  }
  return { text: shoes.map((s) => s.join('')).join('|'), shoes };
}

describe('the fast dealer', () => {
  it('deals exactly the same hands as the card-by-card dealer, shoe after shoe', () => {
    for (let seed = 1; seed <= 25; seed++) {
      const a = new Shoe(mulberry32(seed));
      const b = new Shoe(mulberry32(seed));
      for (let hand = 0; hand < 70; hand++) {
        expect(dealResult(a), `seed ${seed} hand ${hand}`).toBe(dealHand(b).result);
        expect(a.remaining).toBe(b.remaining);
      }
    }
  });
});

describe('seeds and curve positions', () => {
  it('gives every session its own reproducible seed in the range settings allow', () => {
    const seeds = Array.from({ length: 2000 }, (_, i) => trialSeed(42, i));
    expect(new Set(seeds).size).toBe(2000);
    expect(seeds.every((s) => Number.isInteger(s) && s >= 1 && s <= 2 ** 31 - 1)).toBe(true);
    expect(trialSeed(42, 5)).toBe(trialSeed(42, 5));
    expect(trialSeed(42, 5)).not.toBe(trialSeed(43, 5));
  });

  it('samples the curve at evenly spaced shoes, with the same length whatever the session length', () => {
    expect(curveSteps(500, 120)).toEqual({ every: 5, length: 101 });
    expect(curveSteps(50, 120)).toEqual({ every: 1, length: 51 });
    expect(curveSteps(1, 120)).toEqual({ every: 1, length: 2 });
    expect(curveSteps(5000, 120)).toEqual({ every: 42, length: 121 });
  });
});

describe('one simulated session', () => {
  it('is reproducible from its seed and differs between seeds', () => {
    const a = runTrial(spec());
    expect(runTrial(spec())).toEqual(a);
    expect(runTrial(spec({}, { kind: 'sim', count: 10, seed: 8 }))).not.toEqual(a);
  });

  it('starts the curve at zero, ends it on the final result, and plays every shoe it is given', () => {
    const r = runTrial(spec({}, { kind: 'sim', count: 40, seed: 3 }));
    expect(r.curve).toHaveLength(41);
    expect(r.curve[0]).toBe(0);
    expect(r.curve.at(-1)).toBeCloseTo(r.pnlUnits, 9);
    expect(r.stop).toBe('completed');
    expect(r.realBets + r.virtualHands).toBeGreaterThan(40 * 60);
  });

  it('ignores keep-alive, as the Backtest window says', () => {
    const base = runTrial(spec({ pause_consec_loss_count: 2, pause_bet_count: 4 }));
    const withKeepAlive = runTrial(spec({ pause_consec_loss_count: 2, pause_bet_count: 4, keepalive_enabled: true, keepalive_amount: 0.4, keepalive_idle: 2 }));
    expect(withKeepAlive).toEqual(base);
  });

  it('stops at the take profit and fills the rest of the curve with the final result', () => {
    const r = runTrial(spec({ take_profit: 3 }, paste(Array<Result>(50).fill('P'), Array<Result>(50).fill('P'))));
    expect(r.stop).toBe('take_profit');
    expect(r.pnlUnits).toBe(3);
    expect(r.realBets).toBe(3);
    expect(r.curve.at(-1)).toBe(3);
    expect(r.curve.slice(1)).toEqual(Array(r.curve.length - 1).fill(3));
  });

  it('stops at the stop loss', () => {
    const r = runTrial(spec({ stop_loss: 2 }, paste(losingShoe(20))));
    expect(r.stop).toBe('stop_loss');
    expect(r.pnlUnits).toBe(-2);
    expect(r.realBets).toBe(2);
  });

  it('treats the session as the day for the daily limits', () => {
    expect(runTrial(spec({ daily_take_profit: 2 }, paste(Array<Result>(30).fill('P')))).stop).toBe('daily_take_profit');
    expect(runTrial(spec({ daily_stop_loss: 2 }, paste(losingShoe(30)))).stop).toBe('daily_stop_loss');
  });

  it('reports the consecutive-loss stop with its own code', () => {
    expect(runTrial(spec({ stop_consec_loss_count: 2 }, paste(losingShoe(10)))).stop).toBe('consecutive_losses');
  });

  it('declares a session wiped out when it loses the bankroll, and stops there', () => {
    const shoes = paste(losingShoe(40));
    const r = runTrial(spec({}, shoes, 3));
    expect(r.stop).toBe('wiped_out');
    expect(r.pnlUnits).toBe(-3);
    expect(r.realBets).toBe(3);
    expect(r.maxDrawdownUnits).toBe(3);
    // Unlimited bankroll plays on.
    const unlimited = runTrial(spec({}, shoes, 0));
    expect(unlimited.stop).toBe('completed');
    expect(unlimited.pnlUnits).toBe(-40);
  });

  it('counts runs closed in profit and runs given up', () => {
    // P P P: three wins, each closes a run in profit.
    const wins = runTrial(spec({}, paste(['P', 'P', 'P'])));
    expect(wins).toMatchObject({ runsClosed: 3, runsCut: 0, realBets: 3 });
    // A run left open when the shoe changes (policy reset_all) is cut.
    const cut = runTrial(spec({ shoe_change_policy: 'reset_all' }, paste(['B'], ['P'])));
    expect(cut.runsCut).toBe(1);
  });

  it('counts a run-end stake that loses as a run given up, and a stake over the hard cap too', () => {
    // Player loses 1, Banker wins 0.95 (next stake 2 = the run-end stake), and that stake loses: the run is given up.
    const runEnd = runTrial(spec({ max_bet_mode: 'custom', max_bet_units: 2 }, paste(['B', 'B', 'P'])));
    expect(runEnd).toMatchObject({ runsClosed: 0, runsCut: 1 });
    // The same run with a hard cap of 1 unit: the 2-unit recovery stake is never placed, the run is dropped.
    const capped = runTrial(spec({ hard_cap_mode: 'custom', hard_cap_units: 1, max_bet_mode: 'off' }, paste(['B', 'B', 'P'])));
    expect(capped).toMatchObject({ runsClosed: 0, runsCut: 1, realBets: 3 });
    expect(capped.maxStakeUnits).toBe(1);
  });

  it('measures drawdown, largest stake and money staked', () => {
    // Player bet loses 1 (stake 1); Banker bet wins 0.95 (run -0.05, so the next stake is 2); that 2-unit bet loses.
    const r = runTrial(spec({}, paste(['B', 'B', 'P'])));
    expect(r.turnoverUnits).toBe(4);
    expect(r.maxStakeUnits).toBe(2);
    expect(r.pnlUnits).toBeCloseTo(-2.05, 9);
    expect(r.maxDrawdownUnits).toBeCloseTo(2.05, 9);
  });

  it('refuses a strategy the bot cannot run yet', () => {
    expect(() => runTrial(spec({ strategy: 'AME' }))).toThrow(/AME-AI is not available yet/);
  });
});

describe('a backtest session equals a live session', () => {
  // The same hands go through the real Replay feed (the live path) and through the backtest. Every figure the
  // two share must match exactly, for a spread of risk settings, because both use one PlayCore.
  const configs: Array<[string, Record<string, unknown>]> = [
    ['document settings', {}],
    ['pause after losses', { pause_consec_loss_count: 2, pause_bet_count: 3 }],
    ['wait for pattern', { stop_consec_loss_count: 3, stop_consec_loss_action: 'WAIT FOR PATTERN' }],
    ['stop after losses', { stop_consec_loss_count: 4, stop_consec_loss_action: 'STOP BET' }],
    ['cut open run near shoe end', { end_shoe_policy: 'cut_run', end_shoe_hand: 40 }],
    ['no new runs + hard cap + low run-end', { end_shoe_policy: 'no_new_runs', end_shoe_hand: 30, hard_cap_mode: 'custom', hard_cap_units: 20, max_bet_mode: 'custom', max_bet_units: 15 }],
    ['take profit and stop loss', { take_profit: 5, stop_loss: 30 }],
    ['continue across shoes, 3 single wins', { shoe_change_policy: 'continue', single_wins_mode: 'custom', single_wins_limit: 3 }],
    ['daily stop loss', { daily_stop_loss: 25 }],
    ['half-unit rounding', { round_step: 0.5, min_chip: 0 }],
  ];

  const stopStatus: Record<string, RegExp> = {
    completed: /^STOPPED \(replay finished\)$/,
    take_profit: /^STOPPED \(take profit\)$/,
    stop_loss: /^STOPPED \(stop loss\)$/,
    consecutive_losses: /^STOPPED \(\d+ consecutive losses\)$/,
    daily_stop_loss: /^LOCKED \(daily stop loss\)$/,
    daily_take_profit: /^LOCKED \(daily take profit\)$/,
  };

  for (const [name, over] of configs) {
    it(`matches for: ${name}`, async () => {
      for (const seed of [1, 2, 3]) {
        const { text, shoes } = shoeText(seed * 101, 10);
        const trial = runTrial({ settings: settings(over), shoes: { kind: 'paste', shoes }, bankrollUnits: 0, curvePoints: 120 });

        const h = harness();
        const started = (await h.post('/api/start', { settings: { unit_value: 1, min_chip: 0, start_balance: 1000, feed: 'csv', csv_text: text, ...over } })).json;
        expect(started.ok, started.status).toBe(true);
        await vi.waitFor(async () => expect((await h.get('/api/state')).json.running).toBe(false), { timeout: 10_000, interval: 10 });
        const live = (await h.get('/api/state?since=0')).json;
        await h.close();

        const at = `${name}, seed ${seed}`;
        expect(live.hands, `${at}: hands`).toBe(trial.realBets + trial.virtualHands);
        expect(live.pnl, `${at}: profit/loss`).toBeCloseTo(trial.pnlUnits, 2);
        expect(live.virtual_hands, `${at}: virtual hands`).toBe(trial.virtualHands);
        expect(live.max_drawdown, `${at}: drawdown`).toBeCloseTo(trial.maxDrawdownUnits, 2);
        expect(live.max_stake_units, `${at}: largest stake`).toBe(trial.maxStakeUnits);
        expect(live.status, `${at}: how it ended`).toMatch(stopStatus[trial.stop]!);
        const staked = live.rows.reduce((a: number, r: any) => a + (r.mode === 'VIRTUAL' ? 0 : Number(r.stake)), 0);
        expect(staked, `${at}: money staked`).toBeCloseTo(trial.turnoverUnits, 1);
      }
    }, 60_000);
  }

  it('a simulated session equals the same session played from its results', () => {
    // runTrial deals with the fast dealer; replaying the very same shoes as pasted results must agree exactly.
    for (const seed of [5, 6, 7]) {
      const simulated = runTrial(spec({ pause_consec_loss_count: 3, pause_bet_count: 2 }, { kind: 'sim', count: 12, seed }));
      const pasted = runTrial(spec({ pause_consec_loss_count: 3, pause_bet_count: 2 }, { kind: 'paste', shoes: shoeText(seed, 12).shoes }));
      expect(simulated).toEqual(pasted);
    }
  });
});

describe('pasted results', () => {
  it('splits shoes at "|", ignores everything but P, B and T, and skips empty shoes', () => {
    expect(parsePastedShoes('PBT|ppb|| x B-T ,|')).toEqual([['P', 'B', 'T'], ['P', 'P', 'B'], ['B', 'T']]);
    expect(parsePastedShoes('')).toEqual([]);
    expect(parsePastedShoes('|||')).toEqual([]);
  });
});

describe('statistics', () => {
  it('interpolates percentiles', () => {
    expect(percentile([], 0.5)).toBe(0);
    expect(percentile([5], 0.9)).toBe(5);
    expect(percentile([1, 2, 3, 4], 0.5)).toBe(2.5);
    expect(percentile([0, 10], 0.1)).toBe(1);
    expect(percentile([1, 2, 3, 4, 5], 0), ).toBe(1);
    expect(percentile([1, 2, 3, 4, 5], 1)).toBe(5);
  });

  it('draws histogram bins on round edges so every bin is wholly a loss or a profit', () => {
    const values = Array.from({ length: 500 }, (_, i) => -1234 + i * 5.1);
    const bins = histogram(values);
    expect(bins.reduce((a, b) => a + b.n, 0)).toBe(values.length);
    expect(bins.some((b) => b.from === 0 || b.to === 0)).toBe(true);
    for (const b of bins) expect(b.from < 0 && b.to > 0, `bin ${b.from}..${b.to} straddles zero`).toBe(false);
    for (let i = 1; i < bins.length; i++) expect(bins[i]!.from).toBeCloseTo(bins[i - 1]!.to, 6);
  });

  it('copes with identical, single and empty inputs', () => {
    expect(histogram([])).toEqual([]);
    const same = histogram([5, 5, 5]);
    expect(same.reduce((a, b) => a + b.n, 0)).toBe(3);
    expect(same.every((b) => b.to > b.from)).toBe(true);
    expect(histogram([-7]).reduce((a, b) => a + b.n, 0)).toBe(1);
    expect(histogram([0, 0]).reduce((a, b) => a + b.n, 0)).toBe(2);
  });

  const trial = (over: Partial<TrialResult>): TrialResult => ({
    pnlUnits: 0, maxDrawdownUnits: 0, maxStakeUnits: 1, turnoverUnits: 100, realBets: 50, virtualHands: 10,
    runsClosed: 5, runsCut: 1, stop: 'completed', curve: [0, 0, 0], ...over,
  });

  it('aggregates the figures the window shows', () => {
    const trials = [
      trial({ pnlUnits: -100, maxDrawdownUnits: 120, maxStakeUnits: 45, turnoverUnits: 400, stop: 'wiped_out', curve: [0, -50, -100] }),
      trial({ pnlUnits: 10, maxDrawdownUnits: 20, turnoverUnits: 100, curve: [0, 5, 10] }),
      trial({ pnlUnits: 30, maxDrawdownUnits: 10, turnoverUnits: 100, stop: 'take_profit', curve: [0, 20, 30] }),
      trial({ pnlUnits: 0, turnoverUnits: 0, curve: [0, 0, 0] }),
    ];
    const r = aggregate(trials, { unitValue: 0.5, bankrollUnits: 100, seed: 9, shoes: 3 });
    expect(r).toMatchObject({ unit_value: 0.5, trials: 4, seed: 9, shoes: 3, bankroll_units: 100 });
    expect(r.mean).toBe(-15);
    expect(r.median).toBe(5);
    expect(r.best).toBe(30);
    expect(r.worst).toBe(-100);
    expect(r.p10).toBeCloseTo(-70, 9);
    expect(r.p90).toBeCloseTo(24, 9);
    expect(r.profitable_pct).toBe(50);
    expect(r.ruin_pct).toBe(25);
    expect(r.return_on_turnover_pct).toBeCloseTo((-60 / 600) * 100, 9);
    expect(r.mdd_avg).toBe(37.5);
    expect(r.mdd_worst).toBe(120);
    expect(r.max_stake_avg).toBe(12);
    expect(r.max_stake_worst).toBe(45);
    expect(r.turnover_avg).toBe(150);
    expect(r.real_avg).toBe(50);
    expect(r.virtual_avg).toBe(10);
    expect(r.runs_closed_avg).toBe(5);
    expect(r.runs_cut_avg).toBe(1);
    expect(r.stops).toEqual({ 'wiped out': 1, completed: 2, 'take profit': 1 });
    expect(r.curve.p50).toEqual([0, 2.5, 5]);
    expect(r.curve.p10[2]).toBeCloseTo(percentile([-100, 0, 10, 30], 0.1), 9);
    expect(r.hist.reduce((a, b) => a + b.n, 0)).toBe(4);
  });

  it('gives the same result whatever order the sessions arrive in', () => {
    // Worker threads finish in any order, and floating-point sums change with the order of the terms.
    const rng = mulberry32(2024);
    const awkward = Array.from({ length: 300 }, (_, i): TrialResult => ({
      pnlUnits: (rng() - 0.55) * 1000 + 0.1 * i / 7, maxDrawdownUnits: rng() * 300 + 0.05, maxStakeUnits: Math.floor(rng() * 60) + 1 + 0.05,
      turnoverUnits: rng() * 5000 + 0.3, realBets: Math.floor(rng() * 400), virtualHands: Math.floor(rng() * 50),
      runsClosed: Math.floor(rng() * 30), runsCut: Math.floor(rng() * 5), stop: i % 9 === 0 ? 'wiped_out' : 'completed',
      curve: [0, rng() * 100 - 50, rng() * 200 - 100],
    }));
    const info = { unitValue: 0.1, bankrollUnits: 200, seed: 5, shoes: 2 };
    const expected = aggregate(awkward, info);
    for (let round = 0; round < 25; round++) {
      const shuffled = [...awkward];
      for (let i = shuffled.length - 1; i > 0; i--) {
        const j = Math.floor(rng() * (i + 1));
        [shuffled[i], shuffled[j]] = [shuffled[j]!, shuffled[i]!];
      }
      expect(aggregate(shuffled, info), `shuffle ${round}`).toEqual(expected);
    }
  });

  it('does not change the list it is given', () => {
    const list = [trial({ pnlUnits: 5 }), trial({ pnlUnits: -5 }), trial({ pnlUnits: 0 })];
    const copy = list.map((t) => ({ ...t }));
    aggregate(list, { unitValue: 1, bankrollUnits: 0, seed: 1, shoes: 1 });
    expect(list).toEqual(copy);
  });

  it('has no ruin and a zero return when nothing was staked', () => {
    const r = aggregate([trial({ turnoverUnits: 0 })], { unitValue: 1, bankrollUnits: 0, seed: 1, shoes: 1 });
    expect(r).toMatchObject({ ruin_pct: 0, return_on_turnover_pct: 0, profitable_pct: 0 });
    expect(aggregate([], { unitValue: 1, bankrollUnits: 0, seed: 1, shoes: 1 })).toMatchObject({ trials: 0, mean: 0, hist: [] });
  });
});

describe('what the page reads', () => {
  it('every result field the Backtest window uses is present in the result', () => {
    const page = readFileSync(new URL('../ui/index.html', import.meta.url), 'utf8');
    const start = page.indexOf('function btRender(s)');
    const body = page.slice(start, page.indexOf('const num = (v, d)', start));
    const used = [...new Set([...body.matchAll(/\br\.([a-z_0-9]+)/g)].map((m) => m[1]!))].sort();
    expect(used.length).toBeGreaterThan(20);

    const result = aggregate([runTrial(spec())], { unitValue: 1, bankrollUnits: 0, seed: 1, shoes: 10 }) as unknown as Record<string, unknown>;
    for (const field of used) expect(result, `the window reads r.${field}`).toHaveProperty(field);
    expect(Object.keys((result.curve as object))).toEqual(['p10', 'p50', 'p90']);
    expect(Array.isArray(result.hist) && (result.hist as any[])[0]).toHaveProperty('from');
  });
});
