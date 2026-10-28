import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { FastifyInstance } from 'fastify';
import { buildApp, type AppOptions } from '../src/server/app.js';
import { SessionManager } from '../src/session.js';
import { SettingsStore } from '../src/store/files.js';

export const PORT = 8247;
export const HOST = `127.0.0.1:${PORT}`;

export interface Harness {
  app: FastifyInstance;
  manager: SessionManager;
  dir: string;
  get: (url: string) => Promise<{ status: number; json: any; text: string; headers: Record<string, unknown>; raw: Buffer }>;
  post: (url: string, payload?: unknown) => Promise<{ status: number; json: any; text: string }>;
  close: () => Promise<void>;
}

const open: Harness[] = [];

export interface HarnessOptions {
  dir?: string;
  /** Extra app options (openFile, telegram hooks). */
  app?: Partial<Pick<AppOptions, 'openFile' | 'telegram'>>;
}

export function harness(dirOrOptions?: string | HarnessOptions): Harness {
  const options: HarnessOptions = typeof dirOrOptions === 'string' ? { dir: dirOrOptions } : (dirOrOptions ?? {});
  const dir = options.dir ?? mkdtempSync(join(tmpdir(), 't247-'));
  // Telegram hooks that make alerts fast and deterministic unless a test says otherwise.
  const telegram = { minGapMs: 0, ...options.app?.telegram };
  const manager = new SessionManager(new SettingsStore(dir), { telegram });
  const app = buildApp({ dataDir: dir, port: PORT, manager, openFile: options.app?.openFile });
  const headers = { host: HOST, 'x-t247': '1', 'content-type': 'application/json' };
  const parse = (text: string): any => {
    try {
      return JSON.parse(text);
    } catch {
      return null;
    }
  };
  const h: Harness = {
    app,
    manager,
    dir,
    async get(url) {
      const r = await app.inject({ method: 'GET', url, headers });
      return { status: r.statusCode, json: parse(r.body), text: r.body, headers: r.headers, raw: r.rawPayload };
    },
    async post(url, payload = {}) {
      const r = await app.inject({ method: 'POST', url, headers, payload: JSON.stringify(payload) });
      return { status: r.statusCode, json: parse(r.body), text: r.body };
    },
    async close() {
      await app.close();
    },
  };
  open.push(h);
  return h;
}

export async function cleanup(): Promise<void> {
  while (open.length) {
    const h = open.pop()!;
    await h.close();
    rmSync(h.dir, { recursive: true, force: true });
  }
}

/** Settings that make a deterministic, fast manual session. */
export const MANUAL = { feed: 'manual', unit_value: 1, min_chip: 0, start_balance: 1000 };
