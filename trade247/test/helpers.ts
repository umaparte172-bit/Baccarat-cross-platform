import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { FastifyInstance } from 'fastify';
import { buildApp } from '../src/server/app.js';

export const PORT = 8247;
export const HOST = `127.0.0.1:${PORT}`;

export interface Harness {
  app: FastifyInstance;
  dir: string;
  get: (url: string) => Promise<{ status: number; json: any; text: string; headers: Record<string, unknown> }>;
  post: (url: string, payload?: unknown) => Promise<{ status: number; json: any; text: string }>;
  close: () => Promise<void>;
}

const open: Harness[] = [];

export function harness(dir: string = mkdtempSync(join(tmpdir(), 't247-'))): Harness {
  const app = buildApp({ dataDir: dir, port: PORT });
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
    dir,
    async get(url) {
      const r = await app.inject({ method: 'GET', url, headers });
      return { status: r.statusCode, json: parse(r.body), text: r.body, headers: r.headers };
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
