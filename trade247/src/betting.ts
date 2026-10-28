import type { PlayCore } from './engine/play.js';
import type { Settings } from './engine/settings.js';
import type { Side } from './engine/types.js';
import type { Placement } from './live/bettor.js';

/**
 * Real betting on a live table. The session decides what to bet (the strategy and the risk layer, as always);
 * this class decides whether and when it is placed, and keeps the books honest about it:
 *
 *  - Safety limits come first: a bet above the largest allowed stake, or one that would take the session past its
 *    staking limit, is never placed and ends the session instead.
 *  - In "confirm" mode a bet waits for a click on "Place bet"; in "auto" mode it is placed after a short random pause.
 *  - A bet is placed only if the strategy still wants exactly that bet at that moment.
 *  - When the result arrives, what was placed must equal what the strategy would have bet. If it does not, or if the
 *    table did not confirm the amount, the session stops and says so: money may be on the table that the books do not know.
 *  - A bet that was wanted but not placed (window closed, chip missing) is booked as a hand that was only watched.
 */

export interface PendingBet {
  side: Side;
  stakeMicro: number;
  keepAlive: boolean;
  /** Hands recorded when the bet was prepared: the bet is void if a hand has been settled since. */
  hands: number;
  expiresAt: number;
}

export interface BetHost {
  settings(): Settings;
  core(): PlayCore | null;
  now(): number;
  running(): boolean;
  paused(): boolean;
  feedLost(): boolean;
  handsRecorded(): number;
  place(side: Side, stake: number, shouldAbort: () => boolean): Promise<Placement>;
  /** End the session because of a betting problem and tell the user (the Telegram alert is sent by the host). */
  stopForBet(status: string, message: string): void;
  random(): number;
}

/** What to do with the hand that has just been dealt. */
export type HandDecision =
  | { action: 'play'; forcePaused: boolean; note: string }
  | { action: 'stop' };

export interface BetView {
  mode: Settings['bet_mode'];
  pending: { pick: 'PLAYER' | 'BANKER'; stake: number; keep_alive: boolean; expires_ts: number } | null;
  /** True while the bet is being clicked in, or sits on the table waiting for its result. */
  on_table: boolean;
  staked: number;
  max_stake: number;
  max_session: number;
  note: string;
}

const MICRO = 1_000_000;

export class BetCoordinator {
  private pending: PendingBet | null = null;
  private placed: { side: Side; stakeMicro: number } | null = null;
  private placing = false;
  private stakedMicro = 0;
  private note = '';
  private timer: NodeJS.Timeout | null = null;
  private pauseAfterResult = false;

  constructor(private readonly host: BetHost) {}

  get active(): boolean {
    const s = this.host.settings();
    return this.host.running() && s.feed === 'live' && s.bet_mode !== 'paper';
  }

  /** A bet is being placed or is on the table: pausing now would leave it unaccounted for. */
  get busy(): boolean {
    return this.placing || this.placed !== null;
  }

  /** Called when a pause is asked for while a bet is on the table: it takes effect after the result. */
  requestPauseAfterResult(): void {
    this.pauseAfterResult = true;
  }

  takePauseRequest(): boolean {
    const asked = this.pauseAfterResult;
    this.pauseAfterResult = false;
    return asked;
  }

  clear(): void {
    this.cancelTimer();
    this.pending = null;
    this.placed = null;
    this.placing = false;
    this.stakedMicro = 0;
    this.note = '';
    this.pauseAfterResult = false;
  }

  view(): BetView {
    const s = this.host.settings();
    const p = this.pending;
    return {
      mode: s.bet_mode,
      pending: p
        ? { pick: p.side === 'P' ? 'PLAYER' : 'BANKER', stake: p.stakeMicro / MICRO, keep_alive: p.keepAlive, expires_ts: p.expiresAt / 1000 }
        : null,
      on_table: this.busy,
      staked: Math.round(this.stakedMicro / 10_000) / 100,
      max_stake: s.bet_max_stake,
      max_session: s.bet_max_session,
      note: this.note,
    };
  }

  // ---------------------------------------------------------------- before the hand: place the bet

  /** The live table says bets are open for a new round. */
  onWindowOpen(): void {
    const host = this.host;
    const core = host.core();
    if (!this.active || !core || host.paused() || host.feedLost() || this.placing || this.placed || this.pending) return;
    const intended = core.intendedBet(false);
    if (!intended) return;

    const s = host.settings();
    const stake = intended.stakeMicro / MICRO;
    if (stake > s.bet_max_stake + 1e-9) {
      host.stopForBet('STOPPED (stake above the safety limit)', `the next bet is ${stake.toFixed(2)}, above the largest allowed stake of ${s.bet_max_stake.toFixed(2)}`);
      return;
    }
    if ((this.stakedMicro + intended.stakeMicro) / MICRO > s.bet_max_session + 1e-9) {
      host.stopForBet('STOPPED (session staking limit)', `this bet would take the session past its staking limit of ${s.bet_max_session.toFixed(2)}`);
      return;
    }

    const order: PendingBet = {
      side: intended.side,
      stakeMicro: intended.stakeMicro,
      keepAlive: intended.keepAlive,
      hands: host.handsRecorded(),
      expiresAt: host.now() + s.bet_confirm_seconds * 1000,
    };
    this.note = '';
    if (s.bet_mode === 'confirm') {
      this.pending = order;
      this.cancelTimer();
      this.timer = setTimeout(() => {
        if (this.pending === order) {
          this.pending = null;
          this.note = 'bet skipped: not approved in time';
        }
      }, s.bet_confirm_seconds * 1000);
    } else {
      const delay = (s.bet_delay_min + host.random() * Math.max(0, s.bet_delay_max - s.bet_delay_min)) * 1000;
      this.cancelTimer();
      this.timer = setTimeout(() => void this.placeNow(order), delay);
    }
  }

  /** "Place bet" in confirm mode. */
  approve(): { ok: boolean; error?: string } {
    const order = this.pending;
    if (!order) return { ok: false, error: 'No bet is waiting for approval' };
    this.pending = null;
    this.cancelTimer();
    void this.placeNow(order);
    return { ok: true };
  }

  /** "Skip" in confirm mode. */
  skip(): { ok: boolean; error?: string } {
    if (!this.pending) return { ok: false, error: 'No bet is waiting for approval' };
    this.pending = null;
    this.cancelTimer();
    this.note = 'bet skipped by you';
    return { ok: true };
  }

  private cancelTimer(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  private async placeNow(order: PendingBet): Promise<void> {
    const host = this.host;
    const core = host.core();
    if (!this.active || !core || this.placing) return;
    // The delay or the wait for approval may have outlived the situation the bet was prepared for.
    const again = core.intendedBet(host.paused());
    if (!again || again.side !== order.side || again.stakeMicro !== order.stakeMicro || host.handsRecorded() !== order.hands) {
      this.note = 'bet skipped: the situation changed before it was placed';
      return;
    }

    this.placing = true;
    let result: Placement;
    try {
      result = await host.place(order.side, order.stakeMicro / MICRO, () => !host.running());
    } catch (e) {
      result = { kind: 'unconfirmed', reason: (e as Error).message };
    }
    this.placing = false;
    if (!host.running()) return;

    if (result.kind === 'rejected') {
      this.note = `bet not placed: ${result.reason}`;
      return;
    }
    if (result.kind === 'unconfirmed') {
      host.stopForBet('STOPPED (bet not confirmed - check the table)', `the table did not confirm the bet: ${result.reason}`);
      return;
    }
    if (host.handsRecorded() !== order.hands) {
      host.stopForBet('STOPPED (bet placed too late - check the table)', 'a result arrived while the bet was being placed, so it cannot be matched to a round');
      return;
    }
    this.placed = { side: order.side, stakeMicro: order.stakeMicro };
    this.stakedMicro += order.stakeMicro;
  }

  // ---------------------------------------------------------------- after the hand: settle it

  /**
   * A result has arrived. Says how to book it: normally, or as a hand that was only watched because the bet the
   * strategy wanted was not placed. Stops the session if what was placed is not what the strategy would have bet.
   */
  beforeHand(core: PlayCore, paused: boolean): HandDecision {
    if (!this.active) return { action: 'play', forcePaused: false, note: '' };
    const intended = core.intendedBet(paused);
    const placed = this.placed;
    this.placed = null;
    this.pending = null;
    this.cancelTimer();

    if (placed) {
      const same = intended !== null && intended.side === placed.side && intended.stakeMicro === placed.stakeMicro;
      if (!same) {
        this.host.stopForBet(
          'STOPPED (placed bet differs from the strategy - check the table)',
          `a ${(placed.stakeMicro / MICRO).toFixed(2)} bet on ${placed.side === 'P' ? 'Player' : 'Banker'} is on the table but the strategy would now bet ${intended ? `${(intended.stakeMicro / MICRO).toFixed(2)} on ${intended.side === 'P' ? 'Player' : 'Banker'}` : 'nothing'}`,
        );
        return { action: 'stop' };
      }
      this.note = '';
      return { action: 'play', forcePaused: false, note: '' };
    }
    if (intended) {
      const why = this.note || 'bet not placed';
      this.note = '';
      return { action: 'play', forcePaused: true, note: why };
    }
    this.note = '';
    return { action: 'play', forcePaused: false, note: '' };
  }
}
