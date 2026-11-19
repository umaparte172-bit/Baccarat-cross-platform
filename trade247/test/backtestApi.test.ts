import { afterEach, describe, expect, it, vi } from 'vitest';
import { BacktestManager, type Executor, type RunSpec } from '../src/backtest/jobs.js';
import { MANUAL, cleanup, harness, type Harness } from './helpers.js';

afterEach(cleanup);

// What the page's collect() posts: strings for every form field, plus the Backtest window's own parameters.
const pageSettings = (over: Record<string, unknown> = {}) => ({
  strategy: 'IHG', unit_value: 1, progression: 'recovery', keepalive_amount: 0.1, keepalive_enabled: false,
  entry_loss_filter: 0, take_profit: 0, stop_loss: 0, min_chip: '0', start_balance: '1000',
  max_bet_mode: 'doc', max_bet_units: '45', hard_cap_mode: 'off', hard_cap_units: '200', daily_stop_loss: '0', daily_take_profit: '0',
  single_wins_mode: 'doc', single_wins_limit: '7', keepalive_idle: '10', round_step: '1',
  stop_consec_loss_count: '0', stop_consec_loss_action: 'STOP BET', pause_consec_loss_count: '0', pause_bet_count: '0',
  shoe_change_policy: 'reset_all', end_shoe_policy: 'off', end_shoe_hand: '60',
  feed: 'simulator', sim_interval: '1', sim_seed: '0', csv_text: '', csv_path: '', telegram_token: '', telegram_chat_id: '',
  ...over,
});
const pageParams = (over: Record<string, unknown> = {}) => ({ source: 'sim', text: '', shoes: 3, trials: 5, bankroll_units: 0, seed: 21, ...over });

const finished = async (h: Harness, id: string): Promise<any> =>
  vi.waitFor(async () => {
    const s = (await h.get(`/api/backtest/status?id=${id}`)).json;
    expect(s.finished).toBe(true);
    return s;
  }, { timeout: 20_000, interval: 10 });

describe('the Backtest endpoints, driven like the page drives them', () => {
  it('starts a run, polls it to the end, and returns everything the window draws', async () => {
    const h = harness();
    const reply = (await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams() })).json;
    expect(reply.ok).toBe(true);
    expect(reply.id).toMatch(/^[0-9a-f]{16}$/);

    const s = await finished(h, reply.id);
    expect(s).toMatchObject({ ok: true, source: 'sim', total: 5, done: 5, finished: true });
    expect(typeof s.seconds).toBe('number');
    const r = s.result;
    expect(r).toMatchObject({ trials: 5, shoes: 3, seed: 21, unit_value: 1, bankroll_units: 0 });
    for (const key of ['mean', 'median', 'p10', 'p90', 'best', 'worst', 'profitable_pct', 'ruin_pct', 'return_on_turnover_pct', 'mdd_avg', 'mdd_worst', 'max_stake_avg', 'max_stake_worst', 'turnover_avg', 'real_avg', 'virtual_avg', 'runs_closed_avg', 'runs_cut_avg']) {
      expect(typeof r[key], key).toBe('number');
    }
    expect(Object.values(r.stops).reduce((a: number, b) => a + (b as number), 0)).toBe(5);
    expect(r.hist.reduce((a: number, b: any) => a + b.n, 0)).toBe(5);
    expect(r.curve.p10).toHaveLength(4);
    expect(r.curve.p50[0]).toBe(0);
  });

  it('is the same run for the same seed and settings', async () => {
    const h = harness();
    const run = async () => (await finished(h, (await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams() })).json.id)).result;
    expect(await run()).toEqual(await run());
  });

  it('applies the settings on the page, so a take profit changes how sessions end', async () => {
    const h = harness();
    const id = (await h.post('/api/backtest/start', { settings: pageSettings({ take_profit: 1, stop_loss: 1 }), params: pageParams({ trials: 20, shoes: 50 }) })).json.id;
    const r = (await finished(h, id)).result;
    expect(Object.keys(r.stops).sort()).toEqual(['stop loss', 'take profit']);
    expect(r.best).toBeGreaterThanOrEqual(0);
    expect(r.worst).toBeLessThanOrEqual(0);
    expect(r.mdd_worst).toBeLessThan(5);
  });

  it('counts sessions wiped out when a bankroll is set', async () => {
    const h = harness();
    const id = (await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams({ trials: 30, shoes: 100, bankroll_units: 20 }) })).json.id;
    const r = (await finished(h, id)).result;
    expect(r.bankroll_units).toBe(20);
    expect(r.ruin_pct).toBeGreaterThan(0);
    expect(r.stops['wiped out']).toBe(Math.round((r.ruin_pct / 100) * 30));
    expect(r.worst).toBeLessThanOrEqual(-20);
  });

  it('runs pasted results', async () => {
    const h = harness();
    const id = (await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams({ source: 'paste', text: 'PBBPPBPBBP|PPPBBPBP' }) })).json.id;
    const s = await finished(h, id);
    expect(s).toMatchObject({ source: 'paste', total: 1 });
    expect(s.result).toMatchObject({ trials: 1, shoes: 2 });
  });

  it('answers a refusal the way the window expects ({ ok: false, error })', async () => {
    const h = harness();
    expect((await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams({ shoes: 0 }) })).json)
      .toEqual({ ok: false, error: 'Shoes per session must be between 1 and 5000' });
    expect((await h.post('/api/backtest/start', { settings: pageSettings({ strategy: 'MARTINGALE', progression: 'list', chain: [1, 2] }), params: pageParams() })).json)
      .toEqual({ ok: false, error: 'Martingale uses one Base Unit - choose Auto Progression (Manual Sequence Input is for Sequence and the ODD strategies)' });
    expect((await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams({ source: 'paste', text: '' }) })).json.ok).toBe(false);
    expect((await h.post('/api/backtest/start', {})).json.ok).toBe(true); // empty request: defaults (500 shoes x 100 sessions)
  });

  it('answers an unknown run id as finished with an error, so the window stops polling', async () => {
    const h = harness();
    expect((await h.get('/api/backtest/status?id=nope')).json).toEqual({ ok: false, finished: true, done: 0, total: 1, seconds: 0, error: 'Unknown backtest' });
    expect((await h.get('/api/backtest/status')).json.finished).toBe(true);
  });

  it('cancels through the API', async () => {
    const h = harness();
    const id = (await h.post('/api/backtest/start', { settings: pageSettings(), params: pageParams({ trials: 2000, shoes: 2000 }) })).json.id;
    expect((await h.post('/api/backtest/cancel', { id })).json).toEqual({ ok: true });
    expect((await h.get(`/api/backtest/status?id=${id}`)).json).toMatchObject({ ok: false, finished: true, error: 'Cancelled' });
    expect((await h.post('/api/backtest/cancel', { id })).json).toEqual({ ok: false });
    expect((await h.post('/api/backtest/cancel', {})).json).toEqual({ ok: false });
  });

  it('needs the same protections as every other endpoint', async () => {
    const h = harness();
    const r = await h.app.inject({ method: 'POST', url: '/api/backtest/start', payload: '{}', headers: { host: '127.0.0.1:8247', 'content-type': 'application/json' } });
    expect(r.statusCode).toBe(403);
  });
});

describe('a backtest never disturbs a live session', () => {
  it('leaves the session, its settings and its money exactly as they were', async () => {
    const h = harness();
    await h.post('/api/start', { settings: MANUAL });
    await h.post('/api/manual', { result: 'P' });
    await h.post('/api/manual', { result: 'B' });
    const before = (await h.get('/api/state?since=0')).json;

    const id = (await h.post('/api/backtest/start', { settings: pageSettings({ unit_value: 5, take_profit: 3 }), params: pageParams({ trials: 40, shoes: 20 }) })).json.id;
    await finished(h, id);

    const after = (await h.get('/api/state?since=0')).json;
    const strip = ({ now_ts, ...rest }: any) => rest;
    expect(strip(after)).toEqual(strip(before));
    expect(after.settings.unit_value).toBe(1);
    expect(after.settings.take_profit).toBe(0);

    // And the session carries on normally.
    await h.post('/api/manual', { result: 'P' });
    expect((await h.get('/api/state')).json.hands).toBe(3);
  });

  it('does not save the settings it borrowed', async () => {
    const h = harness();
    await h.post('/api/backtest/start', { settings: pageSettings({ unit_value: 9 }), params: pageParams() });
    const dir = h.dir;
    await h.close();
    expect(harness(dir).manager.currentSettings().unit_value).not.toBe(9);
  });

  it('never hands the Telegram token to the workers', async () => {
    const seen: RunSpec[] = [];
    const spy: Executor = (spec) => {
      seen.push(spec);
      return { done: new Promise(() => {}), cancel() {} };
    };
    const h = harness({ backtest: new BacktestManager(spy) });
    await h.post('/api/settings', { telegram_token: '123456789:AAFakeTokenForTestsOnly_0123456789', telegram_chat_id: '42' });
    await h.post('/api/backtest/start', { settings: pageSettings({ telegram_token: '123456789:AAFakeTokenForTestsOnly_0123456789' }), params: pageParams() });
    expect(seen).toHaveLength(1);
    expect(JSON.stringify(seen[0])).not.toContain('AAFakeToken');
  });
});
