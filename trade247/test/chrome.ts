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
    { stdio: 'ignore', windowsHide: true },
  );
  const portFile = join(profile, 'DevToolsActivePort');
  let port = 0;
  for (let i = 0; i < 100 && !port; i++) {
    await new Promise((r) => setTimeout(r, 100));
    try {
      port = Number(readFileSync(portFile, 'utf8').split('\n')[0]);
    } catch {
      /* not written yet */
    }
  }
  if (!port) {
    child.kill();
    throw new Error('Chrome did not start');
  }
  return {
    port,
    stop: async () => {
      child.kill();
      await new Promise((r) => setTimeout(r, 300));
      try {
        rmSync(profile, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
      } catch {
        /* Chrome may still hold a file; the temp folder is cleaned by the system */
      }
    },
  };
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
