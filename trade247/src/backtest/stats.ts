import { STOP_LABELS, type TrialResult } from './trial.js';

export interface HistogramBin {
  from: number;
  to: number;
  n: number;
}

/** The Backtest window reads exactly these fields. All amounts are in units unless stated. */
export interface BacktestResult {
  /** Dollar value of one unit, so the page can show a dollar figure next to the units. */
  unit_value: number;
  trials: number;
  seed: number;
  shoes: number;
  mean: number;
  median: number;
  p10: number;
  p90: number;
  best: number;
  worst: number;
  profitable_pct: number;
  bankroll_units: number;
  ruin_pct: number;
  return_on_turnover_pct: number;
  mdd_avg: number;
  mdd_worst: number;
  max_stake_avg: number;
  max_stake_worst: number;
  turnover_avg: number;
  real_avg: number;
  virtual_avg: number;
  runs_closed_avg: number;
  runs_cut_avg: number;
  /** How sessions ended, by label ("completed", "take profit", ...). */
  stops: Record<string, number>;
  hist: HistogramBin[];
  curve: { p10: number[]; p50: number[]; p90: number[] };
}

/** Linear-interpolation percentile of an ascending array (p between 0 and 1). */
export function percentile(sorted: readonly number[], p: number): number {
  if (sorted.length === 0) return 0;
  const at = (sorted.length - 1) * p;
  const lo = Math.floor(at);
  const hi = Math.ceil(at);
  const a = sorted[lo]!;
  const b = sorted[hi]!;
  return a + (b - a) * (at - lo);
}

const mean = (values: readonly number[]): number => (values.length === 0 ? 0 : values.reduce((a, b) => a + b, 0) / values.length);

/** 1, 2, 2.5, 5 times a power of ten. */
function niceStep(rough: number): number {
  if (!(rough > 0)) return 1;
  const power = 10 ** Math.floor(Math.log10(rough));
  const f = rough / power;
  return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 2.5 ? 2.5 : f <= 5 ? 5 : 10) * power;
}

/**
 * Bins aligned to multiples of a round width, so that 0 is always a bin edge: a bin is then wholly a loss,
 * wholly a profit, or (only when every result is the same) a single bin.
 */
export function histogram(values: readonly number[], targetBins = 24): HistogramBin[] {
  if (values.length === 0) return [];
  const min = Math.min(...values);
  const max = Math.max(...values);
  const width = niceStep((max - min) / targetBins) || 1;
  const lo = Math.floor(min / width) * width;
  let hi = Math.ceil(max / width) * width;
  if (hi <= lo) hi = lo + width;
  const count = Math.max(1, Math.round((hi - lo) / width));
  const bins: HistogramBin[] = Array.from({ length: count }, (_, i) => ({ from: round(lo + i * width), to: round(lo + (i + 1) * width), n: 0 }));
  for (const v of values) {
    const i = Math.min(count - 1, Math.max(0, Math.floor((v - lo) / width + 1e-9)));
    bins[i]!.n++;
  }
  return bins;
}

const round = (n: number): number => Math.round(n * 1e6) / 1e6;

export interface AggregateInfo {
  unitValue: number;
  bankrollUnits: number;
  seed: number;
  shoes: number;
}

/** A total order on sessions, so that the same sessions always add up the same way. */
function byValue(a: TrialResult, b: TrialResult): number {
  return (
    a.pnlUnits - b.pnlUnits ||
    a.maxDrawdownUnits - b.maxDrawdownUnits ||
    a.maxStakeUnits - b.maxStakeUnits ||
    a.turnoverUnits - b.turnoverUnits ||
    a.realBets - b.realBets ||
    a.virtualHands - b.virtualHands ||
    a.runsClosed - b.runsClosed ||
    a.runsCut - b.runsCut ||
    (a.stop < b.stop ? -1 : a.stop > b.stop ? 1 : 0)
  );
}

/**
 * The result does not depend on the order the sessions arrive in (worker threads finish in any order, and
 * floating-point sums change with the order of the terms), so it is the same whatever the number of threads.
 */
export function aggregate(input: readonly TrialResult[], info: AggregateInfo): BacktestResult {
  const trials = [...input].sort(byValue);
  const n = trials.length;
  const finals = trials.map((t) => t.pnlUnits).sort((a, b) => a - b);
  const turnover = trials.reduce((a, t) => a + t.turnoverUnits, 0);
  const total = finals.reduce((a, b) => a + b, 0);

  const stops: Record<string, number> = {};
  for (const t of trials) {
    const label = STOP_LABELS[t.stop] ?? String(t.stop);
    stops[label] = (stops[label] ?? 0) + 1;
  }

  const points = trials[0]?.curve.length ?? 0;
  const curve = { p10: new Array<number>(points), p50: new Array<number>(points), p90: new Array<number>(points) };
  for (let k = 0; k < points; k++) {
    const column = trials.map((t) => t.curve[k]!).sort((a, b) => a - b);
    curve.p10[k] = percentile(column, 0.1);
    curve.p50[k] = percentile(column, 0.5);
    curve.p90[k] = percentile(column, 0.9);
  }

  const ruined = trials.filter((t) => t.stop === 'wiped_out').length;
  return {
    unit_value: info.unitValue,
    trials: n,
    seed: info.seed,
    shoes: info.shoes,
    mean: mean(finals),
    median: percentile(finals, 0.5),
    p10: percentile(finals, 0.1),
    p90: percentile(finals, 0.9),
    best: finals.at(-1) ?? 0,
    worst: finals[0] ?? 0,
    profitable_pct: n === 0 ? 0 : (trials.filter((t) => t.pnlUnits > 0).length / n) * 100,
    bankroll_units: info.bankrollUnits,
    ruin_pct: n === 0 ? 0 : (ruined / n) * 100,
    return_on_turnover_pct: turnover > 0 ? (total / turnover) * 100 : 0,
    mdd_avg: mean(trials.map((t) => t.maxDrawdownUnits)),
    mdd_worst: Math.max(0, ...trials.map((t) => t.maxDrawdownUnits)),
    max_stake_avg: mean(trials.map((t) => t.maxStakeUnits)),
    max_stake_worst: Math.max(0, ...trials.map((t) => t.maxStakeUnits)),
    turnover_avg: n === 0 ? 0 : turnover / n,
    real_avg: mean(trials.map((t) => t.realBets)),
    virtual_avg: mean(trials.map((t) => t.virtualHands)),
    runs_closed_avg: mean(trials.map((t) => t.runsClosed)),
    runs_cut_avg: mean(trials.map((t) => t.runsCut)),
    stops,
    hist: histogram(finals),
    curve,
  };
}
