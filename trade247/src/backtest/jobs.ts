import { randomBytes } from 'node:crypto';
import { availableParallelism } from 'node:os';
import { Worker } from 'node:worker_threads';
import { randomSeed } from '../engine/dealer.js';
import { unsupportedReason } from '../engine/factory.js';
import { embeddedWorkerSource } from '../runtime.js';
import { normalizeSettings, type Settings } from '../engine/settings.js';
import { aggregate, type BacktestResult } from './stats.js';
import { parsePastedShoes, type TrialResult } from './trial.js';
import { playSessions, type WorkerInput, type WorkerMessage } from './worker.js';

export interface BacktestParams {
  source: 'sim' | 'paste';
  text: string;
  shoes: number;
  trials: number;
  bankroll_units: number;
  seed: number;
}

export const LIMITS = { maxShoes: 5000, maxTrials: 2000, maxPastedHands: 100_000, curvePoints: 120 } as const;

/** The work for one run, already validated. */
export interface RunSpec {
  settings: Settings;
  seed: number;
  trials: number;
  shoes: WorkerInput['shoes'];
  bankrollUnits: number;
}

export interface Execution {
  done: Promise<TrialResult[]>;
  cancel(): void;
}

/** Something that can play all the sessions of a run and report progress. */
export type Executor = (spec: RunSpec, onProgress: (done: number) => void) => Execution;

/** Plays the sessions on worker threads, so a live session's event loop is never held up. */
export function workerExecutor(maxWorkers = Math.max(1, Math.min(8, availableParallelism() - 1))): Executor {
  return (spec, onProgress) => {
    const count = Math.max(1, Math.min(maxWorkers, spec.trials));
    // Three ways to start a worker: from the code embedded in the packaged program; under tsx (development,
    // tests) through a small bootstrap for the TypeScript file; or the plain JavaScript file after "npm run build".
    const embedded = embeddedWorkerSource();
    const ts = import.meta.url.endsWith('.ts');
    const file = embedded === null ? new URL(ts ? './worker-dev.mjs' : './worker.js', import.meta.url) : null;
    const workers: Worker[] = [];
    let cancelled = false;

    const done = new Promise<TrialResult[]>((resolve, reject) => {
      const parts: TrialResult[][] = [];
      let finished = 0;
      const fail = (error: Error): void => {
        for (const w of workers) void w.terminate();
        reject(error);
      };
      for (let index = 0; index < count; index++) {
        const input: WorkerInput = {
          settings: spec.settings, seed: spec.seed, trials: spec.trials, index, workers: count,
          shoes: spec.shoes, bankrollUnits: spec.bankrollUnits, curvePoints: LIMITS.curvePoints,
        };
        const worker = file ? new Worker(file, { workerData: input }) : new Worker(embedded!, { eval: true, workerData: input });
        workers.push(worker);
        worker.on('message', (m: WorkerMessage) => {
          if (m.type === 'progress') onProgress(m.done);
          else {
            parts.push(m.results);
            if (++finished === count) resolve(parts.flat());
          }
        });
        worker.on('error', (e) => fail(e));
        worker.on('exit', (code) => {
          if (code !== 0 && !cancelled && finished < count) fail(new Error(`A backtest worker stopped unexpectedly (code ${code}).`));
        });
      }
    });
    return {
      done,
      cancel() {
        cancelled = true;
        for (const w of workers) void w.terminate();
      },
    };
  };
}

/** Plays the sessions on the calling thread, yielding between sessions. For tests and as a fallback. */
export function inlineExecutor(): Executor {
  return (spec, onProgress) => {
    let cancelled = false;
    const done = (async () => {
      const results: TrialResult[] = [];
      for (let i = 0; i < spec.trials && !cancelled; i++) {
        results.push(...playSessions({ ...spec, index: i, workers: spec.trials, curvePoints: LIMITS.curvePoints }, onProgress, () => cancelled));
        await new Promise((r) => setImmediate(r));
      }
      return results;
    })();
    return { done, cancel: () => void (cancelled = true) };
  };
}

export type StartReply = { ok: true; id: string } | { ok: false; error: string };

export interface StatusReply {
  ok: boolean;
  id: string;
  source: 'sim' | 'paste';
  total: number;
  done: number;
  seconds: number;
  finished: boolean;
  error?: string;
  result?: BacktestResult;
}

interface Job {
  id: string;
  source: 'sim' | 'paste';
  total: number;
  done: number;
  startedAt: number;
  endedAt?: number;
  error?: string;
  result?: BacktestResult;
  cancel(): void;
}

const KEEP_FINISHED = 5;

const whole = (v: unknown, fallback: number): number => {
  const n = typeof v === 'number' ? v : typeof v === 'string' && v.trim() !== '' ? Number(v) : NaN;
  return Number.isFinite(n) ? Math.floor(n) : fallback;
};

export class BacktestManager {
  private readonly jobs = new Map<string, Job>();

  constructor(
    private readonly executor: Executor = workerExecutor(),
    private readonly now: () => number = Date.now,
  ) {}

  /** Validate a request from the page and start it. Nothing is saved: the settings are only borrowed. */
  start(base: Settings, rawSettings: unknown, rawParams: unknown): StartReply {
    if ([...this.jobs.values()].some((j) => j.endedAt === undefined)) return { ok: false, error: 'A backtest is already running' };

    const { settings, errors } = normalizeSettings(rawSettings, base);
    if (errors.length) return { ok: false, error: errors[0]! };
    const unsupported = unsupportedReason(settings);
    if (unsupported) return { ok: false, error: unsupported };
    settings.telegram_token = '';
    settings.telegram_chat_id = '';

    const p = (rawParams && typeof rawParams === 'object' ? rawParams : {}) as Record<string, unknown>;
    const source = p.source === 'paste' ? 'paste' : 'sim';
    const bankroll = Number(p.bankroll_units);
    const bankrollUnits = Number.isFinite(bankroll) && bankroll > 0 ? Math.min(bankroll, 1e9) : 0;
    const seed = whole(p.seed, 0) > 0 ? Math.min(whole(p.seed, 0), 2 ** 31 - 1) : randomSeed();

    let spec: RunSpec;
    if (source === 'paste') {
      const shoes = parsePastedShoes(typeof p.text === 'string' ? p.text : '');
      const hands = shoes.reduce((a, s) => a + s.length, 0);
      if (hands === 0) return { ok: false, error: 'Paste P / B / T results to test' };
      if (hands > LIMITS.maxPastedHands) return { ok: false, error: `At most ${LIMITS.maxPastedHands.toLocaleString('en-US')} pasted hands` };
      spec = { settings, seed, trials: 1, shoes: { kind: 'paste', shoes }, bankrollUnits };
    } else {
      const shoes = whole(p.shoes, 500);
      const trials = whole(p.trials, 100);
      if (shoes < 1 || shoes > LIMITS.maxShoes) return { ok: false, error: `Shoes per session must be between 1 and ${LIMITS.maxShoes}` };
      if (trials < 1 || trials > LIMITS.maxTrials) return { ok: false, error: `Sessions must be between 1 and ${LIMITS.maxTrials}` };
      spec = { settings, seed, trials, shoes: { kind: 'sim', count: shoes }, bankrollUnits };
    }

    const job: Job = {
      id: randomBytes(8).toString('hex'), source, total: spec.trials, done: 0, startedAt: this.now(), cancel: () => {},
    };
    this.jobs.set(job.id, job);
    this.prune();

    let execution: Execution;
    try {
      execution = this.executor(spec, (n) => void (job.done = Math.min(job.total, job.done + n)));
    } catch (e) {
      this.end(job, undefined, `Could not start the backtest: ${(e as Error).message}`);
      return { ok: true, id: job.id };
    }
    job.cancel = () => {
      execution.cancel();
      this.end(job, undefined, 'Cancelled');
    };
    execution.done.then(
      (results) => {
        if (job.endedAt !== undefined) return; // cancelled meanwhile
        const shoes = spec.shoes.kind === 'sim' ? spec.shoes.count : spec.shoes.shoes.length;
        job.done = job.total;
        this.end(job, aggregate(results, { unitValue: settings.unit_value, bankrollUnits, seed, shoes }));
      },
      (e: Error) => this.end(job, undefined, e.message),
    );
    return { ok: true, id: job.id };
  }

  status(id: unknown): StatusReply | null {
    const job = typeof id === 'string' ? this.jobs.get(id) : undefined;
    if (!job) return null;
    const finished = job.endedAt !== undefined;
    return {
      ok: !job.error,
      id: job.id,
      source: job.source,
      total: job.total,
      done: job.done,
      seconds: Math.round((((job.endedAt ?? this.now()) - job.startedAt) / 1000) * 10) / 10,
      finished,
      ...(job.error ? { error: job.error } : {}),
      ...(job.result ? { result: job.result } : {}),
    };
  }

  cancel(id: unknown): boolean {
    const job = typeof id === 'string' ? this.jobs.get(id) : undefined;
    if (!job || job.endedAt !== undefined) return false;
    job.cancel();
    return true;
  }

  /** Stop whatever is running (the app is closing). */
  dispose(): void {
    for (const job of this.jobs.values()) if (job.endedAt === undefined) job.cancel();
  }

  private end(job: Job, result?: BacktestResult, error?: string): void {
    if (job.endedAt !== undefined) return;
    job.endedAt = this.now();
    if (result) job.result = result;
    if (error) job.error = error;
  }

  private prune(): void {
    const finished = [...this.jobs.values()].filter((j) => j.endedAt !== undefined);
    for (const old of finished.slice(0, Math.max(0, finished.length - KEEP_FINISHED))) this.jobs.delete(old.id);
  }
}
