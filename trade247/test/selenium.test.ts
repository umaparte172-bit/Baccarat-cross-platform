import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import { LiveFeed, type LiveHooks } from '../src/feeds/live.js';
import type { HandEvent } from '../src/feeds/feed.js';
import { listPages } from '../src/live/cdp.js';
import { TableBettor } from '../src/live/bettor.js';
import { parseProfile, type BettingProfile, type LiveProfile } from '../src/live/profile.js';
import { SeleniumBrowser, SeleniumPage, configureSeleniumManager, type SeleniumConnect } from '../src/live/selenium.js';
import { findChrome, openTab, startChrome, until, type TestChrome } from './chrome.js';
import { MOCK_BETTING_PROFILE, startMockBetting, type MockBetting } from './mockBetting.js';

/**
 * The Selenium WebDriver table driver against the local mock table in a real Chrome. It needs a ChromeDriver:
 * T247_TEST_CHROMEDRIVER, the one GitHub runners provide (CHROMEWEBDRIVER), or Electron's ChromeDriver from the dev
 * dependencies (started with --disable-build-check, as its Chromium version can differ from the installed Chrome's).
 */
function findChromeDriver(): string | null {
  const exe = process.platform === 'win32' ? 'chromedriver.exe' : 'chromedriver';
  const candidates = [
    process.env.T247_TEST_CHROMEDRIVER,
    process.env.CHROMEWEBDRIVER ? join(process.env.CHROMEWEBDRIVER, exe) : undefined,
    join(process.cwd(), 'node_modules', 'electron-chromedriver', 'bin', exe),
  ];
  return candidates.find((c): c is string => !!c && existsSync(c)) ?? null;
}

const chrome = findChrome();
const driverPath = findChromeDriver();
const parsed = parseProfile(MOCK_BETTING_PROFILE);
if ('error' in parsed || !parsed.betting) throw new Error('the mock profile must be valid');
const profile: LiveProfile = parsed;
const betting: BettingProfile = parsed.betting;

const connect: SeleniumConnect = async (port) => {
  const { Builder } = await import('selenium-webdriver');
  const chromeModule = (await import('selenium-webdriver/chrome.js')).default;
  const service = new chromeModule.ServiceBuilder(driverPath!).addArguments('--disable-build-check');
  return new Builder()
    .forBrowser('chrome')
    .setChromeOptions(new chromeModule.Options().debuggerAddress(`127.0.0.1:${port}`))
    .setChromeService(service)
    .build();
};

describe('the Selenium driver without a browser', () => {
  it('reports a missing Chrome with the same advice as the DevTools driver, and a ChromeDriver problem with what to do', async () => {
    const log: string[] = [];
    const hooks: LiveHooks = { connected: () => log.push('connected'), waiting: (r) => log.push(r), lost: () => {}, shoeChange: () => {} };
    const feed = new LiveFeed(() => {}, { port: 1, profile, driver: 'selenium', scanMs: 10_000 }, hooks);
    feed.start();
    await until(() => log.length > 0, 5000);
    feed.stop();
    expect(log[0]).toMatch(/Cannot reach Chrome on port 1/);

    const failing = new LiveFeed(() => {}, {
      port: 1, profile, driver: 'selenium', scanMs: 10_000,
      seleniumConnect: async () => { throw new Error('Selenium could not attach ChromeDriver: version mismatch'); },
    }, hooks);
    log.length = 0;
    failing.start();
    await until(() => log.length > 0, 5000);
    failing.stop();
    expect(log[0]).toMatch(/version mismatch/);
  });

  it('uses the selenium-manager shipped beside the app when Selenium cannot find its own', () => {
    const env: NodeJS.ProcessEnv = { T247_ASSETS_DIR: join('X', 'resources', 'app.asar', 'assets') };
    const file = process.platform === 'win32' ? 'selenium-manager.exe' : 'selenium-manager';
    const expected = join('X', 'resources', 'app.asar.unpacked', 'assets', 'selenium-manager', file);
    expect(configureSeleniumManager(env, (p) => p === expected)).toBe(expected);
    expect(env.SE_MANAGER_PATH).toBe(expected);
    expect(configureSeleniumManager({ SE_MANAGER_PATH: 'given' }, () => false)).toBe('given');
    expect(configureSeleniumManager({}, () => false)).toBeNull();
  });
});

describe.skipIf(!chrome || !driverPath)('the Selenium driver on a table in a real Chrome', () => {
  let browser: TestChrome;
  let table: MockBetting;
  let selenium: SeleniumBrowser;
  let page: SeleniumPage;

  beforeAll(async () => {
    browser = await startChrome(chrome!);
    table = await startMockBetting();
    await openTab(browser.port, table.pageUrl);
    await table.waitForUi();
    selenium = await SeleniumBrowser.connect(browser.port, driverPath!, connect);
    const tab = (await selenium.tabs()).find((t) => t.url.startsWith(table.pageUrl));
    if (!tab) throw new Error('the mock table tab was not found through Selenium');
    page = new SeleniumPage(selenium, tab.handle, tab.url);
    // A tab in the background stops painting; the table has to be the one in front, as it is for a user.
    await page.bringToFront();
  }, 60_000);

  afterAll(async () => {
    await selenium?.quit();
    await table?.stop();
    await browser?.stop();
  });

  beforeEach(async () => {
    const s = table.state;
    Object.assign(s, { open: true, status: 'PLACE YOUR BETS', total: 0, selected: null, clicks: [], placedOn: [], mode: 'normal', coverBanker: false, coverPlayer: false });
    await table.waitForUi();
  });

  const bettor = () => new TableBettor(page, betting, { clickGapMs: [60, 60], confirmMs: 2500 });
  const clickLog = () => table.state.clicks.map((c) => (c.value ? `${c.target}:${c.value}` : c.target));

  it('lists the tabs without switching to them (a switch would bring a tab to the front)', async () => {
    await openTab(browser.port, 'about:blank');
    const front = async () => (await listPages(browser.port))[0]!.id;
    const before = await front();
    const tabs = await selenium.tabs();
    expect(tabs.map((t) => t.handle)).toContain(page.handle);
    expect(tabs.find((t) => t.handle === page.handle)!.url).toBe(page.url);
    expect(await front()).toBe(before);
    await page.bringToFront();
    expect(await front()).toBe(page.handle);
  });

  it('reads the road and the betting window in a frame inside a frame from another site', async () => {
    const road = await page.readRoad(profile);
    expect(road).toMatchObject({ container: true, unreadable: false });
    expect(road!.items.join('')).toBe(table.state.cells.join(''));
    const snap = await page.readWindow(betting);
    expect(snap?.reading).toMatchObject({ found: true, open: true, text: 'PLACE YOUR BETS', hasPlayer: true, hasBanker: true, total: 0 });
    expect(snap?.reading.chips.map((c) => c.value)).toEqual([0.1, 1, 5, 25, 100]);
    expect((snap!.frame as { path: number[] }).path.length).toBe(2);
  });

  it('places a Player bet with WebDriver pointer actions: fewest chips, real (trusted) clicks, confirmed by the table', async () => {
    expect(await bettor().place('P', 7)).toEqual({ kind: 'accepted', chips: [5, 1, 1], total: 7 });
    expect(clickLog()).toEqual(['chip:5', 'player', 'chip:1', 'player', 'player']);
    expect(table.state.clicks.every((c) => c.trusted)).toBe(true);
  });

  it('places a Banker bet on the Banker spot', async () => {
    expect(await bettor().place('B', 125)).toMatchObject({ kind: 'accepted', total: 125 });
    expect(table.state.placedOn).toEqual(['banker', 'banker']);
  });

  it('does not touch the table when bets are closed, or when the spot is covered', async () => {
    table.state.open = false;
    table.state.status = 'BETS CLOSED';
    await table.waitForUi();
    expect(await bettor().place('P', 5)).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('not open') });
    expect(table.state.clicks).toHaveLength(0);

    Object.assign(table.state, { open: true, status: 'PLACE YOUR BETS', coverBanker: true });
    await table.waitForUi();
    expect(await bettor().place('B', 5)).toMatchObject({ kind: 'rejected', reason: expect.stringContaining('covered') });
    expect(table.state.placedOn).toEqual([]);
  });

  it('says "unconfirmed" and does not retry when the table never shows the bet', async () => {
    table.state.mode = 'noconfirm';
    expect(await bettor().place('P', 5)).toMatchObject({ kind: 'unconfirmed' });
    expect(table.state.placedOn).toEqual(['player']);
  });

  it('runs the live feed on Selenium: history is not played, new hands are reported once, in order', async () => {
    const hands: HandEvent[] = [];
    const log: string[] = [];
    const feed = new LiveFeed((e) => hands.push(e), {
      port: browser.port, profile, driver: 'selenium', chromedriverPath: driverPath!, seleniumConnect: connect, pollMs: 150, scanMs: 300,
    }, { connected: () => log.push('connected'), waiting: (r) => log.push(`waiting:${r}`), lost: (r) => log.push(`lost:${r}`), shoeChange: () => log.push('shoe') });
    feed.start();
    try {
      await until(() => log.includes('connected'), 20_000);
      await new Promise((r) => setTimeout(r, 400));
      expect(hands).toHaveLength(0);
      table.state.cells.push('B', 'P', 'T');
      await until(() => hands.length >= 3, 10_000);
      expect(hands.map((h) => h.result).join('')).toBe('BPT');
    } finally {
      feed.stop();
    }
    // Ending the WebDriver session leaves the user's Chrome open.
    await new Promise((r) => setTimeout(r, 300));
    expect((await listPages(browser.port)).some((p) => p.url.startsWith(table.pageUrl))).toBe(true);
  }, 40_000);
});
