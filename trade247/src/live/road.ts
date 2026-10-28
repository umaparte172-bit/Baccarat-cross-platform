import type { Result } from '../engine/types.js';
import { diffRoad } from './profile.js';

const same = (a: readonly Result[], b: readonly Result[]): boolean => a.length === b.length && a.every((r, i) => b[i] === r);

/** What a new reading of the road means: a new shoe has started, or one more hand was dealt. */
export type RoadEvent = { kind: 'shoe' } | { kind: 'hand'; result: Result };

/**
 * Turns successive readings of a table's road into hands. The first reading is history and is not played; every hand
 * added after it is a result. A shorter road means a new shoe (reported at once, before any of its hands, so the
 * strategy is reset before it decides the first bet of the shoe), but only once two readings in a row agree, because a
 * page that is half redrawn looks the same.
 */
export class RoadTracker {
  private baseline: Result[] | null = null;
  private pendingReset: Result[] | null = null;

  /** Forget everything (after an outage the hands in between were not seen): the next reading is history again. */
  forget(): void {
    this.baseline = null;
    this.pendingReset = null;
  }

  consume(items: readonly Result[]): RoadEvent[] {
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
      this.baseline = [];
      return [{ kind: 'shoe' }, ...this.consume(items)];
    }
    this.pendingReset = null;
    if (change.kind === 'same') return [];
    this.baseline = [...items];
    return change.fresh.map((result): RoadEvent => ({ kind: 'hand', result }));
  }
}
