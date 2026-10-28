import type { Settings } from './settings.js';
import type { Outcome, Result, Side } from './types.js';

/** What the strategy intends to do on the next hand. */
export interface BetPlan {
  mode: 'REAL' | 'VIRTUAL';
  side: Side;
  /** Stake in base units (0 is never used; virtual plans still carry the stake they would have had). */
  units: number;
  /** Money actually placed, after rounding up to the minimum chip. Only meaningful for REAL plans. */
  stakeMicro: number;
  /** The stake is at or above the run-end limit: it is placed in full and the run closes either way. */
  final: boolean;
  /** Pattern label shown beside the next pick. */
  pattern: string;
  reason?: string;
}

/** The outcome of one settled hand. */
export interface HandResult {
  plan: BetPlan;
  result: Result;
  outcome: Outcome;
  pnlMicro: number;
  /** Pattern label after this hand (it describes the state the hand left behind). */
  pattern: string;
  note: string;
  singleWins: number;
  /** Net of the run as shown on the row; for a closing row this is the value before the reset. */
  runNetMicro: number;
  runClosed: boolean;
}

export interface EngineStatus {
  run_net_units: number;
  single_wins: number;
  virtual_mode: boolean;
  /** A real bet has been placed in the current run and the run has not closed yet. */
  run_open: boolean;
}

/**
 * A strategy engine is a pure state machine: the same results always produce the same bets.
 * It never touches the clock, the network or the disk.
 */
export interface StrategyEngine {
  readonly name: string;
  /** Apply changed limits to a running session; the run's money and counters are untouched. */
  configure(settings: Settings): void;
  plan(paused: boolean): BetPlan;
  /** Settle the hand that was just played. `paused` means "watch only": no money, no run progress. */
  settle(result: Result, paused: boolean): HandResult;
  /** A new shoe is starting; apply the configured shoe-change policy. */
  newShoe(): void;
  /** Drop the open run (its loss is accepted) and return to the base stake. */
  cutRun(): void;
  /** Start again from scratch: no pattern history, no run, no counters. */
  reset(): void;
  status(): EngineStatus;
  snapshot(): unknown;
  restore(snapshot: unknown): void;
}
