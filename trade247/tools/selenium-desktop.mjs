// End-to-end tests of the local Electron UI using standard Selenium WebDriver.
// No external websites or live feeds are opened by this test.
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { Builder, By, until } from 'selenium-webdriver';
import chrome from 'selenium-webdriver/chrome.js';

const require = createRequire(import.meta.url);
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const packaged = process.argv.indexOf('--app');
if (packaged >= 0 && !process.argv[packaged + 1]) throw new Error('--app requires an executable path');
const binary = packaged >= 0 ? resolve(process.argv[packaged + 1]) : require('electron');
const driverPath = join(dirname(require.resolve('electron-chromedriver/package.json')), 'bin', process.platform === 'win32' ? 'chromedriver.exe' : 'chromedriver');
if (!existsSync(binary) || !existsSync(driverPath)) throw new Error('Electron or its matching WebDriver is missing. Run npm ci first.');
const dataDir = mkdtempSync(join(tmpdir(), 't247-selenium-'));
const env = { ...process.env, T247_DATA_DIR: dataDir, T247_OPEN_REPORTS: '0', T247_SELENIUM_TEST: '1' };
delete env.ELECTRON_RUN_AS_NODE;
const options = new chrome.Options().setChromeBinaryPath(binary);
if (packaged < 0) options.addArguments(`app=${join(root, 'build/electron')}`);
const service = new chrome.ServiceBuilder(driverPath).setEnvironment(env).build();
let driver;
let passed = 0;
const pass = (name) => { passed++; console.log(`PASS  ${name}`); };

try {
  // Explicit service and options prevent Selenium environment overrides from opening another browser/server.
  driver = await new Builder().disableEnvironmentOverrides().forBrowser('chrome')
    .setChromeOptions(options).usingServer(await service.start()).build();
  await driver.manage().setTimeouts({ implicit: 0, pageLoad: 30_000, script: 10_000 });
  const el = (selector) => driver.findElement(By.css(selector));
  const click = async (selector) => (await el(selector)).click();
  const text = async (selector) => (await el(selector)).getText();
  const wait = (condition, message, timeout = 15_000) => driver.wait(condition, timeout, message, 150);
  const fill = async (selector, value) => {
    const input = await el(selector);
    await input.clear();
    await input.sendKeys(value);
  };
  const select = async (selector, value) => {
    const changed = await driver.executeScript(`
      const el = document.querySelector(arguments[0]);
      if (!el) return false;
      el.value = arguments[1];
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return el.value === arguments[1];
    `, selector, value);
    assert.equal(changed, true, `Could not select ${value} in ${selector}`);
  };

  await driver.wait(until.elementLocated(By.id('start')), 30_000);
  await wait(async () => (await text('#miniStats')).includes('Hands 0'), 'Initial settings did not load');
  const address = new URL(await driver.getCurrentUrl());
  assert.equal(address.hostname, '127.0.0.1');
  assert.match(await driver.getTitle(), /TRADE247/);
  pass('Electron opens the local dashboard');
  assert.equal(await driver.executeScript('return typeof require === "undefined" && typeof process === "undefined"'), true);
  pass('The renderer has no Node.js access');

  await select('#feed', 'manual');
  await fill('#base_bet', '1');
  await click('#start');
  await wait(async () => (await el('#start')).getAttribute('disabled'), 'Session did not start');
  pass('START begins a manual practice session through the UI');
  for (const [index, result] of ['P', 'B', 'T'].entries()) {
    await click(`#manual button[data-r="${result}"]`);
    await wait(async () => (await text('#miniStats')).includes(`Hands ${index + 1} ·`), `Result ${result} was not recorded`);
  }
  assert.equal((await driver.findElements(By.css('#rows tr'))).length, 3);
  pass('Player, Banker and Tie controls update the visible history');
  await click('#pause');
  await wait(async () => (await text('#pause')) === 'RESUME', 'Session did not pause');
  await click('#pause');
  await wait(async () => (await text('#pause')) === 'PAUSE', 'Session did not resume');
  pass('PAUSE and RESUME work');
  await click('#stop');
  await wait(async () => (await text('#status')).startsWith('STOPPED'), 'Session did not stop');
  assert.equal(await (await el('#start')).isEnabled(), true);
  pass('STOP ends the practice session and enables START');

  await select('#feed', 'csv');
  await fill('[data-key="csv_text"]', 'PBBPTP');
  await click('#start');
  await wait(async () => (await text('#status')).startsWith('STOPPED') && (await text('#miniStats')).includes('Hands 6 ·'), 'Replay did not finish all six hands');
  assert.equal((await driver.findElements(By.css('#rows tr'))).length, 6);
  pass('Replay entered through the UI finishes and replaces session history');
  await click('#btBtn');
  await wait(async () => (await el('#bt')).isDisplayed(), 'Backtest panel did not open');
  await driver.executeScript(`
    document.querySelector('input[name="progMode"][value="auto"]').checked = true;
    const base = document.querySelector('#base_bet');
    base.value = '1';
    base.dispatchEvent(new Event('input', { bubbles: true }));
  `);
  assert.equal(await el('#base_bet').then(x => x.getAttribute('value')), '1');
  await fill('#bt_shoes', '2');
  await fill('#bt_trials', '2');
  await click('#sweepBox summary');
  await click('#btCompare');
  await wait(async () => {
    const message = await text('#btMsg');
    if (message.startsWith('Error:')) throw new Error(message);
    return message.startsWith('Finished');
  }, 'Compare Settings did not finish', 60_000);
  assert.equal((await driver.findElements(By.css('#btOut tbody tr'))).length, 1);
  pass('Compare Settings completes a same-seed parameter sweep');
  await click('#btClose');
  await wait(async () => !(await (await el('#bt')).isDisplayed()), 'Backtest panel did not close');
  pass('Backtest panel opens and closes');

  await select('#strategy', 'AME');
  await select('#feed', 'manual');
  await click('#start');
  await wait(async () => (await text('#nextSub')).toLowerCase().includes('warming up'), 'AME did not enter its documented warm-up');
  for (const result of ['B', 'B', 'B', 'B', 'B', 'B']) await click(`#manual button[data-r="${result}"]`);
  await wait(async () => (await text('#miniStats')).includes('Hands 6 ·'), 'AME did not process six manual hands');
  await click('#stop');
  await wait(async () => await (await el('#start')).isEnabled(), 'AME session did not finish stopping');
  pass('AME-AI starts, warms up and processes table results');

  await select('#strategy', 'JJ');
  await click('#start');
  await wait(async () => !(await (await el('#start')).isEnabled()), 'JJ session did not start');
  await wait(async () => (await text('#miniStats')).includes('JJ: cycle'), 'JJ cycle state was not shown');
  await click('#manual button[data-r="P"]');
  await wait(async () => (await text('#miniStats')).includes('Hands 1 ·'), 'JJ did not process a manual hand');
  await click('#stop');
  await wait(async () => await (await el('#start')).isEnabled(), 'JJ session did not finish stopping');
  pass('JJ cycle strategy starts and exposes its cycle state');

  await select('#strategy', 'IHG');
  await driver.executeScript("document.querySelector('#adv').open = true");
  assert.equal(await el('[data-key="single_wins_action"]').then((x) => x.isDisplayed()), true);
  assert.equal(await el('[data-key="single_wins_virtual"]').then((x) => x.isDisplayed()), true);
  assert.equal(await el('[data-key="banker_cover"]').then((x) => x.isDisplayed()), true);
  pass('IHG advanced controls are visible in the Electron UI');
  console.log(`${passed}/${passed} Selenium desktop checks passed`);
} catch (error) {
  console.error('FAIL', error instanceof Error ? error.stack : error);
  if (driver) {
    try { console.error(`Window: ${await driver.getTitle()} (${await driver.getCurrentUrl()})`); }
    catch { /* The app may have exited. */ }
  }
  process.exitCode = 1;
} finally {
  if (driver) {
    // Leave a stopped session so Electron's normal close confirmation cannot strand the test process.
    try {
      const stop = await driver.findElement(By.id('stop'));
      if (await stop.isEnabled()) {
        await stop.click();
        await driver.wait(async () => !(await stop.isEnabled()), 5000);
      }
    } catch { /* The window may already be gone after a failed test. */ }
    await driver.quit().catch(() => {});
  }
  await service.kill().catch(() => {});
  // Only this test's newly created folder is removed; existing user settings are never touched.
  try { rmSync(dataDir, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); }
  catch { console.warn(`Test data remains at ${dataDir}`); }
}
