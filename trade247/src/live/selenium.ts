import { existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import type { WebDriver, WebElement } from 'selenium-webdriver';
import { listPages } from './cdp.js';
import { pickRoad, pickWindow, providersOf, type RoadReading, type TablePage, type WindowSnapshot } from './page.js';
import { probeScript, type BettingProfile, type LiveProfile } from './profile.js';
import { CHECK_TARGET, windowScript, type TargetCheck, type WindowReading } from './table.js';

/**
 * The live table driven through Selenium WebDriver (ChromeDriver) instead of the built-in DevTools connection.
 *
 * Selenium attaches to the Chrome window the user opened with "Open Chrome for the table" (its own profile, where the
 * user signed in by hand), so the workflow is the same: the program never types credentials and never opens a site.
 * Results are read with executeScript in every frame of the tab (WebDriver frame switching, any depth, any site), and
 * bets are placed with WebDriver Actions: a pointer move to the element, a press and a release.
 *
 * One WebDriver session has one "current window and frame", so every operation here runs under one lock.
 */

const FRAMES = 'iframe,frame';
const MAX_DEPTH = 6;
const MAX_FRAMES = 40;

/** A frame inside a tab: the index of the <iframe> at each level, and the site it showed when it was read. */
export interface SeleniumFrame {
  path: number[];
  origin: string;
}

export type SeleniumConnect = (port: number, chromedriverPath: string) => Promise<WebDriver>;

class Lock {
  private tail: Promise<unknown> = Promise.resolve();
  run<T>(fn: () => Promise<T>): Promise<T> {
    const next = this.tail.then(fn, fn);
    this.tail = next.catch(() => {});
    return next;
  }
}

/** Points Selenium at the selenium-manager program shipped with the app (it cannot run from inside a bundle). */
export function configureSeleniumManager(env: NodeJS.ProcessEnv = process.env, exists: (p: string) => boolean = existsSync): string | null {
  if (env.SE_MANAGER_PATH) return env.SE_MANAGER_PATH;
  const file = process.platform === 'win32' ? 'selenium-manager.exe' : 'selenium-manager';
  const candidates = [
    env.T247_ASSETS_DIR ? join(env.T247_ASSETS_DIR, 'selenium-manager', file).replace(/app\.asar([\\/])/, 'app.asar.unpacked$1') : '',
    join(dirname(process.execPath), file),
  ].filter(Boolean);
  const found = candidates.find((c) => exists(c)) ?? null;
  if (found) env.SE_MANAGER_PATH = found;
  return found;
}

/** Starts ChromeDriver and attaches it to the Chrome listening on the debugging port. */
export const connectSelenium: SeleniumConnect = async (port, chromedriverPath) => {
  // Same check and same advice as the DevTools driver when that Chrome is not running.
  await listPages(port);
  if (chromedriverPath && !existsSync(chromedriverPath)) throw new Error(`ChromeDriver was not found at ${chromedriverPath}`);
  configureSeleniumManager();
  const { Builder } = await import('selenium-webdriver');
  const chrome = (await import('selenium-webdriver/chrome.js')).default;
  const options = new chrome.Options().debuggerAddress(`127.0.0.1:${port}`);
  let builder = new Builder().forBrowser('chrome').setChromeOptions(options);
  if (chromedriverPath) builder = builder.setChromeService(new chrome.ServiceBuilder(chromedriverPath));
  try {
    return await builder.build();
  } catch (e) {
    const why = String((e as Error).message ?? e).split('\n')[0]!.slice(0, 300);
    throw new Error(
      `Selenium could not attach ChromeDriver to the Chrome on port ${port}: ${why}. ` +
        'Set "ChromeDriver path" to a chromedriver that matches your Chrome version, or choose the DevTools driver',
    );
  }
};

/** One Selenium session attached to the table browser; shared by the tabs read through it. */
export class SeleniumBrowser {
  readonly lock = new Lock();
  closed = false;

  constructor(
    readonly driver: WebDriver,
    /** Chrome's debugging port: lists tabs without switching to them. */
    readonly port = 0,
  ) {}

  static async connect(port: number, chromedriverPath: string, connect: SeleniumConnect = connectSelenium): Promise<SeleniumBrowser> {
    await loadSelenium();
    return new SeleniumBrowser(await connect(port, chromedriverPath), port);
  }

  /**
   * The open tabs and their addresses, most recently used first. Switching to a tab in WebDriver brings it to the
   * front, so the addresses come from Chrome's tab list (ChromeDriver's window handles are those tabs' ids) and no
   * tab is switched to just to be listed.
   */
  tabs(): Promise<Array<{ handle: string; url: string }>> {
    return this.lock.run(async () => {
      let handles: string[];
      try {
        handles = await this.driver.getAllWindowHandles();
      } catch (e) {
        this.closed = true;
        throw new Error(`Selenium lost the browser: ${String((e as Error).message).split('\n')[0]}`);
      }
      const listed = this.port ? await listPages(this.port).catch(() => []) : [];
      const known = new Set(handles);
      const out = listed.filter((t) => known.has(t.id)).map((t) => ({ handle: t.id, url: t.url }));
      // A handle Chrome's list does not show (should not happen) is still offered, last, with its address unknown.
      for (const handle of handles) if (!out.some((t) => t.handle === handle)) out.push({ handle, url: '' });
      return out.filter((t) => !t.url.startsWith('devtools://') && !t.url.startsWith('chrome-extension://'));
    });
  }

  /** Brings a tab back to the front (after a search that switched through tabs and found nothing). */
  restore(handle: string): Promise<void> {
    return this.lock.run(async () => {
      await this.driver.switchTo().window(handle).catch(() => {});
    });
  }

  /** Ends the WebDriver session. The Chrome window it attached to stays open (ChromeDriver did not start it). */
  async quit(): Promise<void> {
    if (this.closed) return;
    this.closed = true;
    try {
      await this.driver.quit();
    } catch {
      /* already gone */
    }
  }
}

export class SeleniumPage implements TablePage {
  private gone = false;
  private origins: string[] = [];

  constructor(
    private readonly browser: SeleniumBrowser,
    readonly handle: string,
    readonly url: string,
  ) {}

  get closed(): boolean {
    return this.gone || this.browser.closed;
  }

  close(): void {
    this.gone = true;
  }

  providers(): string[] {
    return providersOf(this.origins);
  }

  private get driver(): WebDriver {
    return this.browser.driver;
  }

  /** Makes this tab current, at its top frame. A tab that has gone closes the page. */
  private async top(): Promise<void> {
    try {
      await this.driver.switchTo().window(this.handle);
      await this.driver.switchTo().defaultContent();
    } catch (e) {
      this.gone = true;
      throw new Error(`The table tab is gone: ${String((e as Error).message).split('\n')[0]}`);
    }
  }

  /** Runs a script in every frame of the tab (depth first) and collects the answers. */
  evaluateInFrames<T>(expression: string): Promise<Array<{ frame: SeleniumFrame; value: T }>> {
    return this.browser.lock.run(async () => {
      if (this.closed) throw new Error('The table tab is closed');
      await this.top();
      const out: Array<{ frame: SeleniumFrame; value: T }> = [];
      const seen = { frames: 0 };
      await this.walk(expression, [], out, seen);
      this.origins = out.map((o) => o.frame.origin);
      return out;
    });
  }

  private async walk<T>(expression: string, path: number[], out: Array<{ frame: SeleniumFrame; value: T }>, seen: { frames: number }): Promise<void> {
    seen.frames++;
    try {
      const r = await this.driver.executeScript<[string, T] | null>(`return [location.origin, (${expression})];`);
      if (r && r[1] !== null && r[1] !== undefined) out.push({ frame: { path: [...path], origin: String(r[0] ?? '') }, value: r[1] });
    } catch {
      /* a frame that is navigating or refuses scripts is skipped */
    }
    if (path.length >= MAX_DEPTH) return;
    let count = 0;
    try {
      count = (await this.driver.findElements(FRAMES_BY())).length;
    } catch {
      return;
    }
    for (let i = 0; i < count && seen.frames < MAX_FRAMES; i++) {
      let entered = false;
      try {
        const children = await this.driver.findElements(FRAMES_BY());
        if (!children[i]) break;
        await this.driver.switchTo().frame(children[i]!);
        entered = true;
        await this.walk(expression, [...path, i], out, seen);
      } catch {
        /* the frame went away while it was being read */
      } finally {
        if (entered) await this.driver.switchTo().parentFrame().catch(() => {});
      }
    }
  }

  async readRoad(profile: LiveProfile): Promise<RoadReading | null> {
    const found = await this.evaluateInFrames<{ container: boolean; items: string[] }>(probeScript(profile));
    return pickRoad(found.map(({ frame, value }) => ({ origin: frame.origin, value })));
  }

  async readWindow(b: BettingProfile): Promise<WindowSnapshot | null> {
    return pickWindow(await this.evaluateInFrames<WindowReading>(windowScript(b)));
  }

  bringToFront(): Promise<void> {
    return this.browser.lock.run(async () => {
      await this.top();
      await this.driver.executeScript('window.focus();').catch(() => {});
    });
  }

  /**
   * Switches into the frame the window was read from (checking it still shows the same site), finds the element, checks
   * that a click would reach it, then clicks it with WebDriver Actions (pointer move, press, release).
   */
  clickElement(target: object, selector: string, index = 0): Promise<void> {
    const frame = target as SeleniumFrame;
    return this.browser.lock.run(async () => {
      await this.top();
      for (const i of frame.path) {
        const children = await this.driver.findElements(FRAMES_BY());
        if (!children[i]) throw new Error('The frame that holds the table has changed');
        await this.driver.switchTo().frame(children[i]!);
      }
      const origin = await this.driver.executeScript<string>('return location.origin;');
      if (origin !== frame.origin) throw new Error('The frame that holds the table has changed');

      const elements = await this.driver.findElements(css(selector));
      const element: WebElement | undefined = elements[Math.max(0, Math.floor(index))];
      if (!element) throw new Error(`Cannot find "${selector}" on the table`);
      const check = await this.driver.executeScript<TargetCheck>(`return (${CHECK_TARGET}).call(arguments[0]);`, element);
      if (!check?.visible) throw new Error(`"${selector}" is not visible`);
      if (check.disabled) throw new Error(`"${selector}" is disabled`);
      if (check.covered) throw new Error(`"${selector}" is covered by something else on the page`);
      await this.driver.actions({ async: true }).move({ origin: element }).press().release().perform();
    });
  }
}

// By is loaded lazily with the rest of Selenium, so the DevTools driver never loads it.
let ByClass: typeof import('selenium-webdriver').By | null = null;
export async function loadSelenium(): Promise<void> {
  ByClass ??= (await import('selenium-webdriver')).By;
}
const css = (selector: string): import('selenium-webdriver').By => {
  if (!ByClass) throw new Error('Selenium is not loaded');
  return ByClass.css(selector);
};
const FRAMES_BY = (): import('selenium-webdriver').By => css(FRAMES);
