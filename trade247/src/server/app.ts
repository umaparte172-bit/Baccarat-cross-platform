import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import Fastify, { type FastifyInstance, type FastifyRequest } from 'fastify';
import { SessionManager } from '../session.js';
import { SettingsStore } from '../store/files.js';

export interface AppOptions {
  dataDir: string;
  /** The port the server listens on; used to validate the Host and Origin headers. */
  port: number;
  uiPath?: string;
  logger?: boolean;
  manager?: SessionManager;
}

const CSP = [
  "default-src 'self'",
  "script-src 'self' 'unsafe-inline'",
  "style-src 'self' 'unsafe-inline'",
  'img-src data:',
  "connect-src 'self'",
  "frame-ancestors 'none'",
  "base-uri 'none'",
  "form-action 'none'",
].join('; ');

const REPORT_FORMATS = ['csv', 'xlsx'] as const;
type ReportFormat = (typeof REPORT_FORMATS)[number];

const body = (req: FastifyRequest): Record<string, unknown> =>
  req.body && typeof req.body === 'object' ? (req.body as Record<string, unknown>) : {};

export function buildApp(options: AppOptions): FastifyInstance {
  const manager = options.manager ?? new SessionManager(new SettingsStore(options.dataDir));
  const uiPath = options.uiPath ?? fileURLToPath(new URL('../../ui/index.html', import.meta.url));
  const page = readFileSync(uiPath, 'utf8');
  const allowedHosts = new Set([`127.0.0.1:${options.port}`, `localhost:${options.port}`, `[::1]:${options.port}`]);

  const app = Fastify({ logger: options.logger ?? false, bodyLimit: 1_048_576 });

  // Only the local machine may talk to the app. Host checks stop DNS-rebinding; the custom header
  // plus the missing CORS support stop other web pages from calling the API.
  app.addHook('onRequest', async (req, reply) => {
    reply.header('X-Content-Type-Options', 'nosniff');
    reply.header('Cache-Control', 'no-store');
    reply.header('Content-Security-Policy', CSP);
    reply.header('X-Frame-Options', 'DENY');
    reply.header('Referrer-Policy', 'no-referrer');

    const host = String(req.headers.host ?? '').toLowerCase();
    if (!allowedHosts.has(host)) return reply.code(403).send({ ok: false, error: 'Forbidden host' });

    const origin = req.headers.origin;
    if (origin !== undefined && origin !== `http://${host}`) {
      return reply.code(403).send({ ok: false, error: 'Forbidden origin' });
    }
    if (req.url.startsWith('/api/') && req.headers['x-t247'] !== '1') {
      return reply.code(403).send({ ok: false, error: 'Missing X-T247 header' });
    }
  });

  app.setErrorHandler((error, req, reply) => {
    const status = (error as { statusCode?: number }).statusCode;
    if (status && status >= 400 && status < 500) return reply.code(status).send({ ok: false, error: 'Bad request' });
    req.log.error(error);
    return reply.code(500).send({ ok: false, error: 'Internal error' });
  });

  const serveIndex = async (_req: FastifyRequest, reply: import('fastify').FastifyReply) =>
    reply.type('text/html; charset=utf-8').send(page);
  app.get('/', serveIndex);
  app.get('/index.html', serveIndex);

  app.get('/api/state', async (req) => {
    const since = Number((req.query as { since?: string }).since);
    return manager.getState(since);
  });

  app.post('/api/start', async (req) => manager.start(body(req).settings));
  app.post('/api/apply', async (req) => manager.apply(body(req)));
  app.post('/api/settings', async (req) => manager.saveSettings(body(req)));
  app.post('/api/stop', async () => {
    manager.stop();
    return { ok: true };
  });
  app.post('/api/pause', async () => ({ ok: manager.pause() }));
  app.post('/api/resume', async () => ({ ok: manager.resume() }));
  app.post('/api/retry', async () => ({ ok: manager.retry() }));
  app.post('/api/reset', async () => manager.reset());
  app.post('/api/manual', async (req) => manager.manual(body(req).result));

  const formatOf = (raw: unknown): ReportFormat | null =>
    (REPORT_FORMATS as readonly unknown[]).includes(raw) ? (raw as ReportFormat) : null;

  app.post('/api/report/open', async (req) => {
    const fmt = formatOf(body(req).fmt);
    if (!fmt) return { ok: false, error: 'Unknown report format' };
    if (fmt === 'xlsx') return { ok: false, error: 'Excel (.xlsx) export is not available yet - use CSV' };
    if (manager.reportCsv() === null) return { ok: false, error: 'No log yet - press START first' };
    // opened: false makes the page download the same snapshot through GET /api/report.
    return { ok: true, opened: false, path: manager.historyPath };
  });

  app.get('/api/report', async (req, reply) => {
    const fmt = formatOf((req.query as { fmt?: string }).fmt);
    const csv = fmt === 'csv' ? manager.reportCsv() : null;
    if (csv === null) return reply.code(404).send({ ok: false, error: 'No report available' });
    return reply
      .header('Content-Disposition', 'attachment; filename="trade247_report.csv"')
      .type('text/csv; charset=utf-8')
      .send('﻿' + csv);
  });

  app.post('/api/telegram_test', async () => ({ ok: false, error: 'Telegram alerts are not available yet' }));

  app.post('/api/backtest/start', async () => ({ ok: false, error: 'Backtest is not available yet' }));
  app.get('/api/backtest/status', async () => ({ ok: false, finished: true, done: 0, total: 1, seconds: 0, error: 'Backtest is not available yet' }));
  app.post('/api/backtest/cancel', async () => ({ ok: true }));

  app.addHook('onClose', async () => manager.stop());
  return app;
}
