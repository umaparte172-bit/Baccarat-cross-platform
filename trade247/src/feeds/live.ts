import { listPages } from '../live/cdp.js';
import { PageSession } from '../live/page.js';
import type { LiveProfile } from '../live/profile.js';
import { RoadTracker } from '../live/road.js';
import type { Feed, HandHandler } from './feed.js';

export interface LiveOptions {
  /** Chrome's remote debugging port. */
  port: number;
  profile: LiveProfile;
  /** How often the road is read while a table is attached. */
  pollMs?: number;
  /** How often tabs are searched for a table while none is attached. */
  scanMs?: number;
  /** The table counts as lost when its road cannot be found for this long. */
  lostAfterMs?: number;
}

export interface LiveHooks {
  /** A table is attached (first time, or again after an outage). */
  connected(): void;
  /** Still looking for a table; `reason` tells the user what to do. */
  waiting(reason: string): void;
  /** A table that was attached has gone. Called once per outage. */
  lost(reason: string): void;
}

/**
 * Reads results from the user's own Chrome (read-only): the road of the table they have open. Nothing is clicked or
 * typed in the page. The road that is already there when the session starts is history and is not played; every hand
 * added afterwards is a result. A shorter road is a new shoe.
 */
export class LiveFeed implements Feed {
  private active = false;
  private timer: NodeJS.Timeout | null = null;
  private page: PageSession | null = null;
  private readonly road = new RoadTracker();
  private missingSince: number | null = null;
  private everConnected = false;
  private lostReported = false;
  private running = false;
  private wakeRequested = false;
  private wake: (() => void) | null = null;

  constructor(
    private readonly handler: HandHandler,
    private readonly options: LiveOptions,
    private readonly hooks: LiveHooks,
  ) {}

  start(): void {
    this.active = true;
    void this.loop();
  }

  stop(): void {
    this.active = false;
    this.wakeUp();
    this.dropPage();
  }

  /** "Retry Table": reconnect now. The road is read afresh; hands already seen are not repeated. */
  retry(): void {
    if (!this.active) return;
    this.dropPage();
    this.wakeRequested = true;
    this.wakeUp();
    if (!this.running) void this.loop();
  }

  push(): boolean {
    return false;
  }

  // ----------------------------------------------------------------

  private async loop(): Promise<void> {
    if (this.running) return;
    this.running = true;
    try {
      while (this.active) {
        this.wakeRequested = false;
        try {
          await this.step();
        } catch (e) {
          this.dropPage();
          this.noTable((e as Error).message);
        }
        if (!this.active) break;
        if (this.wakeRequested) continue;
        const ms = this.page ? (this.options.pollMs ?? 1000) : (this.options.scanMs ?? 2000);
        await new Promise<void>((resolve) => {
          this.wake = resolve;
          this.timer = setTimeout(resolve, ms);
        });
        this.wake = null;
        this.timer = null;
      }
    } finally {
      this.running = false;
    }
  }

  /** Ends the pause between two readings now. */
  private wakeUp(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = null;
    this.wake?.();
  }

  private dropPage(): void {
    this.page?.close();
    this.page = null;
  }

  private async step(): Promise<void> {
    if (!this.page) {
      this.page = await this.findTable();
      if (!this.active) {
        this.dropPage();
        return;
      }
      if (!this.page) return;
      this.missingSince = null;
      // After an outage the hands in between were not seen, so the road is read afresh instead of replayed.
      if (this.lostReported) this.road.forget();
      this.lostReported = false;
      this.everConnected = true;
      this.hooks.connected();
    }

    const page = this.page;
    const reading = page.closed ? null : await page.readRoad(this.options.profile);
    // Stopped or retried while the page was being read: that page is no longer ours, and nothing is lost.
    if (!this.active || this.page !== page) return;
    if (page.closed) {
      this.dropPage();
      this.noTable('The connection to Chrome closed');
      return;
    }
    const now = Date.now();
    if (!reading) {
      this.missingSince ??= now;
      if (now - this.missingSince >= (this.options.lostAfterMs ?? 15_000)) {
        this.dropPage();
        this.noTable('The table is no longer on the page');
      }
      return;
    }
    this.missingSince = null;
    if (reading.unreadable) return; // a cell was not recognised: wait for a clean reading rather than guess
    for (const hand of this.road.consume(reading.items)) this.handler(hand);
  }

  /** Looks through the open tabs for one that has the table. */
  private async findTable(): Promise<PageSession | null> {
    let targets;
    try {
      targets = await listPages(this.options.port);
    } catch (e) {
      this.noTable((e as Error).message);
      return null;
    }
    const wanted = this.options.profile.hostContains.toLowerCase();
    for (const target of targets) {
      if (wanted && !target.url.toLowerCase().includes(wanted)) continue;
      let candidate: PageSession | null = null;
      try {
        candidate = await PageSession.attach(target);
        if (await candidate.readRoad(this.options.profile)) return candidate;
      } catch {
        /* a tab that cannot be inspected is skipped */
      }
      candidate?.close();
    }
    this.noTable('Chrome is open but no baccarat table is visible. Open a live table in that window');
    return null;
  }

  private noTable(reason: string): void {
    if (this.everConnected) {
      if (!this.lostReported) {
        this.lostReported = true;
        this.hooks.lost(reason);
      }
    } else {
      this.hooks.waiting(reason);
    }
  }
}
