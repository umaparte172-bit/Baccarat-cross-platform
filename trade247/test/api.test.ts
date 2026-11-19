import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { runInNewContext } from 'node:vm';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PORT, HOST, MANUAL, cleanup, harness } from './helpers.js';

afterEach(cleanup);

describe('practice capital', () => {
  it('stops before an unaffordable practice stake and retains the last settled balance', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...MANUAL, strategy: 'MARTINGALE', unit_value: 1, start_balance: 2, paper_bankroll_guard: true } });
    await h.post('/api/manual', { result: 'B' });
    await h.post('/api/manual', { result: 'P' });
    const state = (await h.get('/api/state')).json;
    expect(state).toMatchObject({ running: false, hands: 1, balance: 1, status: 'STOPPED (SHORT OF STAKE)' });
  });
});

const start = (h: ReturnType<typeof harness>, settings: Record<string, unknown> = MANUAL) =>
  h.post('/api/start', { settings });

describe('security', () => {
  it('serves the page without the API header but with hardening headers', async () => {
    const h = harness();
    const r = await h.app.inject({ method: 'GET', url: '/', headers: { host: HOST } });
    expect(r.statusCode).toBe(200);
    expect(r.headers['content-type']).toContain('text/html');
    expect(r.headers['content-security-policy']).toContain("frame-ancestors 'none'");
    expect(r.body).toContain('TRADE247-V1');
  });

  it('rejects API calls without the X-T247 header', async () => {
    const h = harness();
    const r = await h.app.inject({ method: 'GET', url: '/api/state', headers: { host: HOST } });
    expect(r.statusCode).toBe(403);
  });

  it('rejects unknown Host headers (DNS rebinding)', async () => {
    const h = harness();
    const r = await h.app.inject({ method: 'GET', url: '/api/state', headers: { host: `evil.example:${PORT}`, 'x-t247': '1' } });
    expect(r.statusCode).toBe(403);
    const wrongPort = await h.app.inject({ method: 'GET', url: '/', headers: { host: '127.0.0.1:9999' } });
    expect(wrongPort.statusCode).toBe(403);
  });

  it('rejects requests from another origin', async () => {
    const h = harness();
    const r = await h.app.inject({
      method: 'POST', url: '/api/stop', payload: '{}',
      headers: { host: HOST, origin: 'https://evil.example', 'x-t247': '1', 'content-type': 'application/json' },
    });
    expect(r.statusCode).toBe(403);
    const same = await h.app.inject({
      method: 'GET', url: '/api/state', headers: { host: HOST, origin: `http://${HOST}`, 'x-t247': '1' },
    });
    expect(same.statusCode).toBe(200);
  });

  it('does not leak internals on malformed JSON', async () => {
    const h = harness();
    const r = await h.app.inject({
      method: 'POST', url: '/api/start', payload: '{not json',
      headers: { host: HOST, 'x-t247': '1', 'content-type': 'application/json' },
    });
    expect(r.statusCode).toBe(400);
    expect(JSON.parse(r.body)).toEqual({ ok: false, error: 'Bad request' });
  });
});

describe('page contract', () => {
  it('implements every /api route the page calls', async () => {
    const h = harness();
    const page = readFileSync(new URL('../ui/index.html', import.meta.url), 'utf8');
    const paths = [...new Set(page.match(/\/api\/[a-z_]+(?:\/[a-z_]+)?/g) ?? [])];
    expect(paths.length).toBeGreaterThan(10);
    for (const path of paths) {
      const known = h.app.hasRoute({ method: 'GET', url: path }) || h.app.hasRoute({ method: 'POST', url: path });
      expect(known, `the page calls ${path} but the server has no such route`).toBe(true);
    }
  });

  it('uses English number formatting', () => {
    const page = readFileSync(new URL('../ui/index.html', import.meta.url), 'utf8');
    const formatter = page.match(/^const fm = .+$/m)![0];
    expect(runInNewContext(formatter + ';fm(1234.56)')).toBe('1,234.56');
    expect(runInNewContext(formatter + ';fm(1000.095)')).toBe('1,000.095');
    expect(runInNewContext(formatter + ';fm(-0.095)')).toBe('-0.095');
  });
});

describe('manual session', () => {
  it('starts, records hands with exact accounting, pauses, stops, and resets', async () => {
    const h = harness();
    expect((await h.get('/api/state?since=0')).json).toMatchObject({ running: false, status: 'IDLE', hands: 0, session: 0 });

    const started = await start(h);
    expect(started.json).toEqual({ ok: true, status: 'RUNNING' });
    let state = (await h.get('/api/state?since=0')).json;
    expect(state).toMatchObject({ running: true, paused: false, status: 'RUNNING', balance: 1000, pnl: 0 });
    expect(state.next).toMatchObject({ mode: 'REAL', pick: 'PLAYER', units: 1, stake: 1 });
    expect(state.session_no).toBe(1);
    expect(state.started_ts).toBeGreaterThan(0);

    for (const result of ['P', 'P', 'B', 'T']) expect((await h.post('/api/manual', { result })).json.ok).toBe(true);
    state = (await h.get('/api/state?since=0')).json;
    expect(state.rows.map((r: any) => r.outcome)).toEqual(['WIN', 'WIN', 'LOSS', 'TIE']);
    expect(state.rows.map((r: any) => r.session_pnl)).toEqual(['1.00', '2.00', '1.00', '1.00']);
    expect(state).toMatchObject({ hands: 4, pnl: 1, balance: 1001, total_rows: 4, win_rate: 66.7, virtual_hands: 0, max_drawdown: 1 });
    expect(state.rows[0]).toMatchObject({ n: 1, pick: 'P', mode: 'REAL', stake: '1.00', result: 'P', cards: null, session: 1 });
    expect(state.rows[0].time).toMatch(/^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d$/);

    const incremental = (await h.get('/api/state?since=2')).json;
    expect(incremental.rows.map((r: any) => r.n)).toEqual([3, 4]);
    expect(incremental.total_rows).toBe(4);
    expect((await h.get('/api/state?since=99')).json.rows).toHaveLength(4);

    expect((await h.post('/api/pause')).json.ok).toBe(true);
    state = (await h.get('/api/state?since=4')).json;
    expect(state).toMatchObject({ paused: true, status: 'PAUSED' });
    expect(state.next).toMatchObject({ mode: 'VIRTUAL' });
    await h.post('/api/manual', { result: 'P' });
    state = (await h.get('/api/state?since=4')).json;
    expect(state.rows[0]).toMatchObject({ mode: 'VIRTUAL', outcome: 'V-WIN', stake: '0.00', session_pnl: '1.00' });
    expect(state).toMatchObject({ pnl: 1, virtual_hands: 1 });

    expect((await h.post('/api/resume')).json.ok).toBe(true);
    expect((await h.get('/api/state?since=5')).json).toMatchObject({ paused: false, status: 'RUNNING' });

    expect((await h.post('/api/reset')).json).toEqual({ ok: false });

    await h.post('/api/stop');
    state = (await h.get('/api/state?since=5')).json;
    expect(state).toMatchObject({ running: false, status: 'STOPPED', next: null });
    expect(state.stopped_ts).toBeGreaterThanOrEqual(state.started_ts);
    expect((await h.post('/api/manual', { result: 'P' })).json.ok).toBe(false);

    const reset = (await h.post('/api/reset')).json;
    expect(reset.ok).toBe(true);
    expect(reset.saved).toContain('trade247_session_history.csv');
    expect((await h.get('/api/state?since=0')).json).toMatchObject({ hands: 0, session: 0, status: 'IDLE', balance: 1000, rows: [] });
  });

  it('rejects a second start and invalid settings', async () => {
    const h = harness();
    expect((await start(h, { ...MANUAL, unit_value: 0 })).json).toEqual({ ok: false, status: 'Base Unit must be above 0' });
    expect((await start(h)).json.ok).toBe(true);
    expect((await start(h)).json).toEqual({ ok: false, status: 'A session is already running' });
  });

  it('rejects bad manual input and manual input on other feeds', async () => {
    const h = harness();
    expect((await h.post('/api/manual', { result: 'P' })).json.ok).toBe(false);
    await start(h);
    expect((await h.post('/api/manual', { result: 'X' })).json).toEqual({ ok: false, error: 'Result must be P, B or T' });
    await h.post('/api/stop');
    await start(h, { ...MANUAL, feed: 'simulator', sim_interval: 3600 });
    expect((await h.post('/api/manual', { result: 'P' })).json).toEqual({ ok: false, error: 'The Manual feed is not selected' });
  });

  it('applies settings to a running session but locks the feed and the bankroll', async () => {
    const h = harness();
    await start(h);
    const applied = (await h.post('/api/apply', { unit_value: '2', feed: 'simulator', start_balance: '5' })).json;
    expect(applied).toEqual({ ok: true, live: true });
    const state = (await h.get('/api/state?since=0')).json;
    expect(state.settings).toMatchObject({ unit_value: 2, feed: 'manual', start_balance: 1000 });
    expect(state.next.stake).toBe(2);
    await h.post('/api/stop');
    expect((await h.post('/api/apply', { unit_value: '3' })).json).toEqual({ ok: true, live: false });
  });

  it('reports a rejected apply through the error field', async () => {
    const h = harness();
    expect((await h.post('/api/apply', { unit_value: 0 })).json).toEqual({ ok: false, live: false });
    expect((await h.get('/api/state')).json.error).toBe('Base Unit must be above 0');
  });

  it('retry only works on a running session', async () => {
    const h = harness();
    expect((await h.post('/api/retry')).json.ok).toBe(false);
    await start(h);
    expect((await h.post('/api/retry')).json.ok).toBe(true);
  });
});

describe('feeds', () => {
  const waitForHands = async (h: ReturnType<typeof harness>, n: number) =>
    vi.waitFor(async () => {
      const s = (await h.get('/api/state?since=0')).json;
      expect(s.hands).toBeGreaterThanOrEqual(n);
      return s;
    }, { timeout: 10_000, interval: 25 });

  it('the simulator is reproducible by seed and deals real cards', async () => {
    const results: string[][] = [];
    for (let i = 0; i < 2; i++) {
      const h = harness();
      await start(h, { feed: 'simulator', sim_interval: 0, sim_seed: 42, unit_value: 1, min_chip: 0 });
      const s = await waitForHands(h, 40);
      await h.post('/api/stop');
      const stopped = (await h.get('/api/state?since=0')).json;
      results.push(stopped.rows.slice(0, 40).map((r: any) => r.result));
      for (const row of s.rows.slice(0, 40)) {
        const [p, b] = row.cards;
        const score = (c: number[]) => c.reduce((a: number, x: number) => a + x, 0) % 10;
        expect(row.result).toBe(score(p) === score(b) ? 'T' : score(p) > score(b) ? 'P' : 'B');
      }
      await h.close();
    }
    expect(results[0]).toEqual(results[1]);
  });

  it('stops by itself when a replay finishes', async () => {
    const h = harness();
    await start(h, { feed: 'csv', csv_text: 'PBPT|BB', unit_value: 1, min_chip: 0 });
    await vi.waitFor(async () => {
      expect((await h.get('/api/state')).json.status).toBe('STOPPED (replay finished)');
    }, { timeout: 5000 });
    const s = (await h.get('/api/state?since=0')).json;
    expect(s.rows.map((r: any) => r.result)).toEqual(['P', 'B', 'P', 'T', 'B', 'B']);
    expect(s.running).toBe(false);
  });

  it('refuses an empty replay and explains the file-path limitation', async () => {
    const h = harness();
    expect((await start(h, { feed: 'csv', csv_text: '' })).json).toEqual({ ok: false, status: 'Paste P / B / T results to replay' });
    const r = (await start(h, { feed: 'csv', csv_text: '', csv_path: '/tmp/x.csv' })).json;
    expect(r.ok).toBe(false);
    expect(r.status).toMatch(/not available yet/);
  });
});

describe('reports and storage', () => {
  it('writes the history file per hand and serves the same data as CSV', async () => {
    const h = harness();
    expect((await h.post('/api/report/open', { fmt: 'csv' })).json).toEqual({ ok: false, error: 'No log yet - press START first' });
    expect((await h.get('/api/report?fmt=csv')).status).toBe(404);

    await start(h);
    await h.post('/api/manual', { result: 'P' });
    await h.post('/api/manual', { result: 'B' });

    const open = (await h.post('/api/report/open', { fmt: 'csv' })).json;
    expect(open).toMatchObject({ ok: true, opened: false });
    const csv = await h.get('/api/report?fmt=csv');
    expect(csv.headers['content-disposition']).toContain('trade247_report.csv');
    const lines = csv.text.replace(/^\uFEFF/, '').trim().split(/\r\n/);
    expect(lines[0]).toBe('Time,Session,Hand,Strategy,Bet,Mode,Stake,Winner,Outcome,Rule,PnL,SessionPnL,Balance');
    expect(lines).toHaveLength(3);
    expect(lines[1]).toMatch(/,1,1,IHG,PLAYER,REAL,1\.00,PLAYER,WIN,Follow · run closed \+1u,1\.00,1\.00,1001\.00$/);
    expect(lines[2]).toMatch(/,1,2,IHG,PLAYER,REAL,1\.00,BANKER,LOSS,Chop,-1\.00,0\.00,1000\.00$/);

    const history = readFileSync(join(h.dir, 'logs', 'trade247_session_history.csv'), 'utf8').trim().split(/\r\n/);
    expect(history).toEqual(lines);
  });

  it('rejects unknown formats and reports an empty log for both real ones', async () => {
    const h = harness();
    expect((await h.post('/api/report/open', { fmt: 'xlsx' })).json).toEqual({ ok: false, error: 'No log yet - press START first' });
    expect((await h.post('/api/report/open', { fmt: 'csv' })).json).toEqual({ ok: false, error: 'No log yet - press START first' });
    expect((await h.post('/api/report/open', { fmt: 'pdf' })).json).toEqual({ ok: false, error: 'Unknown report format' });
  });

  it('keeps the Telegram token out of the page, the settings file and the state', async () => {
    const h = harness();
    await start(h, { ...MANUAL, telegram_token: 'secret-token-123', telegram_chat_id: '42' });
    const state = await h.get('/api/state?since=0');
    expect(state.json.settings.telegram_token).toBe('********');
    expect(state.text).not.toContain('secret-token-123');

    const settingsFile = readFileSync(join(h.dir, 'settings.json'), 'utf8');
    expect(settingsFile).not.toContain('secret-token-123');
    expect(JSON.parse(readFileSync(join(h.dir, 'secrets.json'), 'utf8'))).toEqual({ telegram_token: 'secret-token-123' });

    await h.post('/api/stop');
    await h.post('/api/apply', { telegram_token: '********', unit_value: 1 });
    expect(JSON.parse(readFileSync(join(h.dir, 'secrets.json'), 'utf8')).telegram_token).toBe('secret-token-123');
    await h.post('/api/apply', { telegram_token: '' });
    expect(JSON.parse(readFileSync(join(h.dir, 'secrets.json'), 'utf8')).telegram_token).toBe('');
  });

  it('remembers settings and the session counter across restarts', async () => {
    const first = harness();
    await start(first, { ...MANUAL, unit_value: 0.25 });
    await first.post('/api/stop');
    const dir = first.dir;
    await first.close();

    const second = harness(dir);
    const state = (await second.get('/api/state')).json;
    expect(state.settings).toMatchObject({ unit_value: 0.25, feed: 'manual' });
    await start(second, { ...MANUAL, unit_value: 0.25 });
    expect((await second.get('/api/state')).json.session_no).toBe(2);
    expect(existsSync(join(dir, 'meta.json'))).toBe(true);
  });
});

describe('IHG through the API', () => {
  const feed = async (h: ReturnType<typeof harness>, letters: string) => {
    for (const result of letters) expect((await h.post('/api/manual', { result })).json.ok).toBe(true);
    return (await h.get('/api/state?since=0')).json;
  };

  it('recovers with ceil(deficit) + 1 units and reports the strategy state', async () => {
    const h = harness();
    await start(h);
    // Player bet loses (run -1), then the Banker bet wins 0.95 (run -0.05): next stake is 2 units.
    const state = await feed(h, 'BB');
    expect(state.rows.map((r: any) => [r.pick, r.outcome, r.pattern])).toEqual([['P', 'LOSS', 'Follow'], ['B', 'WIN', 'Streak']]);
    expect(state.rows[1]).toMatchObject({ run_net_units: -0.05, session_pnl: '-0.05' });
    expect(state.next).toMatchObject({ mode: 'REAL', pick: 'BANKER', units: 2, stake: 2, final: false, pattern: 'Streak' });
    expect(state.strategy_state).toMatchObject({ run_net_units: -0.05, single_wins: 0, virtual_mode: false, run_open: true });
  });

  it('switches to virtual bets at the single-win limit and resumes after two virtual wins', async () => {
    const h = harness();
    await start(h, { ...MANUAL, single_wins_mode: 'custom', single_wins_limit: 1 });
    // L, W, then a loss that follows a win: one single win reaches the limit.
    let state = await feed(h, 'BBP');
    expect(state.rows[2].note).toMatch(/single-win limit reached/);
    expect(state.next.mode).toBe('VIRTUAL');
    expect(state.strategy_state).toMatchObject({ virtual_mode: true, single_wins: 1 });

    const pnlBefore = state.pnl;
    for (let i = 0; i < 2; i++) {
      state = (await h.get('/api/state?since=0')).json;
      await h.post('/api/manual', { result: state.next.pick[0] });
    }
    state = (await h.get('/api/state?since=0')).json;
    expect(state.rows.slice(3).map((r: any) => [r.mode, r.outcome, r.stake])).toEqual([['VIRTUAL', 'V-WIN', '0.00'], ['VIRTUAL', 'V-WIN', '0.00']]);
    expect(state.pnl).toBe(pnlBefore);
    expect(state.next).toMatchObject({ mode: 'REAL', units: 1 });
    expect(state.virtual_hands).toBe(2);
  });

  it('cuts the open run at a new shoe by default, and carries it with the "continue" policy', async () => {
    const run = async (policy: string) => {
      const h = harness();
      await start(h, { feed: 'csv', csv_text: 'PB|B', unit_value: 1, min_chip: 0, shoe_change_policy: policy });
      await vi.waitFor(async () => expect((await h.get('/api/state')).json.running).toBe(false), { timeout: 5000 });
      return (await h.get('/api/state?since=0')).json.rows;
    };
    const reset = await run('reset_all');
    const keep = await run('continue');
    // Hand 2 loses 1 unit. In the new shoe, "reset" starts clean (-1); "continue" keeps the debt (-2).
    expect(reset[2]).toMatchObject({ pattern: 'Follow', run_net_units: -1 });
    expect(keep[2]).toMatchObject({ pattern: 'Streak', run_net_units: -2 });
  });

  it('applies a changed limit to the running session', async () => {
    const h = harness();
    await start(h);
    expect((await h.post('/api/apply', { max_bet_mode: 'custom', max_bet_units: 2 })).json).toEqual({ ok: true, live: true });
    await feed(h, 'BB'); // next stake is 2 units, which is now the run-end stake
    expect((await h.get('/api/state')).json.next).toMatchObject({ units: 2, final: true });
  });
});

describe('client-reference strategy settings', () => {
  const supported: Array<[string, Record<string, unknown>]> = [
    ['AME-AI', { strategy: 'AME' }],
    ['JJ', { strategy: 'JJ' }],
    ['IHG manual sequence', { progression: 'list', chain: [1, 2, 3] }],
    ['IHG virtual entry filter', { entry_loss_filter: 3 }],
  ];

  it.each(supported)('starts with %s', async (_name, extra) => {
    const h = harness();
    const reply = (await start(h, { ...MANUAL, ...extra })).json;
    expect(reply).toMatchObject({ ok: true, status: 'RUNNING' });
    expect((await h.get('/api/state')).json).toMatchObject({ running: true, hands: 0 });
  });

  it('applies the IHG entry filter to a running session', async () => {
    const h = harness();
    await start(h);
    expect((await h.post('/api/apply', { entry_loss_filter: 3 })).json).toEqual({ ok: true, live: true });
    const state = (await h.get('/api/state')).json;
    expect(state.error).toBe('');
    expect(state.settings.entry_loss_filter).toBe(3);
    expect(state.running).toBe(true);
  });

  it('can select and start AME while idle', async () => {
    const h = harness();
    expect((await h.post('/api/apply', { strategy: 'AME' })).json).toEqual({ ok: true, live: false });
    expect((await start(h, MANUAL)).json.status).toBe('RUNNING');
  });
});
