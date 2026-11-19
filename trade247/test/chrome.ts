import { spawn, type ChildProcess } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/** A Chrome or Edge to drive in the live-feed tests, or null (the tests that need one are then skipped). */
export function findChrome(): string | null {
  const fromEnv = process.env.T247_TEST_CHROME;
  const candidates = [
    fromEnv,
    'C:/Program Files/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
    '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    '/usr/bin/google-chrome',
    '/usr/bin/google-chrome-stable',
    '/usr/bin/chromium',
    '/usr/bin/chromium-browser',
  ];
  return candidates.find((c): c is string => !!c && existsSync(c)) ?? null;
}

export interface TestChrome {
  port: number;
  stop: () => Promise<void>;
}

/** Starts a headless Chrome on a free debugging port, with a throwaway profile. */
export async function startChrome(executable: string): Promise<TestChrome> {
  const profile = mkdtempSync(join(tmpdir(), 't247-chrome-'));
  const child: ChildProcess = spawn(
    executable,
    [
      '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--no-first-run',
      '--no-default-browser-check', '--disable-gpu', '--disable-extensions',
      // CI containers and recent Ubuntu images do not allow Chrome's sandbox; this is a throwaway test browser.
      ...(process.platform === 'linux' ? ['--no-sandbox'] : []),
      'about:blank',
    ],
    { stdio: ['ignore', 'ignore', 'pipe'], windowsHide: true },
  );
  let launchError: Error | undefined;
  let diagnostics = '';
  child.on('error', (error) => { launchError = error; });
  child.stderr?.on('data', (chunk: Buffer) => {
    diagnostics = (diagnostics + chunk.toString()).slice(-4000);
  });
  const stop = async () => {
    if (child.exitCode === null && child.signalCode === null && child.pid) {
      child.kill();
      const end = Date.now() + 2000;
      while (child.exitCode === null && child.signalCode === null && Date.now() < end) {
        await new Promise((r) => setTimeout(r, 50));
      }
    }
    try {
      rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch {
      /* Chrome may still hold a file; the temp folder is cleaned by the system */
    }
  };
  const portFile = join(profile, 'DevToolsActivePort');
  let port = 0;
  // A cold CI browser can take longer than ten seconds. Require a responding
  // endpoint, not merely a port file, within the suite's 30-second setup budget.
  const deadline = Date.now() + 20_000;
  while (Date.now() < deadline && !port) {
    if (launchError || child.exitCode !== null || child.signalCode !== null) break;
    await new Promise((r) => setTimeout(r, 100));
    try {
      const candidate = Number(readFileSync(portFile, 'utf8').split('\n')[0]);
      if (Number.isInteger(candidate) && candidate > 0 && candidate <= 65535) {
        const response = await fetch(`http://127.0.0.1:${candidate}/json/version`, { signal: AbortSignal.timeout(500) });
        if (response.ok) port = candidate;
      }
    } catch {
      /* port file or browser endpoint is not ready yet */
    }
  }
  if (!port) {
    const reason = launchError?.message ?? (child.exitCode !== null || child.signalCode !== null
      ? `exited (code ${child.exitCode}, signal ${child.signalCode})` : 'not ready within 20 seconds');
    await stop();
    throw new Error(`Chrome did not start: ${reason}. Executable: ${executable}\n${diagnostics}`);
  }
  return { port, stop };
}

export interface MockTable {
  /** What the table shows. Change it and the page follows within a moment. */
  state: { container: boolean; cells: string[]; /** When set, the road is drawn as a grid: one string per row, '.' for an empty cell. */ rows: string[] | null };
  /** The address to open: a page on one site that shows the table from another site in a frame. */
  pageUrl: string;
  stop: () => Promise<void>;
}

function listen(server: Server): Promise<number> {
  return new Promise((resolve) => server.listen(0, '127.0.0.1', () => resolve((server.address() as AddressInfo).port)));
}

/** A table page inside a frame from a different site (like a casino lobby around a game provider's table). */
export async function startMockTable(): Promise<MockTable> {
  const state = { container: true, cells: [] as string[], rows: null as string[] | null };
  const inner = createServer((req, res) => {
    if (req.url === '/state') {
      res.setHeader('content-type', 'application/json');
      res.end(JSON.stringify(state));
      return;
    }
    res.setHeader('content-type', 'text/html');
    res.end(`<!doctype html><body><script>
      async function tick() {
        try {
          const s = await (await fetch('/state')).json();
          document.body.innerHTML = s.container
            ? '<div data-role="status"><span>PLACE YOUR BETS</span></div><div data-role="balance">$ 1,234.50</div>' +
              (s.rows
                ? '<div class="grid">' + s.rows.map(r => '<div class="row">' + [...r].map(ch => '<div class="cell">' + (ch === '.' ? '' : ch) + '</div>').join('') + '</div>').join('') + '</div>'
                : '<div data-role="Bead-road">' + s.cells.map(c => '<div data-role="roadItem">' + c + '</div>').join('') + '</div>')
            : '<p>lobby</p>';
        } catch (e) {}
      }
      setInterval(tick, 40); tick();
    </script></body>`);
  });
  const innerPort = await listen(inner);
  const outer = createServer((_req, res) => {
    res.setHeader('content-type', 'text/html');
    res.end(`<!doctype html><title>Casino lobby</title><iframe src="http://localhost:${innerPort}/" width="600" height="400"></iframe>`);
  });
  const outerPort = await listen(outer);
  return {
    state,
    pageUrl: `http://127.0.0.1:${outerPort}/lobby?token=SECRET123#frag`,
    stop: async () => {
      inner.closeAllConnections();
      outer.closeAllConnections();
      await Promise.all([new Promise((r) => inner.close(r)), new Promise((r) => outer.close(r))]);
    },
  };
}

/** Opens an address in a new tab of the test Chrome. */
export async function openTab(port: number, url: string): Promise<void> {
  const r = await fetch(`http://127.0.0.1:${port}/json/new?${encodeURIComponent(url)}`, { method: 'PUT' });
  if (!r.ok) throw new Error(`Could not open a tab (HTTP ${r.status})`);
}

export async function until(check: () => boolean, ms = 8000, step = 40): Promise<void> {
  const end = Date.now() + ms;
  while (!check()) {
    if (Date.now() > end) throw new Error('Timed out waiting for the condition');
    await new Promise((r) => setTimeout(r, step));
  }
}
