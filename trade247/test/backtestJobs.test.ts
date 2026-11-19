import { afterEach, describe, expect, it, vi } from 'vitest';
import { BacktestManager, LIMITS, inlineExecutor, workerExecutor, type Executor, type RunSpec, type StatusReply } from '../src/backtest/jobs.js';
import type { TrialResult } from '../src/backtest/trial.js';
import { DEFAULT_SETTINGS, normalizeSettings } from '../src/engine/settings.js';

const base = normalizeSettings({ unit_value: 1, min_chip: 0 }, DEFAULT_SETTINGS).settings;
const page = { unit_value: '1', min_chip: '0', strategy: 'IHG', telegram_token: '123456789:AAFakeTokenForTestsOnly_0123456789', telegram_chat_id: '42' };
const params = (over: Record<string, unknown> = {}) => ({ source: 'sim', text: '', shoes: 3, trials: 4, bankroll_units: 0, seed: 11, ...over });

const managers: BacktestManager[] = [];
const manager = (executor: Executor = inlineExecutor(), now?: () => number): BacktestManager => {
  const m = new BacktestManager(executor, now);
  managers.push(m);
  return m;
};
afterEach(() => {
  while (managers.length) managers.pop()!.dispose();
});

const finished = (m: BacktestManager, id: string): Promise<StatusReply> =>
  vi.waitFor(() => {
    const s = m.status(id);
    expect(s?.finished).toBe(true);
    return s!;
  }, { timeout: 60_000, interval: 10 });

function started(m: BacktestManager, p = params(), s: Record<string, unknown> = page): string {
  const r = m.start(base, s, p);
  if (!r.ok) throw new Error(r.error);
  return r.id;
}

/** An executor the test controls by hand. */
function controlled() {
  let resolve!: (r: TrialResult[]) => void;
  let reject!: (e: Error) => void;
  const calls: RunSpec[] = [];
  let progress: (n: number) => void = () => {};
  const cancel = vi.fn();
  const executor: Executor = (spec, onProgress) => {
    calls.push(spec);
    progress = onProgress;
    return {
      done: new Promise<TrialResult[]>((res, rej) => {
        resolve = res;
        reject = rej;
      }),
      cancel,
    };
  };
  return { executor, calls, cancel, resolve: (r: TrialResult[] = []) => resolve(r), reject: (e: Error) => reject(e), progress: (n: number) => progress(n) };
}

describe('a run from start to finish', () => {
  it('reports progress and then the aggregated result', async () => {
    const m = manager();
    const id = started(m);
    expect(id).toMatch(/^[0-9a-f]{16}$/);
    const done = await finished(m, id);
    expect(done).toMatchObject({ ok: true, id, source: 'sim', total: 4, done: 4, finished: true });
    expect(done.error).toBeUndefined();
    expect(done.result).toMatchObject({ trials: 4, shoes: 3, seed: 11, unit_value: 1, bankroll_units: 0 });
    expect(done.result!.curve.p50).toHaveLength(4);
  });

  it('times the run with the clock it is given', async () => {
    let t = 1_000;
    const c = controlled();
    const m = manager(c.executor, () => t);
    const id = started(m);
    t = 3_450;
    expect(m.status(id)).toMatchObject({ seconds: 2.5, finished: false });
    c.resolve([]);
    await finished(m, id);
    t = 99_000;
    expect(m.status(id)!.seconds).toBe(2.5);
  });

  it('is reproducible from the seed, and a different seed gives a different run', async () => {
    const m = manager();
    const a = (await finished(m, started(m))).result;
    const b = (await finished(m, started(m))).result;
    const c = (await finished(m, started(m, params({ seed: 12 })))).result;
    expect(b).toEqual(a);
    expect(c).not.toEqual(a);
  });

  it('picks a random seed for 0, reports it, and the same seed then repeats the run', async () => {
    const m = manager();
    const first = (await finished(m, started(m, params({ seed: 0 })))).result!;
    expect(first.seed).toBeGreaterThan(0);
    expect(first.seed).toBeLessThanOrEqual(2 ** 31 - 1);
    const again = (await finished(m, started(m, params({ seed: first.seed })))).result!;
    expect(again).toEqual(first);
  });

  it('runs pasted results as one session, with one curve point per shoe', async () => {
    const m = manager();
    const done = await finished(m, started(m, params({ source: 'paste', text: 'PPBPBB|PBPPT|BBBP', trials: 999, shoes: 999 })));
    expect(done).toMatchObject({ source: 'paste', total: 1, done: 1 });
    expect(done.result).toMatchObject({ trials: 1, shoes: 3 });
    expect(done.result!.curve.p50).toHaveLength(4);
    expect(done.result!.p10).toBe(done.result!.p90);
  });

  it('borrows the page settings without keeping the Telegram token', async () => {
    const c = controlled();
    const m = manager(c.executor);
    started(m, params(), { ...page, unit_value: '0.5', take_profit: '12' });
    expect(c.calls).toHaveLength(1);
    const { settings } = c.calls[0]!;
    expect(settings).toMatchObject({ unit_value: 0.5, take_profit: 12 });
    expect(settings.telegram_token).toBe('');
    expect(settings.telegram_chat_id).toBe('');
    expect(JSON.stringify(c.calls[0])).not.toContain('AAFakeToken');
    expect(base.unit_value).toBe(1); // the caller's settings were not touched
  });

  it('hands the executor exactly what was asked for, clamping the bankroll and the seed', () => {
    const c = controlled();
    const m = manager(c.executor);
    started(m, params({ shoes: '25', trials: '7', bankroll_units: '300', seed: 5_000_000_000 }));
    expect(c.calls[0]).toMatchObject({ trials: 7, shoes: { kind: 'sim', count: 25 }, bankrollUnits: 300, seed: 2 ** 31 - 1 });
    const m2 = manager(controlled().executor);
    expect(m2.start(base, page, params({ bankroll_units: -5 })).ok).toBe(true);
  });
});

describe('only one run at a time', () => {
  it('refuses a second start while one is running, and allows it afterwards', async () => {
    const c = controlled();
    const m = manager(c.executor);
    const id = started(m);
    expect(m.start(base, page, params())).toEqual({ ok: false, error: 'A backtest is already running' });
    c.resolve([]);
    await finished(m, id);
    expect(m.start(base, page, params()).ok).toBe(true);
  });
});

describe('progress', () => {
  it('adds up reported progress but never beyond the total', () => {
    const c = controlled();
    const m = manager(c.executor);
    const id = started(m, params({ trials: 10 }));
    c.progress(3);
    c.progress(4);
    expect(m.status(id)).toMatchObject({ done: 7, total: 10, finished: false });
    c.progress(50);
    expect(m.status(id)!.done).toBe(10);
  });
});

describe('cancelling', () => {
  it('stops the work, reports "Cancelled", and ignores a result that arrives afterwards', async () => {
    const c = controlled();
    const m = manager(c.executor);
    const id = started(m);
    expect(m.cancel(id)).toBe(true);
    expect(c.cancel).toHaveBeenCalledTimes(1);
    expect(m.status(id)).toMatchObject({ ok: false, finished: true, error: 'Cancelled' });
    c.resolve([]); // the late result must not turn the cancelled run into a finished one
    await new Promise((r) => setTimeout(r, 20));
    expect(m.status(id)).toMatchObject({ ok: false, error: 'Cancelled' });
    expect(m.status(id)!.result).toBeUndefined();
    expect(m.cancel(id)).toBe(false);
    expect(m.start(base, page, params()).ok).toBe(true);
  });

  it('cancels a run in progress on the calling thread', async () => {
    const m = manager();
    const id = started(m, params({ trials: 2000, shoes: 500 }));
    expect(m.cancel(id)).toBe(true);
    expect(m.status(id)).toMatchObject({ finished: true, error: 'Cancelled' });
  });

  it('says false for an unknown or finished run', async () => {
    const m = manager();
    expect(m.cancel('nope')).toBe(false);
    expect(m.cancel(undefined)).toBe(false);
    const id = started(m);
    await finished(m, id);
    expect(m.cancel(id)).toBe(false);
  });

  it('dispose() stops whatever is running', () => {
    const c = controlled();
    const m = manager(c.executor);
    const id = started(m);
    m.dispose();
    expect(c.cancel).toHaveBeenCalled();
    expect(m.status(id)!.error).toBe('Cancelled');
  });
});

describe('failures', () => {
  it('reports an executor that fails', async () => {
    const c = controlled();
    const m = manager(c.executor);
    const id = started(m);
    c.reject(new Error('worker crashed'));
    expect(await finished(m, id)).toMatchObject({ ok: false, finished: true, error: 'worker crashed' });
    expect(m.start(base, page, params()).ok).toBe(true);
  });

  it('reports an executor that cannot even start', () => {
    const m = manager(() => {
      throw new Error('no threads');
    });
    const id = started(m);
    expect(m.status(id)).toMatchObject({ ok: false, finished: true, error: 'Could not start the backtest: no threads' });
  });
});

describe('what is refused before anything runs', () => {
  const table: Array<[string, Record<string, unknown>, Record<string, unknown> | undefined, RegExp]> = [
    ['no shoes', { shoes: 0 }, undefined, /Shoes per session must be between 1 and 5000/],
    ['too many shoes', { shoes: LIMITS.maxShoes + 1 }, undefined, /Shoes per session must be between 1 and 5000/],
    ['no sessions', { trials: 0 }, undefined, /Sessions must be between 1 and 2000/],
    ['too many sessions', { trials: LIMITS.maxTrials + 1 }, undefined, /Sessions must be between 1 and 2000/],
    ['empty paste', { source: 'paste', text: 'xyz 123 -- !' }, undefined, /Paste P \/ B \/ T results to test/],
    ['a base unit of zero', {}, { unit_value: '0' }, /Base Unit must be above 0/],
    ['a progression that the selected strategy cannot use', {}, { strategy: 'MARTINGALE', progression: 'list', chain: [1, 2] }, /Martingale uses one Base Unit/],
  ];

  it.each(table)('refuses %s', (_name, p, s, message) => {
    const c = controlled();
    const m = manager(c.executor);
    const reply = m.start(base, { ...page, ...s }, params(p));
    expect(reply.ok).toBe(false);
    expect(reply).toMatchObject({ error: expect.stringMatching(message) });
    expect(c.calls).toHaveLength(0);
  });

  it('accepts the extremes the page allows', () => {
    const m = manager(controlled().executor);
    expect(m.start(base, page, params({ shoes: LIMITS.maxShoes, trials: LIMITS.maxTrials })).ok).toBe(true);
  });

  it('rejects an absurdly long paste', () => {
    const m = manager(controlled().executor);
    const reply = m.start(base, page, params({ source: 'paste', text: 'P'.repeat(LIMITS.maxPastedHands + 1) }));
    expect(reply).toMatchObject({ ok: false, error: expect.stringMatching(/At most 100,000 pasted hands/) });
  });
});

describe('bookkeeping', () => {
  it('answers null for unknown ids and keeps only the most recent finished runs', async () => {
    const m = manager();
    expect(m.status('nothing')).toBeNull();
    expect(m.status(undefined)).toBeNull();
    const ids: string[] = [];
    for (let i = 0; i < 8; i++) {
      const id = started(m, params({ trials: 1, shoes: 1 }));
      await finished(m, id);
      ids.push(id);
    }
    expect(m.status(ids[0]!)).toBeNull();
    expect(m.status(ids[7]!)).not.toBeNull();
    expect(ids.filter((id) => m.status(id) !== null).length).toBeLessThanOrEqual(6);
  });
});

describe('on real worker threads', () => {
  const run = async (executor: Executor, over: Record<string, unknown> = {}): Promise<StatusReply> => {
    const m = manager(executor);
    return finished(m, started(m, params({ trials: 7, shoes: 4, seed: 99, ...over })));
  };

  it('gives the same result whether 1 thread, 3 threads, or the calling thread played the sessions', async () => {
    const inline = await run(inlineExecutor());
    const one = await run(workerExecutor(1));
    const three = await run(workerExecutor(3));
    expect(inline.result!.trials).toBe(7);
    expect(one.result).toEqual(inline.result);
    expect(three.result).toEqual(inline.result);
    expect(three).toMatchObject({ done: 7, total: 7, ok: true });
  }, 90_000);

  it('plays pasted results the same way too', async () => {
    const over = { source: 'paste', text: 'PBBPPBPBBP|PPPBBPBP|BPBPBPPB' };
    expect((await run(workerExecutor(2), over)).result).toEqual((await run(inlineExecutor(), over)).result);
  }, 60_000);

  it('can be cancelled while the threads are busy, quickly, without leaving anything running', async () => {
    const m = manager(workerExecutor(2));
    const id = started(m, params({ trials: 2000, shoes: 2000 }));
    await new Promise((r) => setTimeout(r, 800));
    const midway = m.status(id)!;
    expect(midway.finished).toBe(false);
    const t0 = Date.now();
    expect(m.cancel(id)).toBe(true);
    expect(m.status(id)).toMatchObject({ finished: true, error: 'Cancelled' });
    expect(Date.now() - t0).toBeLessThan(2000);
    // Another run can start straight away.
    expect(m.start(base, page, params({ trials: 1, shoes: 1 })).ok).toBe(true);
  }, 60_000);

  it('reports a failure inside a worker instead of hanging', async () => {
    const spec: RunSpec = {
      settings: { ...base, strategy: 'MARTINGALE', progression: 'list', chain: [1, 2] }, seed: 1, trials: 2, shoes: { kind: 'sim', count: 1 }, bankrollUnits: 0,
    };
    await expect(workerExecutor(2)(spec, () => {}).done).rejects.toThrow(/Martingale uses one Base Unit/);
  }, 60_000);
});
