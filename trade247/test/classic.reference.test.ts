import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { ClassicEngine, classicConfig } from '../src/engine/classic.js';
import { normalizeSettings, type Settings } from '../src/engine/settings.js';
import type { Result } from '../src/engine/types.js';

/**
 * The seven classic strategies against two independent sources: the client's own written cases (the 19 vectors
 * below, with the picks, stakes and balance he gave), and 35,000 hands played through the C# reference engine
 * (test/fixtures/classic-reference.json; see test/fixtures/README.md for where it came from).
 */

const NAME: Record<string, string> = {
  Martingale: 'MARTINGALE', Grinding: 'GRINDING', Sequence: 'SEQUENCE', 'Odd Even': 'ODD_EVEN',
  'ODD Diff': 'ODD_DIFF', 'ODD Looser': 'ODD_LOOSER', 'ODD is God': 'ODD_IS_GOD',
};

interface Spec {
  mode: string;
  first?: 'P' | 'B';
  unit?: number;
  /** Stakes in money, as the client wrote them; turned into multiples of the first. */
  stakes?: number[];
  sides?: string[];
  multiplier?: number;
}

function settingsFor(spec: Spec): Settings {
  const unit = spec.unit ?? 1;
  const lists = spec.mode === 'Sequence' || spec.mode.startsWith('ODD') || spec.mode === 'Odd Even';
  const stakes = spec.stakes ?? [unit];
  return normalizeSettings({
    strategy: NAME[spec.mode],
    unit_value: lists ? stakes[0]! : unit,
    min_chip: 0,
    progression: lists ? 'list' : 'recovery',
    chain: lists ? stakes.map((x) => x / stakes[0]!) : [],
    first_side: spec.first ?? 'P',
    sequence_sides: (spec.sides ?? ['P']).join(','),
    martingale_multiplier: spec.multiplier ?? 2,
    hard_cap_mode: 'off',
  }).settings;
}

const engineFor = (spec: Spec): ClassicEngine => new ClassicEngine(classicConfig(settingsFor(spec)));
const money = (micro: number): number => micro / 1_000_000;

/** "P1", "B2", or "N" for a hand with no bet - the notation of the client's cases (stakes in money). */
function token(e: ClassicEngine): string {
  const p = e.plan(false);
  if (p.mode === 'NONE') return 'N';
  return `${p.side}${Math.round(money(p.stakeMicro) * 1e6) / 1e6}`;
}

function vector(id: string, spec: Spec, outcomes: string, expected: string, balance: number) {
  it(`${id}: ${spec.mode} ${outcomes}`, () => {
    const e = engineFor(spec);
    const tokens = [token(e)];
    let profit = 0;
    for (const c of outcomes) {
      profit += money(e.settle(c as Result, false).pnlMicro);
      tokens.push(token(e));
    }
    expect(tokens.join(' ')).toBe(expected);
    expect(Math.round((1000 + profit) * 100) / 100).toBe(balance);
  });
}

describe('the client cases (19 of the 21; the other two are PATTERN, covered by the IHG tests)', () => {
  const m = { mode: 'Martingale' as const, first: 'P' as const };
  vector('M1', m, 'BPP', 'P1 B2 P4 P1', 1001);
  vector('M2', m, 'TBTB', 'P1 P1 B2 B2 B1', 1000.9);
  vector('G1', { mode: 'Grinding', first: 'P' }, 'BBB', 'P1 B1 B2 B1', 1001.85);
  vector('G2', { mode: 'Grinding', first: 'P' }, 'BPPP', 'P1 B1 P1 P2 P1', 1001);
  vector('G3', { mode: 'Grinding', first: 'P', unit: 2 }, 'PBB', 'P2 P2 B2 B4', 1001.9);
  const seq = { mode: 'Sequence' as const, sides: ['B', 'P', 'P'], stakes: [1, 2, 3] };
  vector('S1', seq, 'PBBB', 'B1 P2 P3 B1 B1', 994.95);
  vector('S2', seq, 'BT', 'B1 B1 B1', 1000.95);
  vector('E1', { mode: 'Odd Even', first: 'B', stakes: [1, 2, 3] }, 'PBTB', 'B1 P2 B3 B3 P1', 999.85);
  vector('E2', { mode: 'Odd Even', first: 'P', stakes: [1, 2, 3] }, 'PPB', 'P1 B1 P2 B3', 998);
  vector('E3', { mode: 'Odd Even', first: 'B', stakes: [2, 4, 6] }, 'PBPB', 'B2 P4 B6 P2 B4', 986);
  vector('D1', { mode: 'ODD Diff', stakes: [1, 2, 3] }, 'BPPBB', 'N B1 B2 N N B3', 997);
  vector('D2', { mode: 'ODD Diff', stakes: [1, 2, 3] }, 'PBP', 'N P1 P2 P1', 1001);
  vector('D3', { mode: 'ODD Diff', stakes: [1, 2, 3] }, 'BTB', 'N B1 B1 B1', 1000.95);
  vector('L1', { mode: 'ODD Looser', stakes: [1, 2, 3] }, 'BPPBB', 'N P1 P1 N N P1', 1002);
  vector('L2', { mode: 'ODD Looser', stakes: [1, 2, 3] }, 'PBP', 'N B1 B1 B2', 999.95);
  vector('L3', { mode: 'ODD Looser', stakes: [1, 2, 3] }, 'BTB', 'N P1 P1 P2', 999);
  vector('O1', { mode: 'ODD is God', stakes: [1, 2, 3] }, 'BPP', 'N B1 B2 P3', 997);
  vector('O2', { mode: 'ODD is God', stakes: [1, 2, 3] }, 'TPB', 'N N P1 P2', 999);
  vector('O3', { mode: 'ODD is God', stakes: [5, 10] }, 'BPB', 'N B5 B10 B5', 1004.5);
});

describe('against the C# reference engine', () => {
  interface Scenario extends Spec {
    id: string;
    outcomes: string;
    trace: string;
  }
  const scenarios = JSON.parse(readFileSync(new URL('./fixtures/classic-reference.json', import.meta.url), 'utf8')) as Scenario[];

  it('covers every strategy with many hands', () => {
    const hands = scenarios.reduce((n, s) => n + s.trace.split('|').length, 0);
    expect(hands).toBeGreaterThan(30_000);
    for (const mode of Object.keys(NAME)) expect(scenarios.filter((s) => s.mode === mode).length).toBeGreaterThanOrEqual(40);
  });

  for (const mode of Object.keys(NAME)) {
    it(`${mode}: every pick, stake, result, profit change and balance matches`, () => {
      let compared = 0;
      for (const sc of scenarios.filter((s) => s.mode === mode)) {
        const e = engineFor(sc);
        const rows = sc.trace.split('|');
        let total = 0;
        for (let i = 0; i < rows.length; i++) {
          const [pick, stake, bet, wl, change, after] = rows[i]!.split(',');
          const plan = e.plan(false);
          const where = `${sc.id} ${mode} hand ${i + 1} (${sc.outcomes.slice(0, i + 1)})`;
          expect(plan.mode === 'NONE' ? 'N' : plan.side, `${where}: pick`).toBe(pick);
          if (pick !== 'N') expect(money(plan.stakeMicro), `${where}: stake`).toBeCloseTo(Number(stake), 6);
          const res = e.settle(sc.outcomes[i] as Result, false);
          expect(res.plan.mode === 'REAL', `${where}: bet placed`).toBe(bet === 'true');
          expect(res.outcome === 'WIN' ? 'W' : res.outcome === 'LOSS' ? 'L' : '', `${where}: result`).toBe(wl);
          // C# keeps exact decimals; the bot rounds money to a millionth, so 7.2140625 may show as 7.214063.
          expect(money(res.pnlMicro), `${where}: change`).toBeCloseTo(Number(change), 5);
          total += money(res.pnlMicro);
          expect(total, `${where}: balance`).toBeCloseTo(Number(after), 5);
          compared++;
        }
      }
      expect(compared).toBeGreaterThan(4000);
    });
  }
});
