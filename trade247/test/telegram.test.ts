import { afterEach, describe, expect, it, vi } from 'vitest';
import { CHAT_ID_PATTERN, TOKEN_PATTERN, TelegramNotifier, configProblem, type TelegramConfig } from '../src/notify/telegram.js';
import { CHAT, TOKEN, fakeTelegram, type FakeTelegram } from './fakeTelegram.js';
import { MANUAL, cleanup, harness } from './helpers.js';

const fakes: FakeTelegram[] = [];
const fake = async (b?: Parameters<typeof fakeTelegram>[0]): Promise<FakeTelegram> => {
  const f = await fakeTelegram(b);
  fakes.push(f);
  return f;
};
afterEach(async () => {
  await cleanup();
  while (fakes.length) await fakes.pop()!.close();
});

const config: TelegramConfig = { token: TOKEN, chatId: CHAT };
const notifier = (f: FakeTelegram, over: Partial<ConstructorParameters<typeof TelegramNotifier>[1]> = {}, cfg: TelegramConfig = config) =>
  new TelegramNotifier(() => cfg, { baseUrl: f.baseUrl, minGapMs: 0, ...over });

describe('configuration checks', () => {
  it('accepts what Telegram issues and rejects anything that could change the request path', () => {
    expect(TOKEN_PATTERN.test(TOKEN)).toBe(true);
    // Same shape as a real token, including "-" and "_" in the secret part, but invented.
    expect(TOKEN_PATTERN.test('1234567890:AAH-fake0Fake1Fake-2fake3Fake_4fake5')).toBe(true);
    for (const bad of ['', 'abc', '123:short', `${TOKEN}/x`, `${TOKEN}?a=b`, `${TOKEN}#x`, ` ${TOKEN}`, `${TOKEN}\n`, '../../x:AAAAAAAAAAAAAAAAAAAAAAAA']) {
      expect(TOKEN_PATTERN.test(bad), JSON.stringify(bad)).toBe(false);
    }
    for (const ok of ['4242', '-1001234567890', '@my_channel']) expect(CHAT_ID_PATTERN.test(ok), ok).toBe(true);
    for (const bad of ['', 'abc', '12 34', '@a', '-', '1e5', '4242/..']) expect(CHAT_ID_PATTERN.test(bad), bad).toBe(false);
  });

  it('explains what is missing or wrong', () => {
    expect(configProblem({ token: '', chatId: '' })).toMatch(/Enter the bot token and the chat ID/);
    expect(configProblem({ token: TOKEN, chatId: '' })).toMatch(/Enter the bot token and the chat ID/);
    expect(configProblem({ token: 'nope', chatId: CHAT })).toMatch(/token does not look right/);
    expect(configProblem({ token: TOKEN, chatId: 'nope' })).toMatch(/chat ID must be a number/);
    expect(configProblem(config)).toBeNull();
  });
});

describe('sending', () => {
  it('posts the message to the bot endpoint as JSON, as plain text', async () => {
    const f = await fake();
    expect(await notifier(f).test()).toEqual({ ok: true });
    expect(f.hits).toHaveLength(1);
    const hit = f.hits[0]!;
    expect(hit.method).toBe('POST');
    expect(hit.url).toBe(`/bot${TOKEN}/sendMessage`);
    expect(hit.headers['content-type']).toBe('application/json');
    expect(hit.body).toMatchObject({ chat_id: CHAT, disable_web_page_preview: true });
    expect(hit.body.text).toMatch(/^🧪 SYSTEM · Test message\n/);
    expect(hit.body.text).toMatch(/📂 Category: .*\n⚙️ Action: .*\n🎯 Purpose: /);
    expect(hit.body.parse_mode).toBeUndefined();
  });

  it('truncates very long messages', async () => {
    const f = await fake();
    const n = notifier(f);
    n.notify('disconnected', 'x'.repeat(10_000));
    await n.idle();
    expect(f.texts()[0]!.length).toBe(4000);
  });

  it('sends nothing when alerts are not configured', async () => {
    const f = await fake();
    const n = notifier(f, {}, { token: '', chatId: '' });
    n.notify('daily_stop_loss', 'x');
    await n.idle();
    expect(f.hits).toHaveLength(0);
    expect(await n.test()).toMatchObject({ ok: false, error: expect.stringMatching(/Enter the bot token/) });
    expect(f.hits).toHaveLength(0);
  });

  it('does not contact Telegram for a malformed token', async () => {
    const f = await fake();
    const n = notifier(f, {}, { token: 'bad/../token', chatId: CHAT });
    expect((await n.test()).ok).toBe(false);
    expect(f.hits).toHaveLength(0);
  });
});

describe('failures are reported honestly and never leak the token', () => {
  it('reports a rejection with Telegram\'s own reason', async () => {
    const f = await fake('unauthorized');
    expect(await notifier(f).test()).toEqual({ ok: false, error: 'Telegram refused the message: Unauthorized' });
  });

  it('reports a non-JSON server error by status', async () => {
    const f = await fake('html500');
    expect(await notifier(f).test()).toEqual({ ok: false, error: 'Telegram refused the message: HTTP 500' });
  });

  it('redacts the token if the server echoes it back', async () => {
    const f = await fake('echo-token');
    const result = await notifier(f).test();
    expect(result.ok).toBe(false);
    expect(result.error).toContain('/bot***/sendMessage');
    expect(result.error).not.toContain(TOKEN);
    expect(result.error).not.toContain('AAFakeToken');
  });

  it('gives up when Telegram does not answer', async () => {
    const f = await fake('hang');
    const started = Date.now();
    expect(await notifier(f, { timeoutMs: 150 }).test()).toEqual({ ok: false, error: 'Telegram did not answer in time' });
    expect(Date.now() - started).toBeLessThan(3000);
  });

  it('reports an unreachable network without details that could contain the token', async () => {
    const f = await fake();
    const baseUrl = f.baseUrl;
    await f.close();
    fakes.pop();
    const result = await new TelegramNotifier(() => config, { baseUrl, minGapMs: 0 }).test();
    expect(result).toEqual({ ok: false, error: 'Telegram could not be reached (check the internet connection)' });
  });

  it('does not follow redirects', async () => {
    const f = await fake('redirect');
    const result = await notifier(f).test();
    expect(result.ok).toBe(false);
    expect(f.hits.map((h) => h.url)).toEqual([`/bot${TOKEN}/sendMessage`]);
  });
});

describe('the alert queue', () => {
  it('sends queued alerts one after another, in order, and calls onError for a failure', async () => {
    const f = await fake();
    const errors: string[] = [];
    const n = notifier(f, { onError: (m) => errors.push(m) });
    n.notify('disconnected', 'one');
    n.notify('daily_stop_loss', 'two');
    n.notify('daily_take_profit', 'three');
    await n.idle();
    expect(f.texts()).toEqual(['one', 'two', 'three']);
    expect(errors).toEqual([]);

    f.setBehavior('unauthorized');
    n.notify('disconnected', 'four');
    await n.idle();
    expect(errors).toEqual(['Telegram refused the message: Unauthorized']);
  });

  it('keeps one pending shoe-change alert instead of flooding the chat', async () => {
    const f = await fake('hang');
    const n = notifier(f, { timeoutMs: 200 });
    n.notify('disconnected', 'in flight'); // starts sending (and hangs)
    for (let i = 0; i < 10; i++) n.notify('shoe_change', `shoe ${i}`);
    f.setBehavior('ok');
    await n.idle();
    // The first request timed out; of the ten shoe changes only the first queued one survived.
    expect(f.texts()).toEqual(['in flight', 'shoe 0']);
  });

  it('never queues more than a bounded number of alerts, dropping shoe changes first', async () => {
    const f = await fake('hang');
    const n = notifier(f, { timeoutMs: 200 });
    n.notify('disconnected', 'first'); // in flight
    n.notify('shoe_change', 'shoe');
    for (let i = 0; i < 40; i++) n.notify('daily_stop_loss', `alert ${i}`);
    f.setBehavior('ok');
    await n.idle();
    const texts = f.texts();
    expect(texts).not.toContain('shoe');
    expect(texts.length).toBeLessThanOrEqual(31);
    expect(texts.at(-1)).toBe('alert 39');
  });

  it('waits the configured gap between messages', async () => {
    const f = await fake();
    const n = notifier(f, { minGapMs: 120 });
    const t0 = Date.now();
    n.notify('disconnected', 'a');
    n.notify('daily_stop_loss', 'b');
    await n.idle();
    expect(Date.now() - t0).toBeGreaterThanOrEqual(100);
    expect(f.texts()).toEqual(['a', 'b']);
  });
});

// ---------------------------------------------------------------------------------------------
// End to end: real sessions, real triggers, the fake API on the other end.

const BASE = { unit_value: 1, min_chip: 0, start_balance: 1000 };
const alertSettings = { telegram_token: TOKEN, telegram_chat_id: CHAT };

const run = async (h: ReturnType<typeof harness>, letters: string, extra: Record<string, unknown> = {}) => {
  const started = (await h.post('/api/start', { settings: { ...BASE, ...alertSettings, feed: 'csv', csv_text: letters, ...extra } })).json;
  expect(started.ok, started.status).toBe(true);
  await vi.waitFor(async () => expect((await h.get('/api/state')).json.running).toBe(false), { timeout: 5000, interval: 10 });
  await h.manager.flushAlerts();
};

describe('the four alert triggers', () => {
  it('daily stop loss: one alert saying the day is locked, with the session summary', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await run(h, 'BPPPP', { daily_stop_loss: 2 });
    const texts = f.texts();
    expect(texts).toHaveLength(1);
    expect(texts[0]).toMatch(/^🛑 RISK CONTROL · Daily stop loss hit\n/);
    expect(texts[0]).toMatch(/📂 Category: Risk control - daily loss limit/);
    expect(texts[0]).toMatch(/⚙️ Action: Session stopped\. The day is locked/);
    expect(texts[0]).toMatch(/🎯 Purpose: Caps the loss for the day/);
    expect(texts[0]).toMatch(/ℹ️ Detail: Daily stop loss reached \(-2\.00\)\. Today -2\.00\./);
    expect(texts[0]).toMatch(/📊 Session #1 · IHG · 2 hands/);
    expect(texts[0]).toMatch(/💵 Session P&L: -2\.00 \| Today: -2\.00\n💰 Balance: 998\.00/);
    expect(f.hits[0]!.body.chat_id).toBe(CHAT);
  });

  it('daily take profit: one alert', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await run(h, 'PPPP', { daily_take_profit: 2 });
    expect(f.texts()).toHaveLength(1);
    expect(f.texts()[0]).toMatch(/Daily take profit reached \(\+2\.00\)/);
    expect(f.texts()[0]).toMatch(/^🏁 PROFIT TARGET · Daily take profit hit/);
    expect(f.texts()[0]).toMatch(/Session P&L: \+2\.00 .*\n💰 Balance: 1002\.00/);
  });

  it('shoe change: one alert per new shoe, none for the very first shoe', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await run(h, 'PP|PP|PP');
    const texts = f.texts();
    expect(texts).toHaveLength(2);
    expect(texts[0]).toMatch(/^🔄 TABLE EVENT · New shoe/);
    expect(texts[0]).toMatch(/Detail: Shoe #2 starts\.[\s\S]*Session #1 · IHG · 2 hands/);
    expect(texts[1]).toMatch(/Shoe #3 starts\.[\s\S]*Session #1 · IHG · 4 hands/);
  });

  it('disconnection: alerts once per outage, shows the feed state, and recovers', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await h.post('/api/start', { settings: { ...MANUAL, ...alertSettings } });
    await h.post('/api/manual', { result: 'P' });

    expect(h.manager.feedLost('socket closed')).toBe(true);
    expect((await h.get('/api/state')).json.status).toBe('FEED LOST - socket closed');
    expect(h.manager.feedLost('still down')).toBe(true); // same outage
    await h.manager.flushAlerts();
    expect(f.texts()).toHaveLength(1);
    expect(f.texts()[0]).toMatch(/^⚠️ CONNECTION · Table connection lost/);
    expect(f.texts()[0]).toMatch(/Detail: socket closed[\s\S]*Session #1 · IHG · 1 hands[\s\S]*Session P&L: \+1\.00/);

    expect(h.manager.feedRestored()).toBe(true);
    expect((await h.get('/api/state')).json.status).toBe('RUNNING');
    expect(h.manager.feedRestored()).toBe(false);
    h.manager.feedLost('again');
    await h.manager.flushAlerts();
    // lost, restored, lost again
    expect(f.texts()).toHaveLength(3);
    expect(f.texts()[1]).toMatch(/^✅ CONNECTION · Table connection restored/);
  });

  it('does not alert for ordinary stops or when the session is not running', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    expect(h.manager.feedLost('x')).toBe(false);
    await run(h, 'PPPPP', { take_profit: 3, stop_loss: 9 });
    expect((await h.get('/api/state')).json.status).toBe('STOPPED (take profit)');
    expect(f.hits).toHaveLength(0);
  });

  it('sends nothing at all when no bot is configured', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await h.post('/api/start', { settings: { ...BASE, feed: 'csv', csv_text: 'BPPPP', daily_stop_loss: 2 } });
    await vi.waitFor(async () => expect((await h.get('/api/state')).json.running).toBe(false), { timeout: 5000 });
    await h.manager.flushAlerts();
    expect(f.hits).toHaveLength(0);
  });
});

describe('Send test and settings', () => {
  it('Send test uses the settings the page just posted', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    // The page posts its settings and then asks for the test, exactly like this.
    expect((await h.post('/api/settings', { ...alertSettings })).json.ok).toBe(true);
    expect((await h.post('/api/telegram_test')).json).toEqual({ ok: true });
    expect(f.texts()).toHaveLength(1);
    expect(f.hits[0]!.url).toBe(`/bot${TOKEN}/sendMessage`);
  });

  it('Send test works after the token was saved and the page only knows the mask', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await h.post('/api/settings', { ...alertSettings });
    expect((await h.get('/api/state')).json.settings.telegram_token).toBe('********');
    await h.post('/api/settings', { telegram_token: '********', telegram_chat_id: CHAT });
    expect((await h.post('/api/telegram_test')).json.ok).toBe(true);
    expect(f.hits[0]!.url).toBe(`/bot${TOKEN}/sendMessage`);
  });

  it('Send test says what is wrong', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    expect((await h.post('/api/telegram_test')).json.error).toMatch(/Enter the bot token and the chat ID/);
    await h.post('/api/settings', { telegram_token: 'garbage', telegram_chat_id: CHAT });
    expect((await h.post('/api/telegram_test')).json.error).toMatch(/token does not look right/);
    await h.post('/api/settings', { telegram_token: TOKEN, telegram_chat_id: CHAT });
    f.setBehavior('unauthorized');
    expect((await h.post('/api/telegram_test')).json).toEqual({ ok: false, error: 'Telegram refused the message: Unauthorized' });
    expect(f.hits).toHaveLength(1);
  });

  it('a failed alert is shown in the page without stopping the session, and never contains the token', async () => {
    const f = await fake('echo-token');
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await run(h, 'BPPPP', { daily_stop_loss: 2 });
    const state = (await h.get('/api/state')).json;
    expect(state.status).toBe('LOCKED (daily stop loss)');
    expect(state.error).toMatch(/^Alert not delivered - Telegram refused the message: bad request to \/bot\*\*\*\/sendMessage$/);
    expect(JSON.stringify(state)).not.toContain(TOKEN);
  });

  it('warns at Start when alerts are half set up, without blocking the session', async () => {
    const h = harness();
    const missingChat = (await h.post('/api/start', { settings: { ...MANUAL, telegram_token: TOKEN } })).json;
    expect(missingChat.ok).toBe(true);
    expect((await h.get('/api/state')).json.error).toBe('Telegram alerts are off: Enter the bot token and the chat ID first');
    await h.post('/api/stop');
    expect((await h.post('/api/start', { settings: { ...MANUAL, telegram_token: 'garbage', telegram_chat_id: CHAT } })).json.ok).toBe(true);
    expect((await h.get('/api/state')).json.error).toMatch(/token does not look right/);
    await h.post('/api/stop');
    await h.post('/api/start', { settings: { ...MANUAL, ...alertSettings } });
    expect((await h.get('/api/state')).json.error).toBe('');
  });

  it('keeps the token out of the CSV log and every report', async () => {
    const f = await fake();
    const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
    await run(h, 'PP', alertSettings);
    const csv = (await h.get('/api/report?fmt=csv')).text;
    const xlsx = (await h.get('/api/report?fmt=xlsx')).raw;
    for (const blob of [csv, xlsx.toString('latin1')]) {
      expect(blob).not.toContain(TOKEN);
      expect(blob).not.toContain('AAFakeToken');
    }
  });
});
