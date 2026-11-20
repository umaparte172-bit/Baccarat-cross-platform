import type { Side } from '../engine/types.js';
import { listPages } from '../live/cdp.js';
import { TableBettor, type Placement } from '../live/bettor.js';
import { PageSession, type TablePage } from '../live/page.js';
import { SeleniumBrowser, SeleniumPage, type SeleniumConnect } from '../live/selenium.js';
import type { LiveProfile } from '../live/profile.js';
import { RoadTracker } from '../live/road.js';
import type { Feed, HandHandler } from './feed.js';

export interface LiveOptions {
  /** Chrome's remote debugging port. */
  port: number;
  /** The table profile (the one the session is bet with). */
  profile: LiveProfile;
  /** Profiles tried on the open tabs, in order (default: just `profile`). The one that finds a road is then used. */
  profiles?: LiveProfile[];
  /** How often the road is read while a table is attached. */
  pollMs?: number;
  /** How often tabs are searched for a table while none is attached. */
  scanMs?: number;
  /** The table counts as lost when its road cannot be found for this long. */
  lostAfterMs?: number;
  /** Watch the betting window and report when it opens (needs a profile with a "betting" block). */
  watchWindow?: boolean;
  /** The road must have been still for this long before a new window is reported (the last result is in). */
  settleMs?: number;
  /** Pause between chip clicks, in milliseconds [min, max]. */
  clickGapMs?: [number, number];
  /** How long the table has to show a placed bet. */
  confirmMs?: number;
  /** How the browser is driven: the built-in DevTools connection (default) or Selenium WebDriver. */
  driver?: 'cdp' | 'selenium';
  /** Selenium: the ChromeDriver to use ('' = found automatically). */
  chromedriverPath?: string;
  /** Test hook: how a Selenium session is started. */
  seleniumConnect?: SeleniumConnect;
}

export interface LiveHooks {
  /** A table is attached (first time, or again after an outage). */
  connected(): void;
  /** Still looking for a table; `reason` tells the user what to do. */
  waiting(reason: string): void;
  /** A table that was attached has gone. Called once per outage. */
  lost(reason: string): void;
  /** The table started a new shoe (reported before the first hand of that shoe). */
  shoeChange(): void;
  /** Bets are open for a new round (once per round, after the previous result is in). */
  windowOpen?(): void;
}

/**
 * Reads results from the user's own Chrome (read-only): the road of the table they have open. Nothing is clicked or
 * typed in the page. The road that is already there when the session starts is history and is not played; every hand
 * added afterwards is a result. A shorter road is a new shoe.
 */
export class LiveFeed implements Feed {
  private active = false;
  private timer: NodeJS.Timeout | null = null;
  private page: TablePage | null = null;
  private selenium: SeleniumBrowser | null = null;
  private readonly road = new RoadTracker();
  private missingSince: number | null = null;
  private everConnected = false;
  private lostReported = false;
  private running = false;
  private wakeRequested = false;
  private wake: (() => void) | null = null;
  /** Hands and shoe changes seen so far: identifies the round a window belongs to. */
  private round = 0;
  private armedRound = -1;
  private lastChangeAt = 0;
  /** The profile that found the table on the page now attached. */
  private profile: LiveProfile;

  constructor(
    private readonly handler: HandHandler,
    private readonly options: LiveOptions,
    private readonly hooks: LiveHooks,
  ) {
    this.profile = options.profile;
  }

  start(): void {
    this.active = true;
    void this.loop();
  }

  stop(): void {
    this.active = false;
    this.wakeUp();
    this.dropPage();
    void this.selenium?.quit();
    this.selenium = null;
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

  /**
   * Places a bet on the connected table. The result says whether the table shows it; nothing is retried.
   * `shouldAbort` is checked between clicks.
   */
  async place(side: Side, stake: number, shouldAbort?: () => boolean): Promise<Placement> {
    const betting = this.profile.betting;
    const page = this.page;
    if (!betting) return { kind: 'rejected', reason: 'This table profile has no betting settings' };
    if (!page || page.closed) return { kind: 'rejected', reason: 'The table is not connected' };
    return new TableBettor(page, betting, { shouldAbort, clickGapMs: this.options.clickGapMs, confirmMs: this.options.confirmMs }).place(side, stake);
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
    const reading = page.closed ? null : await page.readRoad(this.profile);
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
    for (const event of this.road.consume(reading.items)) {
      this.round++;
      this.lastChangeAt = now;
      if (event.kind === 'shoe') this.hooks.shoeChange();
      else this.handler({ result: event.result, cards: null });
    }
    await this.watchWindow(page, now);
  }

  /** Tells the session when bets open for a round it has not been told about yet. */
  private async watchWindow(page: TablePage, now: number): Promise<void> {
    const betting = this.profile.betting;
    if (!this.options.watchWindow || !betting || !this.hooks.windowOpen || this.armedRound === this.round) return;
    if (now - this.lastChangeAt < (this.options.settleMs ?? 800)) return;
    const snap = await page.readWindow(betting).catch(() => null);
    if (!this.active || this.page !== page || !snap?.reading.open) return;
    this.armedRound = this.round;
    this.hooks.windowOpen();
  }

  /** Looks through the open tabs for one that has the table. */
  private async findTable(): Promise<TablePage | null> {
    if (this.options.driver === 'selenium') return this.findTableSelenium();
    let targets;
    try {
      targets = await listPages(this.options.port);
    } catch (e) {
      this.noTable((e as Error).message);
      return null;
    }
    const candidates = this.options.profiles ?? [this.options.profile];
    const checked: string[] = [];
    const providers = new Set<string>();
    for (const target of targets) {
      if (!candidates.some((p) => !p.hostContains || target.url.toLowerCase().includes(p.hostContains.toLowerCase()))) continue;
      let candidate: PageSession | null = null;
      try {
        candidate = await PageSession.attach(target);
        for (const profile of this.options.profiles ?? [this.options.profile]) {
          if (await candidate.readRoad(profile)) {
            this.profile = profile;
            return candidate;
          }
        }
      } catch {
        /* a tab that cannot be inspected is skipped */
      }
      if (candidate) {
        checked.push(siteOf(target.url));
        for (const p of candidate.providers()) providers.add(p);
      }
      candidate?.close();
    }
    this.noTable(noTableReason(this.options.port, checked, [...providers]));
    return null;
  }

  /** The same search through a Selenium session attached to the table browser. */
  private async findTableSelenium(): Promise<TablePage | null> {
    if (this.selenium?.closed) this.selenium = null;
    if (!this.selenium) {
      try {
        this.selenium = await SeleniumBrowser.connect(this.options.port, this.options.chromedriverPath ?? '', this.options.seleniumConnect);
      } catch (e) {
        this.noTable((e as Error).message);
        return null;
      }
      if (!this.active) {
        void this.selenium.quit();
        this.selenium = null;
        return null;
      }
    }
    const browser = this.selenium;
    let tabs;
    try {
      tabs = await browser.tabs();
    } catch (e) {
      void browser.quit();
      this.selenium = null;
      this.noTable((e as Error).message);
      return null;
    }
    const candidates = this.options.profiles ?? [this.options.profile];
    const checked: string[] = [];
    const providers = new Set<string>();
    for (const tab of tabs) {
      if (!candidates.some((p) => !p.hostContains || tab.url.toLowerCase().includes(p.hostContains.toLowerCase()))) continue;
      const page = new SeleniumPage(browser, tab.handle, tab.url);
      try {
        for (const profile of candidates) {
          if (await page.readRoad(profile)) {
            this.profile = profile;
            return page;
          }
        }
      } catch {
        /* a tab that cannot be read is skipped */
      }
      checked.push(siteOf(tab.url));
      for (const p of page.providers()) providers.add(p);
      page.close();
    }
    this.noTable(noTableReason(this.options.port, checked, [...providers]));
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

const siteOf = (url: string): string => {
  try {
    return new URL(url).host;
  } catch {
    return 'a tab';
  }
};

/** Says what was looked at and what to do, so the user is not left guessing why nothing connects. */
export function noTableReason(port: number, sites: string[], providers: string[]): string {
  const where = sites.length
    ? `Looked at ${sites.length} tab(s) in the Chrome on port ${port}: ${[...new Set(sites)].join(', ')}.`
    : `The Chrome on port ${port} has no page open.`;
  if (providers.length) {
    return `${where} A ${providers.join(' / ')} table is open there, but its road was not recognised: the casino may have changed its page. Run trade247 --inspect-table and send the file it saves in the data folder.`;
  }
  return `${where} No baccarat table is open in it. Open the table in THAT window (a table opened in your normal Chrome cannot be read) and wait until the road next to the video shows.`;
}
