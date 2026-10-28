import { readFileSync } from 'node:fs';
import Fastify, { type FastifyInstance, type FastifyRequest } from 'fastify';
import { BacktestManager } from '../backtest/jobs.js';
import { writeSnapshot } from '../report/export.js';
import { uiHtml } from '../runtime.js';
import { XLSX_MIME } from '../report/xlsx.js';
import { SessionManager, type SessionOptions } from '../session.js';
import { SettingsStore } from '../store/files.js';

export interface AppOptions {
  dataDir: string;
  /** The port the server listens on; used to validate the Host and Origin headers. */
  port: number;
  uiPath?: string;
  logger?: boolean;
  manager?: SessionManager;
  /** Opens a saved report in the user's default application. Without it the page downloads the file instead. */
  openFile?: (path: string) => void;
  /** Test hooks for the Telegram client. */
  telegram?: SessionOptions['telegram'];
  /** Runs backtests; tests pass one that plays on the calling thread. */
  backtest?: BacktestManager;
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
  const backtest = options.backtest ?? new BacktestManager();
  const manager = options.manager ?? new SessionManager(new SettingsStore(options.dataDir), { telegram: options.telegram });
  const page = options.uiPath ? readFileSync(options.uiPath, 'utf8') : uiHtml();
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

  // The BOM makes Excel read the file as UTF-8 (the log uses the "·" character).
  const BOM = String.fromCodePoint(0xfeff);
  const csvBytes = (csv: string): string => BOM + csv;

  app.post('/api/report/open', async (req) => {
    const fmt = formatOf(body(req).fmt);
    if (!fmt) return { ok: false, error: 'Unknown report format' };
    const data = fmt === 'csv' ? manager.reportCsv() : manager.reportXlsx();
    if (data === null) return { ok: false, error: 'No log yet - press START first' };

    let path: string;
    try {
      path = writeSnapshot(options.dataDir, fmt, typeof data === 'string' ? csvBytes(data) : data, new Date());
    } catch (e) {
      return { ok: false, error: `Could not save the report: ${(e as Error).message}` };
    }
    if (options.openFile) {
      try {
        options.openFile(path);
        return { ok: true, opened: true, path };
      } catch {
        // fall through: the page downloads the same snapshot instead
      }
    }
    // opened: false makes the page download the same snapshot through GET /api/report.
    return { ok: true, opened: false, path };
  });

  app.get('/api/report', async (req, reply) => {
    const fmt = formatOf((req.query as { fmt?: string }).fmt);
    if (fmt === 'xlsx') {
      const xlsx = manager.reportXlsx();
      if (xlsx === null) return reply.code(404).send({ ok: false, error: 'No report available' });
      return reply.header('Content-Disposition', 'attachment; filename="trade247_report.xlsx"').type(XLSX_MIME).send(xlsx);
    }
    const csv = fmt === 'csv' ? manager.reportCsv() : null;
    if (csv === null) return reply.code(404).send({ ok: false, error: 'No report available' });
    return reply
      .header('Content-Disposition', 'attachment; filename="trade247_report.csv"')
      .type('text/csv; charset=utf-8')
      .send(csvBytes(csv));
  });

  app.post('/api/telegram_test', async () => manager.telegramTest());

  app.post('/api/backtest/start', async (req) => {
    const { settings, params } = body(req);
    return backtest.start(manager.currentSettings(), settings, params);
  });
  app.get('/api/backtest/status', async (req) => {
    const status = backtest.status((req.query as { id?: string }).id);
    return status ?? { ok: false, finished: true, done: 0, total: 1, seconds: 0, error: 'Unknown backtest' };
  });
  app.post('/api/backtest/cancel', async (req) => ({ ok: backtest.cancel(body(req).id) }));

  app.addHook('onClose', async () => {
    backtest.dispose();
    manager.stop();
    await manager.flushAlerts();
  });
  return app;
}
