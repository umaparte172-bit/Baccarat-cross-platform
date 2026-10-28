import { createEngine } from './factory.js';
import { roundUpToChip } from './money.js';
import { RiskGovernor, keepAliveBet, riskConfig, type HandPlan, type RiskConfig, type Verdict } from './risk.js';
import type { Settings } from './settings.js';
import type { HandResult, StrategyEngine } from './strategy.js';
import type { Mode, Outcome, Result, Side } from './types.js';

export interface PlayEvent {
  result: Result;
  /** True when this hand starts a new shoe. */
  newShoe?: boolean;
}

/** Everything that happened on one hand, ready for a row, a log line or a statistic. */
export interface PlayedHand {
  res: HandResult;
  hand: HandPlan;
  /** REAL, VIRTUAL, or MIN (a keep-alive bet riding on a hand the strategy sat out). */
  mode: Mode;
  /** The side shown for the hand: the strategy's pick, the keep-alive bet's side, or '' for an observed hand. */
  pick: Side | '';
  outcome: Outcome;
  pnlMicro: number;
  /** Money staked on this hand (0 for virtual hands). */
  stakeMicro: number;
  keepAliveNote: string;
  verdict: Verdict;
}

/** A real bet the program is about to place, or has placed. */
export interface IntendedBet {
  side: Side;
  stakeMicro: number;
  /** The bet is the keep-alive minimum, not the strategy's own. */
  keepAlive: boolean;
}

export interface PlayCoreOptions {
  /** The backtest ignores keep-alive bets. */
  keepAlive?: boolean;
}

/**
 * One session's play, without any I/O: the strategy engine, the risk layer, and the money.
 * The live session and the backtest both call `play()`, so they cannot disagree about a hand.
 */
export class PlayCore {
  readonly engine: StrategyEngine;
  readonly governor: RiskGovernor;
  risk: RiskConfig;

  pnlMicro = 0;
  peakMicro = 0;
  maxDrawdownMicro = 0;
  maxStakeUnits = 0;
  /** Hands played, ties included. */
  hands = 0;
  realBets = 0;
  virtualHands = 0;
  wins = 0;
  losses = 0;
  turnoverMicro = 0;

  private readonly keepAliveOn: boolean;

  constructor(engine: StrategyEngine, settings: Settings, options: PlayCoreOptions = {}) {
    this.keepAliveOn = options.keepAlive ?? true;
    this.engine = engine;
    this.risk = this.riskFor(settings);
    this.governor = new RiskGovernor(this.risk);
  }

  /** Build a core for the strategy in `settings`, or say why that cannot be run. */
  static create(settings: Settings, options: PlayCoreOptions = {}): { core: PlayCore } | { error: string } {
    const made = createEngine(settings);
    if ('error' in made) return made;
    return { core: new PlayCore(made.engine, settings, options) };
  }

  private riskFor(settings: Settings): RiskConfig {
    const risk = riskConfig(settings);
    return this.keepAliveOn ? risk : { ...risk, keepAliveEnabled: false };
  }

  /** Apply changed settings to a running session; money and counters are untouched. */
  configure(settings: Settings): void {
    this.engine.configure(settings);
    this.risk = this.riskFor(settings);
    this.governor.configure(this.risk);
  }

  /** A new shoe is starting: apply the shoe-change policy now (a live table announces it before the first hand). */
  shoeChange(): void {
    this.engine.newShoe();
    this.governor.newShoe();
  }

  /**
   * The real bet the next hand will carry (the strategy's bet or a keep-alive bet), or null when it carries none.
   * The program places exactly this on a live table, and checks that what it settles is the same. Pure: the engine is
   * left as it was.
   */
  intendedBet(paused: boolean): IntendedBet | null {
    if (paused) return null;
    const { engine, governor } = this;
    const hand = governor.prepare(engine, false);
    let plan;
    if (hand.cutRun) {
      // The near-shoe-end policy drops the open run before the hand is played, so the bet is what is left after that.
      const before = engine.snapshot();
      engine.cutRun();
      plan = engine.plan(hand.watchOnly);
      engine.restore(before);
    } else {
      plan = engine.plan(hand.watchOnly);
    }
    if (hand.keepAlive) {
      const side = plan.side ?? 'P';
      return { side, stakeMicro: roundUpToChip(this.risk.keepAliveMicro, this.risk.chipMicro), keepAlive: true };
    }
    if (!hand.watchOnly && plan.mode === 'REAL' && plan.side !== null) return { side: plan.side, stakeMicro: plan.stakeMicro, keepAlive: false };
    return null;
  }

  /** Is the next hand the first of a new shoe in a session that has already started? */
  startsNewShoe(event: PlayEvent): boolean {
    return event.newShoe === true && this.hands > 0;
  }

  /**
   * Play one hand. `dayBeforeMicro` is the day's profit/loss before this hand (the daily limits look at
   * the day, not just the session); `paused` means the user pressed PAUSE (watch only).
   */
  play(event: PlayEvent, paused: boolean, dayBeforeMicro: number): PlayedHand {
    const { engine, governor } = this;
    if (event.newShoe) this.shoeChange();

    const hand = governor.prepare(engine, paused);
    if (hand.cutRun) engine.cutRun();
    const res = engine.settle(event.result, paused || hand.watchOnly);

    // The keep-alive minimum bet rides on a hand the strategy sat out; its money is real.
    let mode: Mode = res.plan.mode;
    let outcome = res.outcome;
    let pnlMicro = res.pnlMicro;
    let stakeMicro = res.plan.mode === 'REAL' ? res.plan.stakeMicro : 0;
    let keepAliveNote = '';
    let pick: Side | '' = res.plan.side ?? '';
    if (hand.keepAlive) {
      // With no pick of its own the strategy keeps the seat alive on Player (no commission).
      pick = res.plan.side ?? 'P';
      const k = keepAliveBet(pick, event.result, this.risk);
      mode = 'MIN';
      outcome = k.outcome;
      pnlMicro = k.pnlMicro;
      stakeMicro = k.stakeMicro;
      keepAliveNote = 'keep-alive minimum bet';
    }

    this.hands++;
    this.pnlMicro += pnlMicro;
    if (this.pnlMicro > this.peakMicro) this.peakMicro = this.pnlMicro;
    const drawdown = this.peakMicro - this.pnlMicro;
    if (drawdown > this.maxDrawdownMicro) this.maxDrawdownMicro = drawdown;
    this.turnoverMicro += stakeMicro;

    if (mode === 'VIRTUAL') this.virtualHands++;
    else if (mode === 'REAL') {
      this.realBets++;
      const units = Math.round(res.plan.units * 100) / 100;
      if (units > this.maxStakeUnits) this.maxStakeUnits = units;
      if (outcome === 'WIN') this.wins++;
      else if (outcome === 'LOSS') this.losses++;
    }

    const verdict = governor.settled({
      res, hand, manualPause: paused, keepAlive: hand.keepAlive,
      sessionPnlMicro: this.pnlMicro, dayPnlMicro: dayBeforeMicro + pnlMicro,
    });
    if (verdict.resetEngine) engine.reset();
    return { res, hand, mode, pick, outcome, pnlMicro, stakeMicro, keepAliveNote, verdict };
  }
}
