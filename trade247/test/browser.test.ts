import { mkdtempSync, rmSync } from 'node:fs';
import { createServer, type Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { findBrowser, openTableBrowser } from '../src/live/browser.js';
import { listPages } from '../src/live/cdp.js';
import { buildApp } from '../src/server/app.js';
import { cleanup, harness, HOST } from './helpers.js';
import { findChrome } from './chrome.js';

afterEach(cleanup);

const dirs: string[] = [];
const tmp = (): string => {
  const d = mkdtempSync(join(tmpdir(), 't247-br-'));
  dirs.push(d);
  return d;
};
afterEach(() => {
  for (const d of dirs.splice(0)) {
    try {
      rmSync(d, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
    } catch {
      /* the browser may still hold a file for a moment; the system cleans the temp folder */
    }
  }
});

/** A stand-in for a browser that is already listening on its debugging port. */
function fakeBrowserOnPort(): Promise<{ server: Server; port: number }> {
  return new Promise((resolve) => {
    const server = createServer((_req, res) => res.end('[]'));
    server.listen(0, '127.0.0.1', () => resolve({ server, port: (server.address() as AddressInfo).port }));
  });
}

const freePort = async (): Promise<number> => {
  const { server, port } = await fakeBrowserOnPort();
  await new Promise((r) => server.close(r));
  return port;
};

describe('finding a browser', () => {
  it('takes the first installed one and says nothing when there is none', () => {
    expect(findBrowser({}, () => false)).toBeNull();
    const first = findBrowser({}, () => true);
    expect(first).toBeTruthy();
    const seen: string[] = [];
    findBrowser({}, (p) => (seen.push(p), false));
    expect(seen[0]).toBe(first);
  });
});

describe('opening the browser for the table', () => {
  it('refuses a bad port', async () => {
    for (const port of [0, 70000, 1.5, Number.NaN]) {
      expect(await openTableBrowser({ port, profileDir: tmp() })).toMatchObject({ ok: false, error: expect.stringContaining('port') });
    }
  });

  it('does not start a second browser when one is already listening on the port', async () => {
    const { server, port } = await fakeBrowserOnPort();
    let started = 0;
    try {
      const r = await openTableBrowser({ port, profileDir: tmp(), spawnFn: (() => (started++, {})) as never });
      expect(r).toEqual({ ok: true, already: true });
      expect(started).toBe(0);
    } finally {
      server.close();
    }
  });

  it('says so when no browser is installed', async () => {
    const port = await freePort();
    const r = await openTableBrowser({ port, profileDir: tmp(), executable: null });
    expect(r).toMatchObject({ ok: false, error: expect.stringContaining('Chrome was not found') });
  });

  it('starts the browser with a debugging port on this computer only and a profile of its own', async () => {
    const port = await freePort();
    const profileDir = join(tmp(), 'profile');
    let call: { exe: string; args: string[]; options: any } | null = null;
    const spawnFn = ((exe: string, args: string[], options: any) => {
      call = { exe, args, options };
      return { pid: 4242, on: () => {}, unref: () => {} };
    }) as never;
    const r = await openTableBrowser({ port, profileDir, executable: 'C:/fake/chrome.exe', spawnFn, waitMs: 500 });
    expect(call).not.toBeNull();
    const c = call!;
    expect(c.exe).toBe('C:/fake/chrome.exe');
    expect(c.args).toContain(`--remote-debugging-port=${port}`);
    expect(c.args).toContain('--remote-debugging-address=127.0.0.1');
    expect(c.args).toContain(`--user-data-dir=${profileDir}`);
    expect(c.options).toMatchObject({ detached: true, stdio: 'ignore' });
    // The fake never opens the port, so the answer explains what to do.
    expect(r).toMatchObject({ ok: false, error: expect.stringContaining('not listening on the debugging port') });
    expect((r as { error: string }).error).toContain(profileDir);
  });

  it.skipIf(!findChrome())('opens a real Chrome that answers on the port, and recognises it the second time', async () => {
    const port = await freePort();
    const profileDir = join(tmp(), 'profile');
    const first = await openTableBrowser({ port, profileDir, executable: findChrome(), waitMs: 25_000, extraArgs: ['--headless=new', '--disable-gpu', ...(process.platform === 'linux' ? ['--no-sandbox'] : [])] });
    try {
      expect(first.ok).toBe(true);
      expect(first.already).toBeUndefined();
      expect((await listPages(port)).length).toBeGreaterThanOrEqual(0);
      expect(await openTableBrowser({ port, profileDir, executable: findChrome() })).toEqual({ ok: true, already: true });
    } finally {
      if (first.pid) {
        try {
          process.kill(first.pid);
        } catch {
          /* already gone */
        }
      }
      await new Promise((r) => setTimeout(r, 400));
    }
  }, 60_000);
});

describe('the app route', () => {
  it('opens the browser on the port from the page, falling back to the saved port, in the data folder', async () => {
    const calls: Array<{ port: number; dir: string }> = [];
    const dir = tmp();
    const app = buildApp({
      dataDir: dir,
      port: 8247,
      openBrowser: async (port, profileDir) => {
        calls.push({ port, dir: profileDir });
        return { ok: true };
      },
    });
    const post = (payload: unknown) =>
      app.inject({ method: 'POST', url: '/api/live/open_browser', headers: { host: HOST, 'x-t247': '1', 'content-type': 'application/json' }, payload: JSON.stringify(payload) });
    try {
      expect((await post({ port: 9333 })).json()).toEqual({ ok: true });
      expect((await post({})).json()).toEqual({ ok: true });
      expect((await post({ port: 'x' })).json()).toEqual({ ok: true });
      expect((await post({ port: 99999 })).json()).toEqual({ ok: true });
      expect(calls.map((c) => c.port)).toEqual([9333, 9014, 9014, 9014]);
      expect(calls[0]!.dir).toBe(join(dir, 'chrome-profile'));
      // Like every call to the API it needs the custom header.
      const bare = await app.inject({ method: 'POST', url: '/api/live/open_browser', headers: { host: HOST, 'content-type': 'application/json' }, payload: '{}' });
      expect(bare.statusCode).toBe(403);
      expect(calls).toHaveLength(4);
    } finally {
      await app.close();
    }
  });

  it('reports why when the browser cannot be opened', async () => {
    const app = buildApp({ dataDir: tmp(), port: 8247, openBrowser: async () => ({ ok: false, error: 'Chrome was not found' }) });
    try {
      const r = await app.inject({ method: 'POST', url: '/api/live/open_browser', headers: { host: HOST, 'x-t247': '1', 'content-type': 'application/json' }, payload: '{}' });
      expect(r.json()).toEqual({ ok: false, error: 'Chrome was not found' });
    } finally {
      await app.close();
    }
  });

  it('the page has the button and calls the route', async () => {
    const html = (await harness().get('/')).text;
    expect(html).toContain('id="openChrome"');
    expect(html).toContain('/api/live/open_browser');
  });
});
