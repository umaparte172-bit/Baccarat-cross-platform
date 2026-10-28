import type { HandEvent } from '../feeds/feed.js';
import type { Result } from '../engine/types.js';
import { diffRoad } from './profile.js';

const same = (a: readonly Result[], b: readonly Result[]): boolean => a.length === b.length && a.every((r, i) => b[i] === r);

/**
 * Turns successive readings of a table's road into hands. The first reading is history and is not played; every hand
 * added after it is a result. A shorter road means a new shoe, but only once two readings in a row agree, because a
 * page that is half redrawn looks the same.
 */
export class RoadTracker {
  private baseline: Result[] | null = null;
  private pendingReset: Result[] | null = null;
  private newShoePending = false;

  /** Forget everything (after an outage the hands in between were not seen): the next reading is history again. */
  forget(): void {
    this.baseline = null;
    this.pendingReset = null;
    this.newShoePending = false;
  }

  consume(items: readonly Result[]): HandEvent[] {
    if (this.baseline === null) {
      this.baseline = [...items];
      return [];
    }
    const change = diffRoad(this.baseline, items);
    if (change.kind === 'reset') {
      if (!this.pendingReset || !same(this.pendingReset, items)) {
        this.pendingReset = [...items];
        return [];
      }
      this.pendingReset = null;
      this.newShoePending = true;
      this.baseline = [];
      return this.consume(items);
    }
    this.pendingReset = null;
    if (change.kind === 'same') return [];
    this.baseline = [...items];
    return change.fresh.map((result) => {
      const newShoe = this.newShoePending;
      this.newShoePending = false;
      return { result, cards: null, newShoe };
    });
  }
}
