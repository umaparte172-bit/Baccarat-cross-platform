export type Result = 'P' | 'B' | 'T';
export type Side = 'P' | 'B';
/** NONE: the strategy has no pick for this hand (it is only observing). */
export type Mode = 'REAL' | 'VIRTUAL' | 'MIN' | 'NONE';
export type Outcome = 'WIN' | 'LOSS' | 'TIE' | 'V-WIN' | 'V-LOSS' | 'V-TIE' | 'NO BET';

/** Card point values 0-9 for [player, banker]; 0 stands for 10/J/Q/K. */
export type Cards = [number[], number[]];

/** One history row, exactly as index.html reads it. */
export interface Row {
  n: number;
  time: string;
  /** '' when the strategy had no pick (a hand it only observed). */
  pick: Side | '';
  mode: Mode;
  stake: string;
  result: Result;
  outcome: Outcome;
  pattern: string;
  note: string;
  single_wins: number | '';
  run_net_units: number | '';
  session_pnl: string;
  cards: Cards | null;
  session: number;
}

export interface NextBet {
  mode: 'REAL' | 'VIRTUAL' | 'NONE';
  pick: 'PLAYER' | 'BANKER' | '';
  units: number;
  stake: number;
  final: boolean;
  pattern: string;
  reason?: string;
}

export const isResult = (v: unknown): v is Result => v === 'P' || v === 'B' || v === 'T';
