import { describe, expect, it } from 'vitest';
import { mulberry32 } from '../src/engine/dealer.js';
import { IhgEngine, type IhgConfig } from '../src/engine/ihg.js';
import { toMicro } from '../src/engine/money.js';
import { DEFAULT_SETTINGS } from '../src/engine/settings.js';
import type { HandResult } from '../src/engine/strategy.js';
import type { Result } from '../src/engine/types.js';

const U = 1_000_000;
const CONFIG: IhgConfig = {
  unitMicro: U, chipMicro: 0, stepUnitsMicro: U,
  runEndUnits: 45, hardCapUnits: null, singlesLimit: 7, shoePolicy: 'continue',
};
const FRESH = { history: '', twoRemaining: 0, pattern: 'First bet', side: 'P', runMicro: 0, stakeUnits: U, previousWin: false, singles: 0, virtual: false, virtualWins: 0 };

const make = (over: Partial<IhgConfig> = {}, state: object = {}): IhgEngine => {
  const e = new IhgEngine({ ...CONFIG, ...over });
  e.restore({ ...FRESH, ...state });
  return e;
};
const play = (e: IhgEngine, results: string, paused = false): HandResult[] =>
  [...results].map((r) => e.settle(r as Result, paused));
const one = (e: IhgEngine, result: string, paused = false): HandResult => e.settle(result as Result, paused);
/** Settle a hand in which the engine's own prediction wins. */
const winNow = (e: IhgEngine): HandResult => e.settle(e.plan(false).side, false);
/** Settle a hand in which the engine's own prediction loses. */
const loseNow = (e: IhgEngine): HandResult => e.settle(e.plan(false).side === 'P' ? 'B' : 'P', false);

describe('selection', () => {
  it('bets Player first, then follows the first result', () => {
    const e = new IhgEngine(CONFIG);
    expect(e.plan(false)).toMatchObject({ mode: 'REAL', side: 'P', units: 1, pattern: 'First bet' });
    play(e, 'B');
    expect(e.plan(false)).toMatchObject({ side: 'B', pattern: 'Follow' });
  });

  it('follows a streak and opposes a chop on the last two results', () => {
    const e = new IhgEngine(CONFIG);
    play(e, 'BB');
    expect(e.plan(false)).toMatchObject({ side: 'B', pattern: 'Streak' });
    play(e, 'P');
    expect(e.plan(false)).toMatchObject({ side: 'B', pattern: 'Chop' });
    play(e, 'B');
    expect(e.plan(false)).toMatchObject({ side: 'P', pattern: 'Chop' });
  });

  it('ignores ties completely', () => {
    const e = new IhgEngine(CONFIG);
    play(e, 'PB');
    const before = JSON.stringify(e.snapshot());
    const tie = one(e, 'T');
    expect(tie).toMatchObject({ outcome: 'TIE', pnlMicro: 0 });
    expect(JSON.stringify(e.snapshot())).toBe(before.replace(/"hands":\d+/, `"hands":3`));
    expect(e.plan(false).side).toBe('P');
  });

  it('follows a 2-2 pattern until a prediction loses, then returns to the two-result rule', () => {
    const e = new IhgEngine(CONFIG);
    play(e, 'PPBB');
    expect(e.plan(false)).toMatchObject({ side: 'P', pattern: '2-2' });
    const sides: string[] = [];
    for (const r of 'PPBBPP') {
      sides.push(e.plan(false).side);
      e.settle(r as Result, false);
    }
    expect(sides).toEqual(['P', 'P', 'B', 'B', 'P', 'P']);
    expect(e.plan(false).pattern).toBe('2-2');

    // The pattern breaks: expected B, got P.
    play(e, 'P');
    expect(e.plan(false)).toMatchObject({ pattern: 'Streak', side: 'P' });
  });
});

describe('money', () => {
  it('pays Player 1:1 and Banker 0.95 without drift', () => {
    const p = make({}, { side: 'P' });
    expect(p.settle('P', false).pnlMicro).toBe(U);
    const b = make({ unitMicro: toMicro(0.15) }, { side: 'B' });
    expect(b.settle('B', false).pnlMicro).toBe(142_500);
    const loss = make({}, { side: 'B' });
    expect(loss.settle('P', false).pnlMicro).toBe(-U);
  });

  it('restarts at one unit after a loss and recovers as ceil(deficit) + 1 after a win', () => {
    const e = new IhgEngine(CONFIG);
    play(e, 'B'); // Player bet loses: run -1
    expect(e.plan(false).units).toBe(1);
    play(e, 'B'); // Banker bet wins 0.95: run -0.05
    expect(e.status().run_net_units).toBe(-0.05);
    expect(e.plan(false).units).toBe(2);
  });

  it('rounds a 2.05 deficit up before adding the unit, as the document does', () => {
    const e = make({}, { runMicro: toMicro(-2.05), previousWin: false, side: 'P' });
    e.settle('P', false); // win +1: run -1.05
    expect(e.plan(false).units).toBe(3);
  });

  it('honours the rounding step', () => {
    const recover = (step: number): number => {
      const e = make({ stepUnitsMicro: toMicro(step) }, { runMicro: toMicro(-1.3), side: 'P' });
      e.settle('P', false); // win +1: run -0.3
      return e.plan(false).units;
    };
    expect(recover(1)).toBe(2);
    expect(recover(0.5)).toBe(1.5);
    expect(recover(0.1)).toBe(1.3);
    expect(recover(0)).toBe(1.3);
  });

  it('keeps a run open at exactly zero and closes it only above zero', () => {
    const e = make({}, { runMicro: -U, side: 'P' });
    const atZero = one(e, 'P'); // run -1 + 1 = 0
    expect(atZero.runClosed).toBe(false);
    expect(e.plan(false).units).toBe(1);
    const above = one(e, 'P'); // run +1
    expect(above).toMatchObject({ runClosed: true, runNetMicro: U });
    expect(e.status().run_net_units).toBe(0);
  });

  it('rounds stakes up to the minimum chip and accounts in real money', () => {
    const e = new IhgEngine({ ...CONFIG, unitMicro: toMicro(0.05), chipMicro: toMicro(0.1) });
    expect(e.plan(false)).toMatchObject({ units: 1, stakeMicro: toMicro(0.1) });
    const loss = one(e, 'B');
    expect(loss.pnlMicro).toBe(toMicro(-0.1));
    expect(e.status().run_net_units).toBe(-2);
  });
});

describe('run limits', () => {
  it('places the run-end stake in full and closes the run even when it loses', () => {
    const e = make({ runEndUnits: 3 }, { runMicro: toMicro(-2), stakeUnits: 3 * U, previousWin: true, side: 'P' });
    expect(e.plan(false)).toMatchObject({ units: 3, final: true });
    const lost = one(e, 'B');
    expect(lost).toMatchObject({ outcome: 'LOSS', pnlMicro: -3 * U, runClosed: true, runNetMicro: toMicro(-5) });
    expect(lost.note).toMatch(/run-end stake 3u - run closed/);
    expect(e.status().run_net_units).toBe(0);
    expect(e.plan(false)).toMatchObject({ units: 1, final: false });
  });

  it('never places a stake above the hard cap: the run is dropped and the stake returns to one unit', () => {
    const e = make({ hardCapUnits: 5, runEndUnits: null }, { runMicro: toMicro(-9), previousWin: false, side: 'P' });
    const res = one(e, 'P'); // win +1: run -8, recovery would be 9 units
    expect(res.note).toMatch(/hard cap exceeded - run dropped/);
    expect(e.plan(false).units).toBe(1);
    expect(e.status().run_net_units).toBe(0);
  });

  it('allows a stake exactly at the hard cap', () => {
    const e = make({ hardCapUnits: 9, runEndUnits: null }, { runMicro: toMicro(-9), side: 'P' });
    play(e, 'P');
    expect(e.plan(false).units).toBe(9);
  });

  it('applies changed limits to a running session without touching its money', () => {
    const e = make({}, { runMicro: toMicro(-3), stakeUnits: 4 * U, side: 'P' });
    e.configure({ ...DEFAULT_SETTINGS, unit_value: 1, min_chip: 0, max_bet_mode: 'custom', max_bet_units: 4, single_wins_mode: 'off' });
    expect(e.plan(false)).toMatchObject({ units: 4, final: true });
    expect(e.status().run_net_units).toBe(-3);
  });
});

describe('single wins and virtual mode', () => {
  const limit2 = { singlesLimit: 2, runEndUnits: null };

  it('counts a loss that follows a win and pauses at the limit', () => {
    const e = make(limit2, { runMicro: toMicro(-1), previousWin: true, singles: 1, side: 'P' });
    const res = one(e, 'B'); // loss after a win: second single win
    expect(res.singleWins).toBe(2);
    expect(res.note).toMatch(/single-win limit reached/);
    expect(e.plan(false)).toMatchObject({ mode: 'VIRTUAL' });
    expect(e.status().virtual_mode).toBe(true);
  });

  it('moves no money while virtual and shows virtual outcomes', () => {
    const e = make(limit2, { virtual: true, runMicro: toMicro(-7), side: 'P' });
    const win = one(e, 'P');
    expect(win).toMatchObject({ outcome: 'V-WIN', pnlMicro: 0 });
    const lose = loseNow(e);
    expect(lose).toMatchObject({ outcome: 'V-LOSS', pnlMicro: 0 });
    expect(e.status().run_net_units).toBe(-7);
  });

  it('resumes after two consecutive virtual wins, at one unit, keeping the run debt', () => {
    const e = make(limit2, { virtual: true, runMicro: toMicro(-40.1), stakeUnits: 21 * U, side: 'P' });
    winNow(e);
    expect(e.plan(false).mode).toBe('VIRTUAL');
    const second = winNow(e);
    expect(second.note).toMatch(/real betting resumes at 1 unit/);
    expect(second.singleWins).toBe(0);
    expect(e.plan(false)).toMatchObject({ mode: 'REAL', units: 1 });
    expect(e.status()).toMatchObject({ run_net_units: -40.1, single_wins: 0, virtual_mode: false });
  });

  it('a virtual loss restarts the count of consecutive wins', () => {
    const e = make(limit2, { virtual: true, side: 'P' });
    winNow(e);
    loseNow(e);
    winNow(e);
    expect(e.plan(false).mode).toBe('VIRTUAL');
    winNow(e);
    expect(e.plan(false).mode).toBe('REAL');
  });

  it('ties neither advance nor break the virtual win count', () => {
    const e = make(limit2, { virtual: true, side: 'P' });
    winNow(e);
    expect(one(e, 'T')).toMatchObject({ outcome: 'V-TIE', pnlMicro: 0 });
    winNow(e);
    expect(e.plan(false).mode).toBe('REAL');
  });

  it('enters virtual play immediately when the run-end stake and the single-win limit coincide', () => {
    const e = make({ runEndUnits: 45, singlesLimit: 7 }, { runMicro: toMicro(-44), stakeUnits: 45 * U, previousWin: true, singles: 6, side: 'P' });
    const res = one(e, 'B');
    expect(res).toMatchObject({ runClosed: true, singleWins: 7, pnlMicro: -45 * U });
    expect(res.note).toMatch(/virtual bets until 2 wins in a row/);
    expect(e.plan(false)).toMatchObject({ mode: 'VIRTUAL' });
    expect(e.status()).toMatchObject({ run_net_units: 0, virtual_mode: true });
  });

  it('does not pause at all when the single-win limit is off', () => {
    const e = make({ singlesLimit: null }, { runMicro: toMicro(-1), previousWin: true, singles: 50, side: 'P' });
    play(e, 'B');
    expect(e.plan(false).mode).toBe('REAL');
  });
});

describe('watch-only (paused) hands', () => {
  it('moves no money and no run state, but keeps following the table', () => {
    const e = make({}, { runMicro: toMicro(-3), stakeUnits: 4 * U, previousWin: true, singles: 2, side: 'P' });
    const res = one(e, 'B', true);
    expect(res).toMatchObject({ outcome: 'V-LOSS', pnlMicro: 0 });
    expect(e.status()).toMatchObject({ run_net_units: -3, single_wins: 2, virtual_mode: false });
    expect(e.plan(true)).toMatchObject({ mode: 'VIRTUAL', side: 'B', reason: 'Paused - watching only' });
    expect(e.plan(false)).toMatchObject({ mode: 'REAL', units: 4, side: 'B' });
  });

  it('does not advance the virtual-win count while paused', () => {
    const e = make({}, { virtual: true, side: 'P' });
    play(e, 'P', true);
    play(e, 'P', true);
    expect(e.plan(false).mode).toBe('VIRTUAL');
  });
});

describe('shoe change policy', () => {
  const dirty = { history: 'PPBB', pattern: '2-2', twoRemaining: 2, side: 'B', runMicro: toMicro(-6), stakeUnits: 7 * U, previousWin: true, singles: 3 };
  const played = (policy: IhgConfig['shoePolicy']): IhgEngine => {
    const e = make({ shoePolicy: policy }, dirty);
    e.settle('B', true); // counts as a hand so the policy applies
    return e;
  };

  it('continue carries everything across', () => {
    const e = played('continue');
    const before = JSON.stringify(e.snapshot());
    e.newShoe();
    expect(JSON.stringify(e.snapshot())).toBe(before);
  });

  it('reset_pattern forgets the pattern but keeps the open run', () => {
    const e = played('reset_pattern');
    e.newShoe();
    expect(e.plan(false)).toMatchObject({ side: 'P', pattern: 'First bet', units: 7 });
    expect(e.status().run_net_units).toBe(-6);
  });

  it('reset_all also cuts the open run and returns to the base unit', () => {
    const e = played('reset_all');
    e.newShoe();
    expect(e.plan(false)).toMatchObject({ side: 'P', pattern: 'First bet', units: 1 });
    expect(e.status()).toMatchObject({ run_net_units: 0, single_wins: 0 });
  });

  it('reset_all keeps virtual mode: it is a table valve, not a deficit', () => {
    const e = make({ shoePolicy: 'reset_all' }, { ...dirty, virtual: true });
    e.settle('B', true);
    e.newShoe();
    expect(e.status().virtual_mode).toBe(true);
  });

  it('does nothing before the first hand of a session', () => {
    const e = make({ shoePolicy: 'reset_all' }, dirty);
    e.newShoe();
    expect(e.plan(false)).toMatchObject({ side: 'B', units: 7 });
  });
});

describe('snapshots', () => {
  it('restoring a snapshot reproduces every later hand exactly', () => {
    const rng = mulberry32(99);
    const results = Array.from({ length: 1500 }, () => (['P', 'B', 'P', 'B', 'T'] as const)[Math.floor(rng() * 5)]!);
    for (const cut of [1, 37, 500, 1200]) {
      const a = new IhgEngine({ ...CONFIG, singlesLimit: 3, runEndUnits: 12 });
      for (const r of results.slice(0, cut)) a.settle(r, false);
      const snap = JSON.parse(JSON.stringify(a.snapshot()));
      const b = new IhgEngine({ ...CONFIG, singlesLimit: 3, runEndUnits: 12 });
      b.restore(snap);
      for (const r of results.slice(cut)) expect(b.settle(r, false)).toEqual(a.settle(r, false));
    }
  });
});

describe('invariants over random play', () => {
  const configs: Array<[string, Partial<IhgConfig>]> = [
    ['document settings', {}],
    ['tight limits', { runEndUnits: 10, singlesLimit: 3, hardCapUnits: 8 }],
    ['half-unit rounding', { stepUnitsMicro: toMicro(0.5), runEndUnits: 30 }],
    ['no limits', { runEndUnits: null, singlesLimit: null }],
  ];

  for (const [name, over] of configs) {
    it(`holds for 12 seeds x 2500 hands: ${name}`, () => {
      for (let seed = 1; seed <= 12; seed++) {
        const rng = mulberry32(seed);
        const cfg = { ...CONFIG, ...over };
        const e = new IhgEngine(cfg);
        let pnl = 0;
        for (let i = 0; i < 2500; i++) {
          const roll = rng();
          const result: Result = roll < 0.4462 ? 'P' : roll < 0.9048 ? 'B' : 'T';
          const planned = e.plan(false);
          const at = `seed ${seed} hand ${i + 1}`;
          const res = e.settle(result, false);

          // Money moves only on real, decided hands, with the right sign and the exact payout.
          if (res.outcome === 'WIN') {
            const expected = Math.round(planned.stakeMicro * (planned.side === 'B' ? 0.95 : 1));
            expect(res.pnlMicro, at).toBe(expected);
          } else if (res.outcome === 'LOSS') expect(res.pnlMicro, at).toBe(-planned.stakeMicro);
          else expect(res.pnlMicro, `${at} ${res.outcome}`).toBe(0);
          pnl += res.pnlMicro;

          const status = e.status();
          const next = e.plan(false);
          // Real bets never happen while virtual, and a stake is never below one unit.
          expect(next.mode === 'VIRTUAL', at).toBe(status.virtual_mode);
          expect(next.units, at).toBeGreaterThanOrEqual(1);
          if (cfg.hardCapUnits !== null) expect(next.units, `${at} hard cap`).toBeLessThanOrEqual(cfg.hardCapUnits);
          // A closed run leaves no debt and restarts at one unit.
          if (res.runClosed && cfg.hardCapUnits === null) {
            expect(status.run_net_units, at).toBe(0);
            expect(next.units, at).toBe(1);
          }
          // After a real loss the next stake is one unit; after a win in an open run it is the recovery stake.
          if (!status.virtual_mode && !res.runClosed && res.plan.mode === 'REAL') {
            if (res.outcome === 'LOSS') expect(next.units, at).toBe(1);
            if (res.outcome === 'WIN' && cfg.hardCapUnits === null && cfg.stepUnitsMicro === U) {
              const deficit = Math.max(0, -status.run_net_units);
              expect(next.units, at).toBe(Math.ceil(deficit - 1e-9) + 1);
            }
          }
          // Wins never exceed what the stake could pay.
          expect(Math.abs(res.pnlMicro), at).toBeLessThanOrEqual(planned.stakeMicro);
        }
        expect(Number.isSafeInteger(pnl)).toBe(true);
      }
    });
  }
});
