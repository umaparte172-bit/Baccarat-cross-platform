import { selectChips } from '../engine/chips.js';
import type { Side } from '../engine/types.js';
import type { TablePage, WindowSnapshot } from './page.js';
import type { BettingProfile } from './profile.js';

/**
 * What happened when a bet was placed on the table.
 *  - accepted: the table itself shows exactly the stake.
 *  - rejected: nothing was placed (the window was closed, a chip was missing, ...). The hand is simply not bet.
 *  - unconfirmed: money may be on the table but the table did not confirm the amount. Nothing is repeated;
 *    the caller must stop and tell the user.
 */
export type Placement =
  | { kind: 'accepted'; chips: number[]; total: number }
  | { kind: 'rejected'; reason: string }
  | { kind: 'unconfirmed'; reason: string };

export interface BettorOptions {
  /** Pause between two clicks, in milliseconds [min, max]. */
  clickGapMs?: [number, number];
  /** How long the table has to show the stake after the last click. */
  confirmMs?: number;
  /** Most chip clicks one bet may take. */
  maxClicks?: number;
  /** Checked between clicks; true stops placing (what is already on the table is then unconfirmed). */
  shouldAbort?: () => boolean;
  random?: () => number;
}

const EPSILON = 0.005;
const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

/**
 * Places a bet the way a person would: select a chip, click the Player or Banker spot, repeat until the stake is
 * made up, then wait for the table to show it. It only starts when the window is open and nothing is on the table,
 * works only with what the table shows (chips that exist, an amount it displays), and never retries.
 */
export class TableBettor {
  constructor(
    private readonly page: TablePage,
    private readonly cfg: BettingProfile,
    private readonly opts: BettorOptions = {},
  ) {}

  readWindow(): Promise<WindowSnapshot | null> {
    return this.page.readWindow(this.cfg);
  }

  async place(side: Side, stake: number): Promise<Placement> {
    const rejected = (reason: string): Placement => ({ kind: 'rejected', reason });
    const snap = await this.readWindow().catch(() => null);
    if (!snap) return rejected('The betting window is not on the page');
    const w = snap.reading;
    if (!w.open) return rejected(`Bets are not open (the table says "${w.text}")`);
    if (side === 'P' ? !w.hasPlayer : !w.hasBanker) return rejected(`Cannot find the ${side === 'P' ? 'Player' : 'Banker'} spot`);
    if (w.total === null) return rejected('Cannot read the total on the table, so a bet could not be confirmed');
    if (w.total > EPSILON) return rejected('A bet is already on the table');

    const usable = w.chips.filter((c) => c.visible && c.value !== null && c.value > 0);
    const choice = selectChips(stake, usable.map((c) => c.value as number), this.opts.maxClicks ?? 40);
    if (!choice.ok) return rejected(choice.reason);

    const spot = side === 'P' ? this.cfg.playerSelector : this.cfg.bankerSelector;
    const [gapMin, gapMax] = this.opts.clickGapMs ?? [60, 180];
    const random = this.opts.random ?? Math.random;
    const gap = (): Promise<void> => sleep(gapMin + random() * Math.max(0, gapMax - gapMin));

    try {
      await this.page.bringToFront();
    } catch (e) {
      return rejected(`Cannot bring the table to the front: ${(e as Error).message}`);
    }

    let placed = 0;
    let selected: number | null = null;
    for (const value of choice.chips) {
      if (this.opts.shouldAbort?.()) return placed > 0 ? { kind: 'unconfirmed', reason: 'Stopped while placing the bet' } : rejected('Stopped before placing the bet');
      try {
        if (selected !== value) {
          const chip = usable.find((c) => c.value === value)!;
          await this.page.clickElement(snap.frame, this.cfg.chipSelector, chip.index);
          selected = value;
          await gap();
        }
        await this.page.clickElement(snap.frame, spot, 0);
        placed++;
        await gap();
      } catch (e) {
        const reason = (e as Error).message;
        return placed > 0 ? { kind: 'unconfirmed', reason: `A click failed after part of the bet was placed: ${reason}` } : rejected(reason);
      }
    }
    return this.confirm(stake, choice.chips);
  }

  /** Waits for the table to show exactly the stake. */
  private async confirm(stake: number, chips: number[]): Promise<Placement> {
    const deadline = Date.now() + (this.opts.confirmMs ?? 3000);
    let last: number | null = null;
    while (Date.now() <= deadline) {
      const snap = await this.readWindow().catch(() => null);
      last = snap?.reading.total ?? null;
      if (last !== null && Math.abs(last - stake) <= EPSILON) return { kind: 'accepted', chips, total: last };
      if (last !== null && last > stake + EPSILON) {
        return { kind: 'unconfirmed', reason: `The table shows ${last} on the spot but ${stake} was requested` };
      }
      await sleep(120);
    }
    return { kind: 'unconfirmed', reason: `The table did not show the bet within the time allowed (total ${last ?? 'unreadable'}, expected ${stake})` };
  }
}
