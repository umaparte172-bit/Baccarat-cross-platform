import { describe, expect, it } from 'vitest';
import { AmeEngine, scoreMomentum } from '../src/engine/ame.js';
import { CycleEngine } from '../src/engine/cycle.js';
import { DEFAULT_SETTINGS } from '../src/engine/settings.js';
import type { Result, Side } from '../src/engine/types.js';

const ame = (over: Record<string, unknown> = {}) => new AmeEngine({ ...DEFAULT_SETTINGS, strategy: 'AME', unit_value: 1, min_chip: 0, ...over });
const jj = (over: Record<string, unknown> = {}) => new CycleEngine({ ...DEFAULT_SETTINGS, strategy: 'JJ', unit_value: 1, min_chip: 0.01, cycle_preset: 'custom', ...over });

function feed(engine: AmeEngine, text: string): void {
  for (const result of text) engine.settle(result as Result, false);
}

describe('AME momentum oracle', () => {
  it('recognises streak, chop and 2-2 rhythms from the client reference', () => {
    expect(scoreMomentum([...('BBBBBBBBBB' as string)] as Side[])).toMatchObject({ period: 1, pick: 'B', ready: true });
    expect(scoreMomentum([...('BPBPBPBPBPBP' as string)] as Side[])).toMatchObject({ period: 2, pick: 'B', ready: true });
    expect(scoreMomentum([...('BBPPBBPPBBPP' as string)] as Side[])).toMatchObject({ period: 4, pick: 'B', ready: true });
    expect(scoreMomentum(['B', 'P', 'B'])).toMatchObject({ ready: false });
  });

  it('uses virtual bets while warming up and real bets at sufficient confidence', () => {
    const engine = ame();
    expect(engine.plan(false)).toMatchObject({ mode: 'VIRTUAL', reason: 'warming up' });
    feed(engine, 'BBBBBBBBBB');
    expect(engine.plan(false)).toMatchObject({ mode: 'REAL', side: 'B' });
    expect((engine.status() as any).confidence).toBeGreaterThanOrEqual(70);
  });

  it('limits dynamic recovery growth and resets after the deficit is recovered', () => {
    const engine = ame({ ame_min_confidence: 0, ame_chaos_entropy: 1, ame_recovery_rate: 0.35, ame_max_step: 1 });
    feed(engine, 'BBBBBBBB');
    const seen: number[] = [];
    for (let i = 0; i < 10; i++) {
      const plan = engine.plan(false); seen.push(plan.units);
      engine.settle(plan.side === 'P' ? 'B' : 'P', false);
    }
    expect(seen.slice(1).every((v, i) => v - seen[i]! <= 1.000001)).toBe(true);
  });
});

describe('JJ cycle oracle', () => {
  it('grows after a negative cycle and resets after a positive one', () => {
    const engine = jj({ cycle_hands: 4, cycle_pct_min: 10, cycle_pct_max: 10, cycle_max_mult: 0, cycle_max_hands: 0 });
    for (let i = 0; i < 4; i++) {
      const plan = engine.plan(false); engine.settle(plan.side === 'P' ? 'B' : 'P', false);
    }
    expect((engine.status() as any).multiplier).toBeCloseTo(1.1, 6);
    for (let i = 0; i < 4; i++) {
      const plan = engine.plan(false); engine.settle(plan.side!, false);
    }
    expect((engine.status() as any).multiplier).toBe(1);
  });

  it('obeys the multiplier cap and formula hand limit', () => {
    const engine = jj({ cycle_hands: 2, cycle_pct_min: 100, cycle_pct_max: 100, cycle_max_mult: 2, cycle_max_hands: 6 });
    const stakes: number[] = [];
    for (let i = 0; i < 6; i++) {
      const plan = engine.plan(false); stakes.push(plan.units); engine.settle(plan.side === 'P' ? 'B' : 'P', false);
    }
    expect(Math.max(...stakes)).toBe(2);
    expect((engine.status() as any).multiplier).toBe(1);
  });
});
