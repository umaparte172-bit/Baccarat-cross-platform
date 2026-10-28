import { Shoe, dealHand, mulberry32, randomSeed } from '../engine/dealer.js';
import type { Cards, Result } from '../engine/types.js';

export interface HandEvent {
  result: Result;
  cards: Cards | null;
  /** True when this hand starts a new shoe (replay "|" marker or simulator reshuffle). */
  newShoe?: boolean;
}

export type HandHandler = (event: HandEvent) => void;

export interface Feed {
  start(): void;
  stop(): void;
  /** Reconnect without losing session state. */
  retry(): void;
  /** Manual feed only: returns false when this feed does not accept pushed results. */
  push(result: Result): boolean;
  /** Called once when a finite feed (replay) has delivered everything. */
  onFinished?: () => void;
}

/** 8-deck simulator. A given seed always produces the same sequence of shoes and hands. */
export class SimulatorFeed implements Feed {
  onFinished?: () => void;
  private timer: NodeJS.Timeout | null = null;
  private active = false;
  private readonly rng: () => number;
  private shoe: Shoe;
  private freshShoe = true;

  constructor(
    private readonly handler: HandHandler,
    private readonly intervalSeconds: () => number,
    seed: number,
  ) {
    this.rng = mulberry32(seed > 0 ? seed : randomSeed());
    this.shoe = new Shoe(this.rng);
  }

  start(): void {
    this.active = true;
    this.schedule();
  }

  stop(): void {
    this.active = false;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }

  retry(): void {
    if (!this.active) return;
    if (this.timer) clearTimeout(this.timer);
    this.schedule();
  }

  push(): boolean {
    return false;
  }

  private schedule(): void {
    const ms = Math.round(this.intervalSeconds() * 1000);
    this.timer = setTimeout(() => this.tick(), ms);
  }

  private tick(): void {
    if (!this.active) return;
    const { result, cards } = dealHand(this.shoe);
    const newShoe = this.freshShoe;
    this.freshShoe = false;
    if (this.shoe.cutReached) {
      this.shoe = new Shoe(this.rng);
      this.freshShoe = true;
    }
    this.handler({ result, cards, newShoe });
    if (this.active) this.schedule();
  }
}

/** Results arrive from the UI buttons. */
export class ManualFeed implements Feed {
  private active = false;
  constructor(private readonly handler: HandHandler) {}
  start(): void {
    this.active = true;
  }
  stop(): void {
    this.active = false;
  }
  retry(): void {}
  push(result: Result): boolean {
    if (!this.active) return false;
    this.handler({ result, cards: null });
    return true;
  }
}

/** Pasted letters: P / B / T, "|" starts a new shoe, anything else is ignored. */
export function parseReplay(text: string): HandEvent[] {
  const events: HandEvent[] = [];
  let newShoe = true;
  for (const ch of text.toUpperCase()) {
    if (ch === '|') newShoe = true;
    else if (ch === 'P' || ch === 'B' || ch === 'T') {
      events.push({ result: ch, cards: null, newShoe });
      newShoe = false;
    }
  }
  return events;
}

export class ReplayFeed implements Feed {
  onFinished?: () => void;
  private active = false;
  private index = 0;
  private timer: NodeJS.Timeout | null = null;

  constructor(private readonly handler: HandHandler, private readonly events: HandEvent[]) {}

  start(): void {
    this.active = true;
    this.schedule();
  }
  stop(): void {
    this.active = false;
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
  }
  retry(): void {}
  push(): boolean {
    return false;
  }

  private schedule(): void {
    this.timer = setTimeout(() => this.tick(), 0);
  }

  private tick(): void {
    if (!this.active) return;
    const event = this.events[this.index++];
    if (!event) {
      this.active = false;
      this.onFinished?.();
      return;
    }
    this.handler(event);
    if (this.active) this.schedule();
  }
}
