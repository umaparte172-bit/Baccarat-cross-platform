import { BANKER_PAYOUT, roundUpToChip, toMicro } from './money.js';
import type { Settings } from './settings.js';
import type { BetPlan, EngineStatus, HandResult, StrategyEngine } from './strategy.js';
import type { Result, Side } from './types.js';

export type ShoePolicy = 'reset_pattern' | 'reset_all' | 'continue';

export interface IhgConfig {
  unitMicro: number;
  chipMicro: number;
  /** Recovery stakes round up to a multiple of this many units (1 = the IHG document, 0 = exact). */
  stepUnitsMicro: number;
  /** A stake at or above this is placed in full and closes its run win or lose. null = no limit. */
  runEndUnits: number | null;
  /** A recovery stake above this is never placed: the run is dropped. null = no limit. */
  hardCapUnits: number | null;
  /** Isolated wins that switch to virtual bets. null = never. */
  singlesLimit: number | null;
  shoePolicy: ShoePolicy;
}

export const IHG_DOCUMENT = { runEndUnits: 45, singlesLimit: 7 } as const;

export function ihgConfig(s: Settings): IhgConfig {
  return {
    unitMicro: toMicro(s.unit_value),
    chipMicro: toMicro(s.min_chip),
    stepUnitsMicro: toMicro(s.round_step),
    runEndUnits: s.max_bet_mode === 'off' ? null : s.max_bet_mode === 'doc' ? IHG_DOCUMENT.runEndUnits : s.max_bet_units,
    hardCapUnits: s.hard_cap_mode === 'off' ? null : s.hard_cap_units,
    singlesLimit: s.single_wins_mode === 'off' ? null : s.single_wins_mode === 'doc' ? IHG_DOCUMENT.singlesLimit : s.single_wins_limit,
    shoePolicy: s.shoe_change_policy,
  };
}

/** One unit expressed in "unit-micros": stakes in units are held as integers of 1e-6 unit. */
const UNIT = 1_000_000;
const VIRTUAL_WINS_TO_RESUME = 2;

interface State {
  /** Last four non-tie winners, e.g. "PPBB". */
  history: string;
  /** Hands left in an active 2-2 pattern (0 = not in 2-2). */
  twoRemaining: number;
  /** Label describing the state after the latest hand. */
  pattern: string;
  /** The side the strategy will pick next. */
  side: Side;
  /** Net money of the open run (negative = deficit). */
  runMicro: number;
  /** Next stake in unit-micros. */
  stakeUnits: number;
  /** The previous settled hand of this run was a win. */
  previousWin: boolean;
  /** A real bet was placed in this run and the run has not closed yet. */
  runOpen: boolean;
  singles: number;
  virtual: boolean;
  virtualWins: number;
}

const freshState = (): State => ({
  history: '',
  twoRemaining: 0,
  pattern: 'First bet',
  side: 'P',
  runMicro: 0,
  stakeUnits: UNIT,
  previousWin: false,
  runOpen: false,
  singles: 0,
  virtual: false,
  virtualWins: 0,
});

const opposite = (side: Side): Side => (side === 'P' ? 'B' : 'P');
const unitsText = (units: number): string => String(Math.round(units * 100) / 100);

/**
 * Izak's Holy Grail, as specified by the IHG document and the client's PRD.
 *
 * Selection (ties ignored): first bet Player, then follow a streak and oppose a chop on the last two
 * results; a 2-2 pattern (PPBB / BBPP) is followed until a prediction loses.
 * Money (linear recovery): a loss restarts at one unit; a win inside an open run bets
 * ceil(deficit) + 1 units. A run closes when its net is above zero. Reaching the run-end stake closes
 * the run whether it wins or loses. Reaching the single-win limit switches to virtual bets until two
 * consecutive virtual wins; the open run's debt is kept and betting resumes at one unit.
 * If the run-end stake and the single-win limit are reached on the same hand, the run closes and
 * virtual play starts immediately (the application rule approved on 2026-10-18).
 */
export class IhgEngine implements StrategyEngine {
  readonly name = 'IHG';
  private s: State = freshState();
  private hands = 0;

  /** Runs that closed in profit, and runs that were given up (run-end loss, hard cap, cut, reset). */
  private runsClosed = 0;
  private runsCut = 0;

  constructor(private cfg: IhgConfig) {}

  counters(): { runsClosed: number; runsCut: number } {
    return { runsClosed: this.runsClosed, runsCut: this.runsCut };
  }

  configure(settings: Settings): void {
    this.cfg = ihgConfig(settings);
  }

  plan(paused: boolean): BetPlan {
    const { s, cfg } = this;
    const units = s.stakeUnits / UNIT;
    const stakeMicro = roundUpToChip(Math.round(units * cfg.unitMicro), cfg.chipMicro);
    const placedUnits = stakeMicro / cfg.unitMicro;
    const final = cfg.runEndUnits !== null && placedUnits >= cfg.runEndUnits;
    const virtual = paused || s.virtual;
    return {
      mode: virtual ? 'VIRTUAL' : 'REAL',
      side: s.side,
      units,
      stakeMicro,
      final,
      pattern: s.pattern,
      reason: paused
        ? 'Paused - watching only'
        : s.virtual
          ? `Virtual until ${VIRTUAL_WINS_TO_RESUME} wins in a row (${s.virtualWins}/${VIRTUAL_WINS_TO_RESUME})`
          : undefined,
    };
  }

  settle(result: Result, paused: boolean): HandResult {
    const plan = this.plan(paused);
    const s = this.s;
    this.hands++;

    if (result === 'T') {
      return {
        plan, result, outcome: plan.mode === 'REAL' ? 'TIE' : 'V-TIE', pnlMicro: 0,
        pattern: s.pattern, note: '', singleWins: s.singles, runNetMicro: s.runMicro, runClosed: false,
      };
    }

    const winner: Side = result;
    const won = plan.side === winner;
    let pnlMicro = 0;
    let note = '';
    let runClosed = false;
    let runShown = s.runMicro;
    let singlesShown = s.singles;

    if (paused) {
      // Watch only: the selection keeps following the table, nothing else moves.
    } else if (s.virtual) {
      s.virtualWins = won ? s.virtualWins + 1 : 0;
      if (!won && s.previousWin) s.singles++;
      s.previousWin = won;
      singlesShown = s.singles;
      if (s.virtualWins >= VIRTUAL_WINS_TO_RESUME) {
        s.virtual = false;
        s.virtualWins = 0;
        s.singles = 0;
        s.previousWin = false;
        s.stakeUnits = UNIT;
        singlesShown = 0;
        note = `${VIRTUAL_WINS_TO_RESUME} virtual wins in a row - real betting resumes at 1 unit`;
      }
    } else {
      pnlMicro = won ? Math.round(plan.stakeMicro * (winner === 'B' ? BANKER_PAYOUT : 1)) : -plan.stakeMicro;
      s.runMicro += pnlMicro;
      runShown = s.runMicro;
      if (!won && s.previousWin) s.singles++;
      singlesShown = s.singles;
      const pause = this.cfg.singlesLimit !== null && s.singles >= this.cfg.singlesLimit;

      if (s.runMicro > 0 || plan.final) {
        runClosed = true;
        const net = unitsText(s.runMicro / this.cfg.unitMicro);
        note = plan.final ? `run-end stake ${unitsText(plan.units)}u - run closed (${net}u)` : `run closed +${net}u`;
        // The document shows the counter already reset when the run closes in profit, and as it
        // stood when a run-end stake closes the run at a loss.
        if (s.runMicro > 0) {
          singlesShown = 0;
          this.runsClosed++;
        } else this.runsCut++;
        s.runMicro = 0;
        s.singles = 0;
        s.previousWin = false;
        s.runOpen = false;
        s.stakeUnits = UNIT;
        if (plan.final && pause) {
          s.virtual = true;
          s.virtualWins = 0;
          note += ' - single-win limit reached, virtual bets until 2 wins in a row';
        }
      } else {
        s.previousWin = won;
        s.runOpen = true;
        s.stakeUnits = won ? this.recoveryStake() : UNIT;
        if (pause) {
          s.virtual = true;
          s.virtualWins = 0;
          note = 'single-win limit reached - virtual bets until 2 wins in a row';
        }
        const cap = this.cfg.hardCapUnits;
        if (cap !== null && s.stakeUnits / UNIT > cap) {
          note = `${note ? note + ' - ' : ''}hard cap exceeded - run dropped`;
          this.cutRun();
          runClosed = true;
        }
      }
    }

    this.advancePattern(winner, won);
    return {
      plan,
      result,
      outcome: plan.mode === 'REAL' ? (won ? 'WIN' : 'LOSS') : won ? 'V-WIN' : 'V-LOSS',
      pnlMicro,
      pattern: s.pattern,
      note,
      singleWins: singlesShown,
      runNetMicro: runShown,
      runClosed,
    };
  }

  /** ceil(deficit) + 1 units, with the deficit rounded up to the configured step. */
  private recoveryStake(): number {
    const { s, cfg } = this;
    const deficit = Math.max(0, Math.round((-s.runMicro / cfg.unitMicro) * UNIT));
    const step = cfg.stepUnitsMicro;
    const rounded = step > 0 ? Math.ceil(deficit / step) * step : deficit;
    return rounded + UNIT;
  }

  private advancePattern(winner: Side, won: boolean): void {
    const s = this.s;
    s.history = (s.history + winner).slice(-4);
    const h = s.history;
    if (s.twoRemaining > 0 && won) {
      s.twoRemaining--;
      s.side = s.twoRemaining === 0 ? opposite(winner) : winner;
      if (s.twoRemaining === 0) s.twoRemaining = 2;
      s.pattern = '2-2';
    } else if (h.length === 4 && h[0] === h[1] && h[2] === h[3] && h[0] !== h[2]) {
      s.side = opposite(winner);
      s.twoRemaining = 2;
      s.pattern = '2-2';
    } else {
      s.twoRemaining = 0;
      if (h.length >= 2) {
        s.side = h[h.length - 2] as Side;
        s.pattern = h[h.length - 1] === h[h.length - 2] ? 'Streak' : 'Chop';
      } else {
        s.side = winner;
        s.pattern = 'Follow';
      }
    }
  }

  newShoe(): void {
    if (this.hands === 0 || this.cfg.shoePolicy === 'continue') return;
    const s = this.s;
    s.history = '';
    s.twoRemaining = 0;
    s.pattern = 'First bet';
    s.side = 'P';
    // Cut the open sequence so no deficit is carried into the new shoe.
    if (this.cfg.shoePolicy === 'reset_all') this.cutRun();
  }

  cutRun(): void {
    const s = this.s;
    if (s.runOpen) this.runsCut++;
    s.runMicro = 0;
    s.stakeUnits = UNIT;
    s.previousWin = false;
    s.runOpen = false;
    // Virtual mode is a table safety valve rather than a deficit, so it keeps running until its
    // two wins arrive; its single-win count is only cleared when betting is live.
    if (!s.virtual) s.singles = 0;
  }

  reset(): void {
    if (this.s.runOpen) this.runsCut++;
    this.s = freshState();
  }

  status(): EngineStatus {
    return {
      run_net_units: Math.round((this.s.runMicro / this.cfg.unitMicro) * 100) / 100,
      single_wins: this.s.singles,
      virtual_mode: this.s.virtual,
      run_open: this.s.runOpen,
    };
  }

  snapshot(): unknown {
    return { ...this.s, hands: this.hands, runsClosed: this.runsClosed, runsCut: this.runsCut };
  }

  restore(snapshot: unknown): void {
    const { hands, runsClosed, runsCut, ...state } = snapshot as State & { hands?: number; runsClosed?: number; runsCut?: number };
    this.s = { ...freshState(), ...state };
    this.hands = hands ?? 0;
    this.runsClosed = runsClosed ?? 0;
    this.runsCut = runsCut ?? 0;
  }
}
