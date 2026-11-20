// End-to-end check of a RUNNING TRADE247 (the packaged app opened normally, or the app from source):
//
//   npx tsx tools/app-check.ts [--port 8247] [--data-dir <dir>] [--chrome-port 9014] [--driver selenium|cdp]
//
// 1. the server answers and serves the page; 2. a replay session plays every hand and the Excel report downloads;
// 3. the Live table feed: the app opens its own Chrome ("Open Chrome for the table"), a local mock baccarat table is
//    opened in it, and a session on that Chrome (Selenium WebDriver by default) reads the results the table deals.
// It uses only the app's own HTTP API, the same one the page uses. Nothing outside this computer is contacted except
// what Selenium Manager downloads (a ChromeDriver matching the installed Chrome). Results are printed as PASS/FAIL;
// on GitHub Actions they are also written as annotations.

import { mkdirSync, writeFileSync, existsSync, readFileSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { defaultDataDir } from '../src/store/paths.js';
import { openTab } from '../test/chrome.js';
import { MOCK_BETTING_PROFILE, startMockBetting } from '../test/mockBetting.js';

const arg = (name: string, fallback: string): string => {
  const i = process.argv.indexOf(`--${name}`);
  return i >= 0 && process.argv[i + 1] ? process.argv[i + 1]! : fallback;
};
const port = Number(arg('port', '8247'));
const dataDir = arg('data-dir', defaultDataDir());
const chromePort = Number(arg('chrome-port', '9014'));
const driver = arg('driver', 'selenium');
const origin = `http://127.0.0.1:${port}`;
const gha = process.env.GITHUB_ACTIONS === 'true';

const results: Array<{ name: string; ok: boolean; detail: string }> = [];
function report(name: string, ok: boolean, detail = ''): void {
  results.push({ name, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? `  (${detail})` : ''}`);
  if (gha) console.log(`::${ok ? 'notice' : 'error'} title=App check ${ok ? 'PASS' : 'FAIL'}::${name}${detail ? ` - ${detail.replace(/\r?\n/g, ' ')}` : ''}`);
}

const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));
async function api(path: string, payload?: unknown): Promise<any> {
  const r = await fetch(origin + path, {
    method: payload === undefined ? 'GET' : 'POST',
    headers: { 'x-t247': '1', 'content-type': 'application/json' },
    body: payload === undefined ? undefined : JSON.stringify(payload),
    signal: AbortSignal.timeout(15_000),
  });
  const text = await r.text();
  try {
    return JSON.parse(text);
  } catch {
    return { status: r.status, text };
  }
}
async function until<T>(what: () => Promise<T>, ok: (v: T) => boolean, ms: number, step = 500): Promise<T> {
  const end = Date.now() + ms;
  let last = await what();
  while (!ok(last) && Date.now() < end) {
    await sleep(step);
    last = await what();
  }
  return last;
}

async function main(): Promise<void> {
  // ---------------------------------------------------------------- 1. server and page
  const up = await until(
    () => fetch(`${origin}/`, { signal: AbortSignal.timeout(3000) }).then((r) => r.text()).catch(() => ''),
    (t) => t.includes('TRADE247'),
    60_000,
    1000,
  );
  report('the server answers at ' + origin + ' and serves the TRADE247 page', up.includes('TRADE247'));
  if (!up.includes('TRADE247')) return;
  const state0 = await api('/api/state');
  report('the API reports its state', typeof state0.status === 'string', `status ${state0.status}`);
  if (state0.running) await api('/api/stop', {});

  // ---------------------------------------------------------------- 2. replay + report
  const letters = 'PBPBBPPBTPBBPPBP';
  const started = await api('/api/start', { settings: { feed: 'csv', csv_text: letters, strategy: 'IHG', unit_value: 1, min_chip: 0, start_balance: 1000, sim_interval: 0 } });
  report('a replay session starts (IHG)', started.ok === true, started.status);
  const done = await until(() => api('/api/state?since=0'), (s) => s.running === false, 30_000, 200);
  report('the replay plays every hand and stops by itself', done.hands === letters.length && String(done.status).startsWith('STOPPED'), `${done.hands} hands, ${done.status}`);
  const rows = (done.rows ?? []) as Array<{ result: string }>;
  report('the history shows the dealt results in order', rows.map((r) => r.result).join('') === letters, rows.map((r) => r.result).join(''));
  const xlsx = await fetch(`${origin}/api/report?fmt=xlsx`, { headers: { 'x-t247': '1' } });
  const xlsxBytes = Buffer.from(await xlsx.arrayBuffer());
  report('the Excel report downloads (a real .xlsx file)', xlsx.ok && xlsxBytes.subarray(0, 2).toString() === 'PK', `${xlsxBytes.length} bytes`);
  const csv = await fetch(`${origin}/api/report?fmt=csv`, { headers: { 'x-t247': '1' } });
  report('the CSV report downloads', csv.ok && (await csv.text()).length > 50);

  // ---------------------------------------------------------------- 3. live table through the app's own Chrome
  const table = await startMockBetting();
  const profilePath = join(dataDir, 'live-profile.json');
  const hadProfile = existsSync(profilePath);
  const previous = hadProfile ? readFileSync(profilePath) : null;
  try {
    mkdirSync(dataDir, { recursive: true });
    writeFileSync(profilePath, JSON.stringify(MOCK_BETTING_PROFILE, null, 2));
    const opened = await api('/api/live/open_browser', { port: chromePort });
    report('"Open Chrome for the table" opens Chrome with its own profile and debugging port', opened.ok === true, opened.error ?? (opened.already ? 'already open' : 'started'));
    if (!opened.ok) return;
    await openTab(chromePort, table.pageUrl);
    await table.waitForUi();

    const live = await api('/api/start', {
      settings: { feed: 'live', live_port: chromePort, live_driver: driver, chromedriver_path: '', bet_mode: 'paper', strategy: 'IHG', unit_value: 1, min_chip: 0, start_balance: 1000 },
    });
    report(`a Live table session starts (driver: ${driver})`, live.ok === true, live.status);
    // The first Selenium start downloads a ChromeDriver that matches this Chrome: allow time for it.
    const connected = await until(() => api('/api/state'), (s) => s.running && s.status === 'RUNNING', 180_000, 1000);
    report('the app finds the table in Chrome and connects', connected.status === 'RUNNING', `${connected.status}${connected.error ? ' / ' + connected.error : ''}`);
    if (connected.status === 'RUNNING') {
      await sleep(1500);
      const before = (await api('/api/state')).hands;
      report('the road already on the table is history and is not played', before === 0, `${before} hands`);
      table.state.cells.push('B', 'P', 'P', 'T');
      const after = await until(() => api('/api/state?since=0'), (s) => s.hands >= 4, 30_000, 300);
      const seen = ((after.rows ?? []) as Array<{ result: string }>).map((r) => r.result).join('');
      report('every hand the table deals is read once, in order', seen === 'BPPT', seen || 'nothing');
      report('the strategy proposes the next bet (paper mode: nothing is clicked)', !!after.next && table.state.clicks.length === 0, JSON.stringify(after.next ?? null).slice(0, 120));
    }
    await api('/api/stop', {});
    const stopped = await until(() => api('/api/state'), (s) => !s.running, 10_000, 300);
    report('STOP ends the live session', stopped.running === false, stopped.status);
  } finally {
    if (previous) writeFileSync(profilePath, previous);
    else rmSync(profilePath, { force: true });
    await table.stop();
  }
}

main()
  .catch((e) => report('the check ran to the end', false, (e as Error).stack ?? String(e)))
  .finally(() => {
    const failed = results.filter((r) => !r.ok).length;
    console.log(failed === 0 ? `${results.length}/${results.length} app checks passed` : `${failed} of ${results.length} app checks failed`);
    if (gha) console.log(`::${failed === 0 ? 'notice' : 'error'} title=App check summary::${results.length - failed}/${results.length} passed`);
    process.exit(failed === 0 ? 0 : 1);
  });
