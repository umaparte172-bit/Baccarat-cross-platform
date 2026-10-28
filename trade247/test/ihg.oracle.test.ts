import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { IhgEngine, type IhgConfig } from '../src/engine/ihg.js';
import { toMicro } from '../src/engine/money.js';
import type { Result } from '../src/engine/types.js';

/**
 * The oracle: every worked hand of the client's IHG document (4 examples, 315 hands) replayed through
 * the engine with the document's own settings. Examples 2-4 are excerpts that begin mid-session, so
 * their opening state is seeded from their first row (see SEEDS).
 */
interface Ref {
  example: number;
  shoe: number;
  hand: number;
  outcome: Result;
  pattern: string;
  side: 'P' | 'B';
  result: 'W' | 'L';
  single_wins: number;
  stake: number;
  cash: string;
  run_net: string;
  closed: number;
  net: string;
}

const ALL: Ref[] = JSON.parse(readFileSync(new URL('./fixtures/ihg-reference.json', import.meta.url), 'utf8')).rows;
const rowsOf = (example: number): Ref[] => ALL.filter((r) => r.example === example);
const label = (r: Ref): string => `example ${r.example}, shoe ${r.shoe} hand ${r.hand}`;

const DOCUMENT: IhgConfig = {
  unitMicro: 1_000_000,
  chipMicro: 0,
  stepUnitsMicro: 1_000_000,
  runEndUnits: 45,
  hardCapUnits: null,
  singlesLimit: 7,
  shoePolicy: 'continue',
};
const UNIT = 1_000_000;

const FRESH_RUN = { runMicro: 0, stakeUnits: UNIT, previousWin: false, singles: 0, virtual: false, virtualWins: 0, twoRemaining: 0 };
// Opening states, read off each excerpt's first row (its pattern label, side, stake, counter and run net).
const SEEDS: Record<number, object> = {
  2: { ...FRESH_RUN, history: 'PB', side: 'P' },
  3: { ...FRESH_RUN, history: 'PB', side: 'P' },
  4: { ...FRESH_RUN, history: 'BP', side: 'B', runMicro: toMicro(-16.1), singles: 2 },
};

function engineFor(example: number): IhgEngine {
  const engine = new IhgEngine(DOCUMENT);
  const seed = SEEDS[example];
  if (seed) engine.restore(seed);
  return engine;
}

/** Strict comparison of one reference row against the engine. */
function expectRow(engine: IhgEngine, r: Ref): void {
  const l = label(r);
  const virtualRow = r.stake === 0;
  const plan = engine.plan(false);
  expect(plan.side, `${l}: side`).toBe(r.side);
  expect(plan.mode, `${l}: mode`).toBe(virtualRow ? 'VIRTUAL' : 'REAL');
  if (!virtualRow) expect(plan.units, `${l}: stake`).toBe(r.stake);

  const res = engine.settle(r.outcome, false);
  expect(res.outcome.endsWith(r.result === 'W' ? 'WIN' : 'LOSS'), `${l}: win/loss (${res.outcome})`).toBe(true);
  expect(res.pnlMicro, `${l}: cash`).toBe(toMicro(Number(r.cash)));
  expect(res.pattern, `${l}: pattern`).toBe(r.pattern);
  expect(res.singleWins, `${l}: single wins`).toBe(r.single_wins);
  expect(res.runNetMicro, `${l}: run net`).toBe(toMicro(Number(r.run_net)));
  expect(res.runClosed, `${l}: run closed`).toBe(r.closed === 1);
}

describe('IHG document oracle', () => {
  it('Example 1: all 216 hands across three shoes match exactly', () => {
    const rows = rowsOf(1);
    expect(rows).toHaveLength(216);
    const engine = engineFor(1);
    let net = 0;
    for (const r of rows) {
      expectRow(engine, r);
      net += Number(r.cash);
      expect(toMicro(net), `${label(r)}: cumulative net`).toBe(toMicro(Number(r.net)));
    }
  });

  it('Example 2: a run closed at the 51-unit run-end stake (20 hands) matches exactly', () => {
    const rows = rowsOf(2);
    expect(rows).toHaveLength(20);
    const engine = engineFor(2);
    for (const r of rows) expectRow(engine, r);
    expect(rows.at(-1)).toMatchObject({ stake: 51, result: 'L', closed: 1 });
  });

  it('Example 4: pause at 7 single wins, virtual play, resume with the debt kept (34 hands) matches exactly', () => {
    const rows = rowsOf(4);
    expect(rows).toHaveLength(34);
    const engine = engineFor(4);
    for (const r of rows) expectRow(engine, r);
    expect(rows.at(-1)).toMatchObject({ stake: 42, run_net: '1.9', closed: 1 });
  });

  it('Example 3: matches through hand 146.2, then follows the approved rule instead of the table', () => {
    const rows = rowsOf(3);
    expect(rows).toHaveLength(45);
    const engine = engineFor(3);
    const split = rows.findIndex((r) => r.shoe === 146 && r.hand === 3);
    expect(split).toBeGreaterThan(0);

    for (const r of rows.slice(0, split)) expectRow(engine, r);
    // 146.2 reached the 81-unit run-end stake with 7 single wins at the same moment.
    expect(rows[split - 1]).toMatchObject({ shoe: 146, hand: 2, stake: 81, single_wins: 7, closed: 1 });

    // The table places two more real bets (hands 3 and 4). The document's text and the rule the client
    // approved on 2026-10-18 say virtual play starts at once, so hands 3-21 are virtual.
    for (const r of rows.slice(split)) {
      const plan = engine.plan(false);
      const inPause = r.shoe === 146 && r.hand <= 21;
      expect(plan.mode, `${label(r)}: mode`).toBe(inPause ? 'VIRTUAL' : 'REAL');
      // Selection depends only on the results, so it must still agree with the table everywhere.
      expect(plan.side, `${label(r)}: side`).toBe(r.side);
      const res = engine.settle(r.outcome, false);
      expect(res.pattern, `${label(r)}: pattern`).toBe(r.pattern);
      if (inPause) expect(res.pnlMicro, `${label(r)}: no cash while virtual`).toBe(0);
      if (r.shoe === 146 && r.hand === 21) expect(res.note).toMatch(/real betting resumes at 1 unit/);
    }
  });

  it('Example 3: betting resumes on hand 22 as a fresh run, on Player at one unit', () => {
    const rows = rowsOf(3);
    const engine = engineFor(3);
    for (const r of rows.filter((x) => x.shoe === 145 || (x.shoe === 146 && x.hand <= 21))) engine.settle(r.outcome, false);
    const plan = engine.plan(false);
    expect(plan).toMatchObject({ mode: 'REAL', side: 'P', units: 1 });
    expect(engine.status()).toMatchObject({ run_net_units: 0, single_wins: 0, virtual_mode: false });
  });
});
