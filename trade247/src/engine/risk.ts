import { BANKER_PAYOUT, roundUpToChip, toMicro } from './money.js';
import type { Settings } from './settings.js';
import type { HandResult, StrategyEngine } from './strategy.js';
import type { Outcome, Result, Side } from './types.js';

/** Winning picks in a row needed to leave "WAIT FOR PATTERN". */
export const WAIT_WINS = 2;

export interface RiskConfig {
  takeProfitMicro: number;
  stopLossMicro: number;
  dailyTakeProfitMicro: number;
  dailyStopLossMicro: number;
  keepAliveEnabled: boolean;
  keepAliveMicro: number;
  keepAliveIdle: number;
  chipMicro: number;
  stopAfterLosses: number;
  stopAction: 'STOP BET' | 'WAIT FOR PATTERN';
  pauseAfterLosses: number;
  pauseHands: number;
  endShoePolicy: 'no_new_runs' | 'cut_run' | 'off';
  endShoeHand: number;
}

export function riskConfig(s: Settings): RiskConfig {
  return {
    takeProfitMicro: toMicro(s.take_profit),
    stopLossMicro: toMicro(s.stop_loss),
    dailyTakeProfitMicro: toMicro(s.daily_take_profit),
    dailyStopLossMicro: toMicro(s.daily_stop_loss),
    keepAliveEnabled: s.keepalive_enabled,
    keepAliveMicro: toMicro(s.keepalive_amount),
    keepAliveIdle: s.keepalive_idle,
    chipMicro: toMicro(s.min_chip),
    stopAfterLosses: s.stop_consec_loss_count,
    stopAction: s.stop_consec_loss_action,
    pauseAfterLosses: s.pause_consec_loss_count,
    pauseHands: s.pause_bet_count,
    endShoePolicy: s.end_shoe_policy,
    endShoeHand: s.end_shoe_hand,
  };
}

/** How the next hand is handled, decided before it is played. */
export interface HandPlan {
  /** The strategy only watches: no real bet, no run progress. */
  watchOnly: boolean;
  reason?: string;
  /** Drop the open run before this hand (near-shoe-end "cut" policy). */
  cutRun: boolean;
  /** Place the keep-alive minimum bet on this hand. */
  keepAlive: boolean;
}

export interface KeepAliveBet {
  outcome: Outcome;
  stakeMicro: number;
  pnlMicro: number;
}

/** The minimum bet that keeps the seat active; it follows the strategy's own pick and is real money. */
export function keepAliveBet(side: Side, result: Result, cfg: RiskConfig): KeepAliveBet {
  const stakeMicro = roundUpToChip(cfg.keepAliveMicro, cfg.chipMicro);
  if (result === 'T') return { outcome: 'TIE', stakeMicro, pnlMicro: 0 };
  const won = side === result;
  const pnlMicro = won ? Math.round(stakeMicro * (side === 'B' ? BANKER_PAYOUT : 1)) : -stakeMicro;
  return { outcome: won ? 'WIN' : 'LOSS', stakeMicro, pnlMicro };
}

export interface Stop {
  /** Pill text. "LOCKED" means the day's limit is reached and Start is refused until it is raised or tomorrow. */
  status: string;
  message: string;
  locked: boolean;
  /** Why the session stopped, as a stable code (the backtest counts these). */
  code: 'take_profit' | 'stop_loss' | 'daily_take_profit' | 'daily_stop_loss' | 'consecutive_losses';
  /** The Telegram alert this stop triggers, if any. */
  alert?: 'daily_stop_loss' | 'daily_take_profit';
}

export interface SettledInfo {
  res: HandResult;
  hand: HandPlan;
  manualPause: boolean;
  keepAlive: boolean;
  sessionPnlMicro: number;
  dayPnlMicro: number;
}

export interface Verdict {
  stop: Stop | null;
  /** WAIT FOR PATTERN is over: the engine starts again from scratch. */
  resetEngine: boolean;
}

const money = (micro: number): string => (Math.round(micro / 10_000) / 100).toFixed(2);

/**
 * Everything around the strategy that protects the bankroll: session and daily limits, the stop/pause
 * after consecutive losses, keep-alive bets, and the end-of-shoe policy. It never changes the strategy's
 * own rules; it only decides whether a hand is played for real, watched, or ends the session.
 *
 * A manual PAUSE freezes this layer too (countdowns, waiting and idle counters), so RESUME continues
 * exactly where it left off. The shoe's hand count keeps running because the shoe itself does.
 */
export class RiskGovernor {
  private consecutiveLosses = 0;
  private pauseLeft = 0;
  private waiting = false;
  private waitWins = 0;
  private idle = 0;
  private shoeHands = 0;

  constructor(private cfg: RiskConfig) {}

  configure(cfg: RiskConfig): void {
    this.cfg = cfg;
  }

  newShoe(): void {
    this.shoeHands = 0;
  }

  /** Pure: decides how the next hand is handled without changing anything. */
  prepare(engine: StrategyEngine, manualPause: boolean): HandPlan {
    const c = this.cfg;
    let watchOnly = false;
    let reason: string | undefined;
    let cutRun = false;
    const handNo = this.shoeHands + 1;

    if (this.pauseLeft > 0) {
      watchOnly = true;
      const losses = `${c.pauseAfterLosses} loss${c.pauseAfterLosses === 1 ? '' : 'es'}`;
      reason = `Paused after ${losses} - ${this.pauseLeft} hand(s) left`;
    } else if (this.waiting) {
      watchOnly = true;
      reason = `Waiting for ${WAIT_WINS} winning picks in a row (${this.waitWins}/${WAIT_WINS})`;
    } else if (c.endShoePolicy !== 'off' && handNo >= c.endShoeHand) {
      const status = engine.status();
      cutRun = c.endShoePolicy === 'cut_run' && status.run_open;
      const runStillOpen = status.run_open && !cutRun;
      if (!runStillOpen && engine.plan(false).mode === 'REAL') {
        watchOnly = true;
        reason = `Near shoe end (hand ${handNo}) - no new runs`;
      }
    }

    const noBet = manualPause || watchOnly || engine.plan(false).mode !== 'REAL';
    const keepAlive = c.keepAliveEnabled && !manualPause && noBet && this.idle + 1 >= c.keepAliveIdle;
    return { watchOnly, reason, cutRun, keepAlive };
  }

  /** Record a settled hand and report whether the session must stop. */
  settled(i: SettledInfo): Verdict {
    const c = this.cfg;
    const { res } = i;
    let resetEngine = false;
    let lossStop: Stop | null = null;
    this.shoeHands++;

    const realBet = !i.manualPause && !i.hand.watchOnly && res.plan.mode === 'REAL';
    if (!i.manualPause) {
      this.idle = realBet || i.keepAlive ? 0 : this.idle + 1;

      if (this.pauseLeft > 0) this.pauseLeft--;

      if (this.waiting && i.hand.watchOnly && res.result !== 'T') {
        this.waitWins = res.plan.side === res.result ? this.waitWins + 1 : 0;
        if (this.waitWins >= WAIT_WINS) {
          this.waiting = false;
          this.waitWins = 0;
          resetEngine = true;
        }
      }
    }

    if (realBet) {
      if (res.outcome === 'LOSS') this.consecutiveLosses++;
      else if (res.outcome === 'WIN') this.consecutiveLosses = 0;

      if (c.stopAfterLosses > 0 && this.consecutiveLosses >= c.stopAfterLosses) {
        if (c.stopAction === 'STOP BET') {
          lossStop = {
            status: `STOPPED (${c.stopAfterLosses} consecutive losses)`,
            message: `${c.stopAfterLosses} real bets lost in a row`,
            locked: false,
            code: 'consecutive_losses',
          };
        } else {
          this.waiting = true;
          this.waitWins = 0;
          this.consecutiveLosses = 0;
        }
      } else if (c.pauseAfterLosses > 0 && c.pauseHands > 0 && this.consecutiveLosses >= c.pauseAfterLosses) {
        this.pauseLeft = c.pauseHands;
        this.consecutiveLosses = 0;
      }
    }

    return { stop: this.limitStop(i) ?? lossStop, resetEngine };
  }

  private limitStop(i: SettledInfo): Stop | null {
    const c = this.cfg;
    if (c.dailyStopLossMicro > 0 && i.dayPnlMicro <= -c.dailyStopLossMicro) {
      return { status: 'LOCKED (daily stop loss)', message: `Daily stop loss reached (${money(i.dayPnlMicro)})`, locked: true, code: 'daily_stop_loss', alert: 'daily_stop_loss' };
    }
    if (c.dailyTakeProfitMicro > 0 && i.dayPnlMicro >= c.dailyTakeProfitMicro) {
      return { status: 'LOCKED (daily take profit)', message: `Daily take profit reached (+${money(i.dayPnlMicro)})`, locked: true, code: 'daily_take_profit', alert: 'daily_take_profit' };
    }
    if (c.stopLossMicro > 0 && i.sessionPnlMicro <= -c.stopLossMicro) {
      return { status: 'STOPPED (stop loss)', message: `Stop loss reached (${money(i.sessionPnlMicro)})`, locked: false, code: 'stop_loss' };
    }
    if (c.takeProfitMicro > 0 && i.sessionPnlMicro >= c.takeProfitMicro) {
      return { status: 'STOPPED (take profit)', message: `Take profit reached (+${money(i.sessionPnlMicro)})`, locked: false, code: 'take_profit' };
    }
    return null;
  }

  status(): { consecutive_losses: number; pause_left: number; waiting: boolean; shoe_hand: number; idle_hands: number } {
    return {
      consecutive_losses: this.consecutiveLosses,
      pause_left: this.pauseLeft,
      waiting: this.waiting,
      shoe_hand: this.shoeHands,
      idle_hands: this.idle,
    };
  }
}

/** Is a day's limit already reached? Used to refuse Start instead of opening a session that stops at once. */
export function dailyLockReason(cfg: RiskConfig, dayPnlMicro: number): string | null {
  if (cfg.dailyStopLossMicro > 0 && dayPnlMicro <= -cfg.dailyStopLossMicro) {
    return `Daily stop loss already reached today (${money(dayPnlMicro)}). Raise the limit or start again tomorrow`;
  }
  if (cfg.dailyTakeProfitMicro > 0 && dayPnlMicro >= cfg.dailyTakeProfitMicro) {
    return `Daily take profit already reached today (+${money(dayPnlMicro)}). Raise the limit or start again tomorrow`;
  }
  return null;
}
