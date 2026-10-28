import { Shoe, dealResult, mulberry32 } from '../engine/dealer.js';
import { toMicro } from '../engine/money.js';
import { PlayCore } from '../engine/play.js';
import type { Settings } from '../engine/settings.js';
import type { Result } from '../engine/types.js';

/** Why a simulated session ended, in the words the Backtest window shows. */
export const STOP_LABELS: Record<string, string> = {
  completed: 'completed',
  take_profit: 'take profit',
  stop_loss: 'stop loss',
  daily_take_profit: 'daily take profit',
  daily_stop_loss: 'daily stop loss',
  consecutive_losses: 'consecutive losses',
  wiped_out: 'wiped out',
};

/** What one simulated session (one "trial") produced. All money is in base units. */
export interface TrialResult {
  pnlUnits: number;
  maxDrawdownUnits: number;
  maxStakeUnits: number;
  turnoverUnits: number;
  realBets: number;
  virtualHands: number;
  runsClosed: number;
  runsCut: number;
  stop: keyof typeof STOP_LABELS;
  /** Profit/loss in units at evenly spaced points over the shoes (same length for every trial). */
  curve: number[];
}

export type Shoes =
  | { kind: 'sim'; count: number; seed: number }
  | { kind: 'paste'; shoes: Result[][] };

export interface TrialSpec {
  settings: Settings;
  shoes: Shoes;
  /** The session counts as wiped out when it loses this many units (0 = unlimited bankroll). */
  bankrollUnits: number;
  /** Number of points on the capital curve (at most). */
  curvePoints: number;
}

/** A distinct, reproducible random seed for session number `index` of a run with seed `base`. */
export function trialSeed(base: number, index: number): number {
  let x = (base + Math.imul(index + 1, 0x9e3779b1)) >>> 0;
  x = Math.imul(x ^ (x >>> 16), 0x85ebca6b);
  x = Math.imul(x ^ (x >>> 13), 0xc2b2ae35);
  x = (x ^ (x >>> 16)) >>> 1;
  return x === 0 ? 1 : x;
}

/** Positions (in shoes) at which the curve is sampled. */
export function curveSteps(totalShoes: number, points: number): { every: number; length: number } {
  const every = Math.max(1, Math.ceil(totalShoes / Math.max(1, points)));
  return { every, length: Math.ceil(totalShoes / every) + 1 };
}

/**
 * Play one session through the same PlayCore a live session uses. Every setting on the page applies
 * (limits, pauses, shoe policy) except keep-alive. The session is treated as one day for the daily limits.
 */
export function runTrial(spec: TrialSpec): TrialResult {
  const made = PlayCore.create(spec.settings, { keepAlive: false });
  if ('error' in made) throw new Error(made.error);
  const core = made.core;
  const unitMicro = toMicro(spec.settings.unit_value);
  const bankrollMicro = spec.bankrollUnits > 0 ? Math.round(spec.bankrollUnits * unitMicro) : 0;

  const total = spec.shoes.kind === 'sim' ? spec.shoes.count : spec.shoes.shoes.length;
  const { every, length } = curveSteps(total, spec.curvePoints);
  const curve = new Array<number>(length).fill(0);
  const rng = spec.shoes.kind === 'sim' ? mulberry32(spec.shoes.seed) : null;
  let filled = 0;
  let stop: keyof typeof STOP_LABELS = 'completed';

  const play = (result: Result, first: boolean): boolean => {
    const played = core.play({ result, newShoe: first }, false, core.pnlMicro);
    if (bankrollMicro > 0 && core.pnlMicro <= -bankrollMicro) {
      stop = 'wiped_out';
      return false;
    }
    const code = played.verdict.stop?.code;
    if (code) {
      stop = code;
      return false;
    }
    return true;
  };

  let shoesDone = 0;
  outer: for (let i = 0; i < total; i++) {
    if (spec.shoes.kind === 'sim') {
      const shoe = new Shoe(rng!);
      let first = true;
      do {
        if (!play(dealResult(shoe), first)) break outer;
        first = false;
      } while (!shoe.cutReached);
    } else {
      const results = spec.shoes.shoes[i]!;
      for (let h = 0; h < results.length; h++) if (!play(results[h]!, h === 0)) break outer;
    }
    shoesDone = i + 1;
    if (shoesDone % every === 0 || shoesDone === total) {
      filled = Math.ceil(shoesDone / every);
      curve[filled] = core.pnlMicro / unitMicro;
    }
  }

  const finalUnits = core.pnlMicro / unitMicro;
  for (let k = filled + 1; k < length; k++) curve[k] = finalUnits;
  const { runsClosed, runsCut } = core.engine.counters();
  return {
    pnlUnits: finalUnits,
    maxDrawdownUnits: core.maxDrawdownMicro / unitMicro,
    maxStakeUnits: core.maxStakeUnits,
    turnoverUnits: core.turnoverMicro / unitMicro,
    realBets: core.realBets,
    virtualHands: core.virtualHands,
    runsClosed,
    runsCut,
    stop,
    curve,
  };
}

/** P / B / T letters, with "|" starting a new shoe. Anything else is ignored. */
export function parsePastedShoes(text: string): Result[][] {
  const shoes: Result[][] = [];
  let current: Result[] = [];
  for (const ch of text.toUpperCase()) {
    if (ch === '|') {
      if (current.length > 0) shoes.push(current);
      current = [];
    } else if (ch === 'P' || ch === 'B' || ch === 'T') current.push(ch);
  }
  if (current.length > 0) shoes.push(current);
  return shoes;
}
