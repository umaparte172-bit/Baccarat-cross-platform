import { BANKER_PAYOUT, roundUpToChip, toMicro } from './money.js';
import { parseSides, type ClassicStrategy, type Settings } from './settings.js';
import type { BetPlan, EngineStatus, HandResult, StrategyEngine } from './strategy.js';
import type { Outcome, Result, Side } from './types.js';
import type { ShoePolicy } from './ihg.js';

export interface ClassicConfig {
  kind: ClassicStrategy;
  unitMicro: number;
  chipMicro: number;
  /** First bet's side; for Odd Even the side bet on odd-numbered results. */
  firstSide: Side;
  /** Martingale: multiplier after a loss. */
  multiplier: number;
  /** Stakes as multiples of the base unit (Sequence and the ODD strategies step through this list). */
  stakes: number[];
  /** Sequence: the sides to bet, in order. */
  sides: Side[];
  /** A stake above this many units drops the progression back to the base stake. null = no cap. */
  hardCapUnits: number | null;
  shoePolicy: ShoePolicy;
}

export function classicConfig(s: Settings): ClassicConfig {
  const kind = s.strategy as ClassicStrategy;
  const listed = s.progression === 'list' && s.chain.length > 0;
  return {
    kind,
    unitMicro: toMicro(s.unit_value),
    chipMicro: toMicro(s.min_chip),
    firstSide: s.first_side,
    multiplier: s.martingale_multiplier,
    stakes: listed ? s.chain : [1],
    sides: parseSides(s.sequence_sides) ?? ['P'],
    hardCapUnits: s.hard_cap_mode === 'off' ? null : s.hard_cap_units,
    shoePolicy: s.shoe_change_policy,
  };
}

const UNIT = 1_000_000;
/** A stake this large is a runaway progression whatever the user configured. */
const RUNAWAY_UNITS = 1_000_000;

interface State {
  /** The pick for the next hand where it is stored (Grinding and the ODD strategies); null = no pick. */
  side: Side | null;
  /** Martingale: the next pick is the last decisive winner (after the first decisive bet). */
  followWinner: boolean;
  lastWinner: Side | null;
  /** Decisive results seen (ties excluded), bet or not. */
  decisions: number;
  /** ODD strategies: Banker-led (+) or Player-led (-) balance over odd-numbered results. */
  oddBalance: number;
  /** Position in the stake list (Sequence and ODD strategies). */
  stakeIdx: number;
  /** Sequence: position in the sides list. */
  seqPos: number;
  /** Martingale and Grinding: the stake, in unit-micros. */
  stakeUnits: number;
  /** Money won or lost since the progression last returned to its base. */
  runMicro: number;
  runOpen: boolean;
}

const opposite = (side: Side): Side => (side === 'P' ? 'B' : 'P');
const unitsText = (units: number): string => String(Math.round(units * 100) / 100);

/**
 * The classic strategies of the Windows application, as the client's 21 cases specify them. They are simple
 * state machines on the decisive results (ties change nothing); stakes follow a list, a multiplier or a grind.
 *
 *  - Martingale: first bet on the first side, then on the last decisive winner; a loss multiplies the stake,
 *    a win returns to the base stake.
 *  - Grinding: follows the last winner; a win adds one unit to the stake, a loss keeps it; once the run is at
 *    least one unit up, the stake returns to one unit.
 *  - Sequence: bets the listed sides in order and steps through the stake list on a loss; a win starts over.
 *  - Odd Even: alternates between the odd side and its opposite on every decisive result.
 *  - ODD Diff / ODD Looser / ODD is God: look only at odd-numbered decisive results (1st, 3rd, ...); Diff bets
 *    the side that has won more of them, Looser the other one, God the last one. No bet until there is a side.
 *
 * Watching only (pause, wait, end of shoe) freezes everything except Martingale's memory of the last winner.
 */
export class ClassicEngine implements StrategyEngine {
  readonly name: string;
  private s: State;
  private hands = 0;
  private runsClosed = 0;
  private runsCut = 0;

  constructor(private cfg: ClassicConfig) {
    this.name = cfg.kind;
    this.s = this.fresh();
  }

  private fresh(): State {
    const c = this.cfg;
    const oddKind = c.kind === 'ODD_DIFF' || c.kind === 'ODD_LOOSER' || c.kind === 'ODD_IS_GOD';
    return {
      side: oddKind ? null : c.firstSide,
      followWinner: false,
      lastWinner: null,
      decisions: 0,
      oddBalance: 0,
      stakeIdx: 0,
      seqPos: 0,
      stakeUnits: UNIT,
      runMicro: 0,
      runOpen: false,
    };
  }

  counters(): { runsClosed: number; runsCut: number } {
    return { runsClosed: this.runsClosed, runsCut: this.runsCut };
  }

  configure(settings: Settings): void {
    this.cfg = classicConfig(settings);
    const s = this.s;
    s.stakeIdx %= this.cfg.stakes.length;
    s.seqPos %= this.cfg.sides.length;
  }

  // ---------------------------------------------------------------- the pick and the stake

  private nextSide(): Side | null {
    const { s, cfg } = this;
    switch (cfg.kind) {
      case 'MARTINGALE':
        return s.followWinner ? s.lastWinner : cfg.firstSide;
      case 'SEQUENCE':
        return cfg.sides[s.seqPos % cfg.sides.length]!;
      default:
        return s.side;
    }
  }

  private stakeUnitsNow(): number {
    const { s, cfg } = this;
    return cfg.kind === 'MARTINGALE' || cfg.kind === 'GRINDING'
      ? s.stakeUnits
      : Math.round(cfg.stakes[s.stakeIdx % cfg.stakes.length]! * UNIT);
  }

  private label(): string {
    const { s, cfg } = this;
    switch (cfg.kind) {
      case 'MARTINGALE':
      case 'GRINDING':
        return s.followWinner || s.decisions > 0 ? 'Follow winner' : 'First bet';
      case 'SEQUENCE':
        return `Step ${(s.seqPos % cfg.sides.length) + 1}/${cfg.sides.length}`;
      case 'ODD_EVEN':
        return s.side === cfg.firstSide ? 'Odd side' : 'Even side';
      default:
        return `Balance ${s.oddBalance > 0 ? '+' : ''}${s.oddBalance}`;
    }
  }

  plan(paused: boolean): BetPlan {
    const side = this.nextSide();
    const units = this.stakeUnitsNow() / UNIT;
    const stakeMicro = roundUpToChip(Math.round(units * this.cfg.unitMicro), this.cfg.chipMicro);
    const pattern = this.label();
    if (side === null) {
      return { mode: 'NONE', side: null, units, stakeMicro, final: false, pattern, reason: 'No bet - observing the next result' };
    }
    return {
      mode: paused ? 'VIRTUAL' : 'REAL',
      side,
      units,
      stakeMicro,
      final: false,
      pattern,
      reason: paused ? 'Paused - watching only' : undefined,
    };
  }

  // ---------------------------------------------------------------- settling a hand

  settle(result: Result, paused: boolean): HandResult {
    const plan = this.plan(paused);
    const s = this.s;
    this.hands++;
    const unchanged = (outcome: Outcome): HandResult => ({
      plan, result, outcome, pnlMicro: 0, pattern: this.label(), note: '', singleWins: 0, runNetMicro: s.runMicro, runClosed: false,
    });
    const winner: Side | null = result === 'T' ? null : result;

    if (paused) {
      if (winner && this.cfg.kind === 'MARTINGALE') s.lastWinner = winner;
      return unchanged(plan.mode === 'NONE' ? 'NO BET' : winner === null ? 'V-TIE' : plan.side === winner ? 'V-WIN' : 'V-LOSS');
    }
    if (winner === null) return unchanged(plan.mode === 'NONE' ? 'NO BET' : 'TIE');

    const isBet = plan.mode === 'REAL';
    const won = plan.side === null ? null : plan.side === winner;
    const pnlMicro = !isBet || won === null ? 0 : won ? Math.round(plan.stakeMicro * (winner === 'B' ? BANKER_PAYOUT : 1)) : -plan.stakeMicro;
    s.runMicro += pnlMicro;
    if (isBet) s.runOpen = true;
    s.lastWinner = winner;
    s.decisions++;
    const runShown = s.runMicro;

    let note = '';
    let runClosed = false;
    switch (this.cfg.kind) {
      case 'MARTINGALE':
        s.followWinner = true;
        if (won) {
          runClosed = true;
          this.resetProgression(true);
        } else {
          s.stakeUnits = Math.round(s.stakeUnits * this.cfg.multiplier);
        }
        break;
      case 'GRINDING':
        s.side = winner;
        if (s.runMicro >= this.cfg.unitMicro) {
          runClosed = true;
          this.resetProgression(true);
        } else if (won) {
          s.stakeUnits += UNIT;
        }
        break;
      case 'SEQUENCE':
        if (won) {
          runClosed = true;
          this.resetProgression(true);
        } else {
          s.seqPos = (s.seqPos + 1) % this.cfg.sides.length;
          s.stakeIdx = (s.stakeIdx + 1) % this.cfg.stakes.length;
        }
        break;
      default:
        runClosed = this.advanceOdd(winner, isBet, won);
    }
    if (runClosed) note = `run closed ${runShown >= 0 ? '+' : ''}${unitsText(runShown / this.cfg.unitMicro)}u`;

    const cap = this.cfg.hardCapUnits;
    const nextUnits = this.stakeUnitsNow() / UNIT;
    if (!runClosed && ((cap !== null && nextUnits > cap) || nextUnits > RUNAWAY_UNITS)) {
      note = 'stake cap exceeded - back to the base stake';
      this.cutRun();
      runClosed = true;
    }

    return {
      plan,
      result,
      outcome: plan.mode === 'NONE' ? 'NO BET' : won ? 'WIN' : 'LOSS',
      pnlMicro,
      pattern: this.label(),
      note,
      singleWins: 0,
      runNetMicro: runShown,
      runClosed,
    };
  }

  /** The ODD strategies and Odd Even. Returns true when a win brought the stake list back to its first stake. */
  private advanceOdd(winner: Side, isBet: boolean, won: boolean | null): boolean {
    const { s, cfg } = this;
    let closed = false;
    if (isBet && won !== null) {
      if (won) {
        closed = s.stakeIdx !== 0;
        if (closed) this.runsClosed++;
        s.stakeIdx = 0;
        s.runMicro = 0;
        s.runOpen = false;
      } else s.stakeIdx = (s.stakeIdx + 1) % cfg.stakes.length;
    }
    if (cfg.kind === 'ODD_EVEN') {
      s.side = s.decisions % 2 === 0 ? cfg.firstSide : opposite(cfg.firstSide);
    } else if (s.decisions % 2 === 1) {
      s.oddBalance += winner === 'B' ? 1 : -1;
      const leader: Side | null = s.oddBalance === 0 ? null : s.oddBalance > 0 ? 'B' : 'P';
      s.side = cfg.kind === 'ODD_IS_GOD' ? winner : cfg.kind === 'ODD_DIFF' ? leader : leader === null ? null : opposite(leader);
    }
    return closed;
  }

  /** The stake goes back to its base. `profit`: the run ended in a win (counted), otherwise it was given up. */
  private resetProgression(profit: boolean): void {
    const s = this.s;
    if (s.runOpen) {
      if (profit) this.runsClosed++;
      else this.runsCut++;
    }
    s.stakeUnits = UNIT;
    s.stakeIdx = 0;
    s.seqPos = 0;
    s.runMicro = 0;
    s.runOpen = false;
  }

  // ---------------------------------------------------------------- shoe, cut, reset

  newShoe(): void {
    if (this.hands === 0 || this.cfg.shoePolicy === 'continue') return;
    const fresh = this.fresh();
    const s = this.s;
    // The pattern memory: what the next pick is built from.
    s.side = fresh.side;
    s.followWinner = false;
    s.lastWinner = null;
    s.decisions = 0;
    s.oddBalance = 0;
    if (this.cfg.shoePolicy === 'reset_all') this.cutRun();
  }

  cutRun(): void {
    this.resetProgression(false);
  }

  reset(): void {
    this.cutRun();
    this.s = this.fresh();
  }

  status(): EngineStatus {
    const s = this.s;
    return {
      run_net_units: Math.round((s.runMicro / this.cfg.unitMicro) * 100) / 100,
      single_wins: 0,
      virtual_mode: false,
      run_open: s.runOpen,
    };
  }

  snapshot(): unknown {
    return { ...this.s, hands: this.hands, runsClosed: this.runsClosed, runsCut: this.runsCut };
  }

  restore(snapshot: unknown): void {
    const { hands, runsClosed, runsCut, ...state } = snapshot as State & { hands?: number; runsClosed?: number; runsCut?: number };
    this.s = { ...this.fresh(), ...state };
    this.hands = hands ?? 0;
    this.runsClosed = runsClosed ?? 0;
    this.runsCut = runsCut ?? 0;
  }
}
