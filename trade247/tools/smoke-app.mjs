// Launches the packaged program and checks that it really works, end to end:
//   node tools/smoke-app.mjs [path-to-executable]
//
// It covers what only the packaged build can get wrong: the embedded page, the embedded worker thread (the
// backtest), a Replay session, the Excel report, the refusal to start twice, and a clean exit code.

import { spawn } from 'node:child_process';
import { request } from 'node:http';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { createServer as createHttpServer } from 'node:http';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const os = { win32: 'win', darwin: 'macos', linux: 'linux' }[process.platform] ?? process.platform;
const defaultExe = join(root, 'release', `trade247-${os}-${process.arch}${process.platform === 'win32' ? '.exe' : ''}`);
const exe = resolve(process.argv[2] ?? defaultExe);

const results = [];
const check = (name, ok, detail = '') => {
  results.push({ name, ok });
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok || !detail ? '' : `\n        ${detail}`}`);
  return ok;
};
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** fetch() will not let a program change the Host header, so a request with a foreign Host is sent by hand. */
const statusWithHost = (port, host) =>
  new Promise((res, rej) => {
    const req = request({ host: '127.0.0.1', port, path: '/api/state', headers: { host, 'x-t247': '1' } }, (r) => {
      r.resume();
      res(r.statusCode);
    });
    req.on('error', rej);
    req.end();
  });

async function freePort() {
  return new Promise((res, rej) => {
    const s = createServer();
    s.once('error', rej);
    s.listen(0, '127.0.0.1', () => {
      const { port } = s.address();
      s.close(() => res(port));
    });
  });
}

function start(port, dataDir) {
  const child = spawn(exe, ['--no-open', '--port', String(port)], {
    env: { ...process.env, T247_DATA_DIR: dataDir, T247_OPEN_REPORTS: '0' },
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
  });
  child.output = '';
  child.stdout.on('data', (d) => (child.output += d));
  child.stderr.on('data', (d) => (child.output += d));
  child.exited = new Promise((res) => child.once('exit', (code) => res(code)));
  return child;
}

async function waitFor(fn, timeoutMs, what) {
  const t0 = Date.now();
  for (;;) {
    const v = await fn();
    if (v) return v;
    if (Date.now() - t0 > timeoutMs) throw new Error(`timed out waiting for ${what}`);
    await sleep(50);
  }
}

if (!existsSync(exe)) {
  console.error(`No executable at ${exe}. Run "npm run package" first.`);
  process.exit(2);
}

const dataDir = mkdtempSync(join(tmpdir(), 't247-smoke-'));
const port = await freePort();
const base = `http://127.0.0.1:${port}`;
const api = (path, body) =>
  fetch(base + path, {
    method: body === undefined ? 'GET' : 'POST',
    headers: { 'x-t247': '1', 'content-type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
const json = async (path, body) => (await api(path, body)).json();


function findChrome() {
  return [
    process.env.T247_TEST_CHROME,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/usr/bin/google-chrome',
    '/usr/bin/google-chrome-stable',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
  ].find((p) => p && existsSync(p));
}

/** A table page in a frame from another site, in a headless Chrome; the packaged program reads three new hands from it. */
async function liveWithChrome(chromePath, json, waitFor) {
  const cells = ['P', 'B'];
  const inner = createHttpServer((req, res) => {
    if (req.url === '/state') return void res.end(JSON.stringify(cells));
    res.setHeader('content-type', 'text/html');
    res.end(`<body><script>
      async function t(){try{const c=await(await fetch('/state')).json();
        document.body.innerHTML='<div data-role="Bead-road">'+c.map(x=>'<div data-role="roadItem">'+x+'</div>').join('')+'</div>'}catch(e){}}
      setInterval(t,40);t()</script></body>`);
  });
  await new Promise((r) => inner.listen(0, '127.0.0.1', r));
  const outer = createHttpServer((_q, res) => {
    res.setHeader('content-type', 'text/html');
    res.end(`<iframe src="http://localhost:${inner.address().port}/"></iframe>`);
  });
  await new Promise((r) => outer.listen(0, '127.0.0.1', r));
  const profile = mkdtempSync(join(tmpdir(), 't247-smoke-chrome-'));
  const chrome = spawn(chromePath, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--no-first-run', '--disable-gpu', 'about:blank'], { stdio: 'ignore', windowsHide: true });
  try {
    let port = 0;
    for (let i = 0; i < 100 && !port; i++) {
      await sleep(100);
      try { port = Number(readFileSync(join(profile, 'DevToolsActivePort'), 'utf8').split(/\r?\n/)[0]); } catch { /* not yet */ }
    }
    if (!port) return { ok: false, detail: 'Chrome did not start' };
    await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(`http://127.0.0.1:${outer.address().port}/`)}`, { method: 'PUT' });
    const started = await json('/api/start', { settings: { feed: 'live', live_port: port, unit_value: 1, min_chip: 0, start_balance: 1000 } });
    if (!started.ok) return { ok: false, detail: JSON.stringify(started) };
    await waitFor(async () => ((await json('/api/state?since=0')).status.startsWith('FEED') ? null : true), 15_000, 'the live feed to connect');
    cells.push('B', 'T', 'P');
    const state = await waitFor(async () => {
      const st = await json('/api/state?since=0');
      return st.hands >= 3 ? st : null;
    }, 15_000, 'three hands from the table');
    await json('/api/stop', {});
    const got = state.rows.map((r) => r.result).join('');
    return { ok: got === 'BTP', detail: `read "${got}", expected "BTP"` };
  } finally {
    chrome.kill();
    outer.closeAllConnections?.();
    inner.closeAllConnections?.();
    outer.close();
    inner.close();
    await sleep(300);
    try { rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }); } catch { /* temp folder */ }
  }
}

let child;
try {
  child = start(port, dataDir);
  await waitFor(() => child.output.includes('is running at'), 30_000, 'the server to start');
  check('starts and reports its address', child.output.includes(`${base}/`), child.output);

  const page = await fetch(base + '/');
  const html = await page.text();
  check('serves the embedded page', page.status === 200 && html.includes('TRADE247-V1') && html.includes('BACKTEST'));
  check('the page has the English number format', html.includes('1,234.56 (English format)'));
  check('hardens the page (CSP, no framing)', (page.headers.get('content-security-policy') ?? '').includes("frame-ancestors 'none'") && page.headers.get('x-frame-options') === 'DENY');
  check('refuses API calls without the header', (await fetch(base + '/api/state')).status === 403);
  check('refuses an unexpected Host', (await statusWithHost(port, 'evil.example')) === 403);
  check('accepts the local Host', (await statusWithHost(port, `127.0.0.1:${port}`)) === 200);

  // A short Replay session.
  const started = await json('/api/start', { settings: { feed: 'csv', csv_text: 'PPBPBB|PPBB', unit_value: 1, min_chip: 0, start_balance: 1000 } });
  check('starts a Replay session', started.ok === true, JSON.stringify(started));
  const state = await waitFor(async () => {
    const s = await json('/api/state?since=0');
    return s.running ? null : s;
  }, 15_000, 'the replay to finish');
  check('plays every hand and stops', state.hands === 10 && state.status === 'STOPPED (replay finished)', `${state.hands} hands, ${state.status}`);
  check('writes the history log', existsSync(join(dataDir, 'logs', 'trade247_session_history.csv')));

  const xlsx = await api('/api/report?fmt=xlsx');
  const bytes = Buffer.from(await xlsx.arrayBuffer());
  check('builds an Excel report', xlsx.status === 200 && bytes.subarray(0, 2).toString() === 'PK' && bytes.length > 1000, `${xlsx.status}, ${bytes.length} bytes`);

  // The backtest runs on worker threads started from the embedded worker code.
  const runBacktest = async () => {
    const r = await json('/api/backtest/start', { settings: { unit_value: 1 }, params: { source: 'sim', shoes: 20, trials: 8, bankroll_units: 0, seed: 5 } });
    if (!r.ok) throw new Error(`backtest refused: ${r.error}`);
    return waitFor(async () => {
      const s = await json(`/api/backtest/status?id=${r.id}`);
      return s.finished ? s : null;
    }, 60_000, 'the backtest to finish');
  };
  const first = await runBacktest();
  check('runs a backtest on worker threads', first.ok === true && first.result?.trials === 8 && first.done === 8, JSON.stringify(first).slice(0, 300));
  const second = await runBacktest();
  check('a backtest with the same seed repeats exactly', first.result !== undefined && JSON.stringify(first.result) === JSON.stringify(second.result));


  // The Live table feed: first without a Chrome (it must say what to do), then, when a Chrome is installed, against a
  // mock table in a real headless Chrome, which proves the packaged program can talk to the browser.
  const waiting = await json('/api/start', { settings: { feed: 'live', live_port: 1, unit_value: 1, min_chip: 0, start_balance: 1000 } });
  const waitingState = await waitFor(async () => {
    const st = await json('/api/state?since=0');
    return st.status.startsWith('FEED WAITING - Cannot reach') ? st : null;
  }, 15_000, 'the live feed to say it cannot reach Chrome');
  check('the live feed explains how to connect when Chrome is not there', waiting.ok === true && waitingState.running === true, waitingState.status);
  await json('/api/stop', {});

  const inspect = spawn(exe, ['--inspect-table', '--live-port', '1'], { env: { ...process.env, T247_DATA_DIR: dataDir }, windowsHide: true });
  let inspectOut = '';
  inspect.stdout.on('data', (d) => (inspectOut += d));
  inspect.stderr.on('data', (d) => (inspectOut += d));
  const inspectCode = await new Promise((r) => inspect.on('exit', r));
  check('--inspect-table without Chrome exits with a clear message', inspectCode === 1 && inspectOut.includes('Cannot reach Chrome'), `exit ${inspectCode}: ${inspectOut.trim()}`);

  const chromePath = findChrome();
  if (!chromePath) {
    console.log('SKIP  live feed against a real Chrome (no Chrome or Edge found; set T247_TEST_CHROME)');
  } else {
    const live = await liveWithChrome(chromePath, json, waitFor);
    check('reads a mock table in a real Chrome through the packaged program', live.ok, live.detail);
  }

  check('keeps its settings on disk', existsSync(join(dataDir, 'settings.json')) && readdirSync(dataDir).includes('meta.json'));
  check('never writes a Telegram token into the settings file', !readFileSync(join(dataDir, 'settings.json'), 'utf8').includes('telegram_token'));

  // A second copy must refuse the port, with a clear message and a failure exit code.
  const twin = start(port, mkdtempSync(join(tmpdir(), 't247-smoke2-')));
  const twinCode = await Promise.race([twin.exited, sleep(15_000).then(() => 'timeout')]);
  check('a second copy refuses the same port', twinCode === 1 && twin.output.includes('already in use'), `exit ${twinCode}: ${twin.output.trim()}`);
  if (twinCode === 'timeout') twin.kill();
} catch (e) {
  check('smoke test completed without an error', false, e.stack ?? String(e));
  if (child) console.log('--- program output ---\n' + child.output);
} finally {
  if (child) {
    child.kill();
    await Promise.race([child.exited, sleep(5000)]);
  }
  rmSync(dataDir, { recursive: true, force: true });
}

const failed = results.filter((r) => !r.ok);
console.log(`\n${results.length - failed.length}/${results.length} checks passed${failed.length ? ` - FAILED: ${failed.map((f) => f.name).join('; ')}` : ''}`);
process.exit(failed.length ? 1 : 0);
