import { afterEach, describe, expect, it, vi } from 'vitest';
import { COMMANDS, helpText } from '../src/notify/control.js';
import { ALERTS, formatAlert, shoeAction } from '../src/notify/messages.js';
import { CHAT, TOKEN, fakeTelegram, type FakeTelegram } from './fakeTelegram.js';
import { MANUAL, cleanup, harness } from './helpers.js';

const fakes: FakeTelegram[] = [];
afterEach(async () => {
  await cleanup();
  while (fakes.length) await fakes.pop()!.close();
});

const now = (): number => Math.floor(Date.now() / 1000);
const message = (text: string, chat = Number(CHAT), date = now()) => ({ message: { message_id: 1, date, chat: { id: chat }, text } });
const button = (data: string, chat = Number(CHAT)) => ({
  callback_query: { id: `cb${Math.random()}`, data, message: { chat: { id: chat }, date: now() } },
});

async function setup(extra: Record<string, unknown> = {}) {
  const f = await fakeTelegram();
  fakes.push(f);
  const h = harness({ app: { telegram: { baseUrl: f.baseUrl } } });
  const saved = await h.post('/api/settings', { ...MANUAL, telegram_token: TOKEN, telegram_chat_id: CHAT, telegram_control: 'on', ...extra });
  expect(saved.json.ok).toBe(true);
  await vi.waitFor(() => expect(f.hits.some((x) => x.url.endsWith('/getUpdates'))).toBe(true), { timeout: 3000 });
  const replies = (): Array<{ text: string; keyboard?: any }> =>
    f.hits.filter((x) => x.url.endsWith('/sendMessage')).map((x) => ({ text: String(x.body.text), keyboard: x.body.reply_markup }));
  const waitReply = async (re: RegExp) => {
    let found: { text: string; keyboard?: any } | undefined;
    await vi.waitFor(() => {
      found = replies().find((r) => re.test(r.text));
      expect(found, `no reply matching ${re}; got:\n${replies().map((r) => r.text).join('\n---\n')}`).toBeTruthy();
    }, { timeout: 3000, interval: 20 });
    return found!;
  };
  return { f, h, replies, waitReply };
}

describe('message layout', () => {
  it('every alert has a category, an action and a purpose', () => {
    for (const [kind, spec] of Object.entries(ALERTS)) {
      expect(spec.categoryText, kind).not.toBe('');
      expect(spec.purpose, kind).not.toBe('');
      if (kind !== 'shoe_change') expect(spec.action, kind).not.toBe('');
    }
    const text = formatAlert('daily_stop_loss', 'Daily stop loss reached (-2.00)', null);
    expect(text.split('\n').slice(0, 5)).toEqual([
      '🛑 RISK CONTROL · Daily stop loss hit',
      '━━━━━━━━━━━━━━━━',
      '📂 Category: Risk control - daily loss limit',
      expect.stringMatching(/^⚙️ Action: Session stopped/),
      expect.stringMatching(/^🎯 Purpose: /),
    ]);
  });

  it('the shoe-change action says what the setting actually does', () => {
    expect(shoeAction('reset_all')).toMatch(/cut.*base unit/);
    expect(shoeAction('reset_pattern')).toMatch(/sequence is kept/);
    expect(shoeAction('continue')).toMatch(/Nothing reset/);
  });

  it('the help lists every command with its category, action and purpose', () => {
    const help = helpText();
    for (const c of COMMANDS) expect(help).toContain(`/${c.command} - ${c.action} → ${c.purpose}`);
    for (const cat of ['MONITOR', 'SESSION', 'BETTING', 'TABLE', 'REPORTS']) expect(help).toContain(cat);
  });
});

describe('remote control over Telegram', () => {
  it('registers the command menu and answers /help and /status from the configured chat', async () => {
    const { f, waitReply } = await setup();
    const menu = f.hits.find((x) => x.url.endsWith('/setMyCommands'));
    expect(menu?.body.commands.map((c: any) => c.command)).toEqual(COMMANDS.map((c) => c.command));
    expect(menu?.url).toBe(`/bot${TOKEN}/setMyCommands`);

    f.pushUpdate(message('/help'));
    await waitReply(/Remote control/);
    f.pushUpdate(message('/status@trade247_bot'));
    const status = await waitReply(/MONITOR · Status/);
    expect(status.text).toMatch(/no session running/);
    expect(status.keyboard.inline_keyboard.flat().map((b: any) => b.callback_data)).toEqual(['status', 'pause', 'resume', 'stop', 'report']);
  });

  it('ignores every other chat and commands sent before the program started', async () => {
    const { f, h, replies } = await setup();
    f.pushUpdate(message('/start', 999));
    f.pushUpdate(button('confirmstart', 999));
    f.pushUpdate(message('/stop', Number(CHAT), now() - 3600));
    f.pushUpdate(message('/status'));
    await vi.waitFor(() => expect(replies().length).toBe(1), { timeout: 3000 });
    await new Promise((r) => setTimeout(r, 150));
    expect(replies()).toHaveLength(1);
    expect(replies()[0]!.text).toMatch(/Status/);
    expect((await h.get('/api/state')).json.running).toBe(false);
    // The ignored button press was not even acknowledged.
    expect(f.hits.filter((x) => x.url.endsWith('/answerCallbackQuery'))).toHaveLength(0);
  });

  it('/start asks first; the button starts the session; /pause, /resume, /history and /stop work', async () => {
    const { f, h, waitReply } = await setup();
    f.pushUpdate(message('/start'));
    const ask = await waitReply(/Start a session with the saved settings\?/);
    expect(ask.text).toMatch(/Paper/);
    expect((await h.get('/api/state')).json.running).toBe(false); // asking does nothing

    f.pushUpdate(button('confirmstart'));
    await waitReply(/Session started/);
    expect((await h.get('/api/state')).json.running).toBe(true);
    await waitReply(/▶️ SESSION · Session started/); // the lifecycle alert

    await h.post('/api/manual', { result: 'P' });
    await h.post('/api/manual', { result: 'B' });
    f.pushUpdate(message('/history 5'));
    const hist = await waitReply(/Last 2 hands/);
    expect(hist.text).toMatch(/#1 .* → P/);
    expect(hist.text).toMatch(/#2 .* → B/);

    f.pushUpdate(message('/pause'));
    await waitReply(/Paused/);
    expect((await h.get('/api/state')).json.paused).toBe(true);
    f.pushUpdate(message('/resume'));
    await waitReply(/Resumed/);
    expect((await h.get('/api/state')).json.paused).toBe(false);

    f.pushUpdate(message('/stop'));
    await waitReply(/Session stopped by Telegram command/);
    expect((await h.get('/api/state')).json.running).toBe(false);
    await waitReply(/SESSION · Session stopped[\s\S]*Stopped from Telegram/);
    expect(f.hits.filter((x) => x.url.endsWith('/answerCallbackQuery'))).toHaveLength(1);
  });

  it('typing confirmstart instead of pressing the button does not start a session', async () => {
    const { f, h, waitReply } = await setup();
    f.pushUpdate(message('/confirmstart'));
    await waitReply(/press the button/);
    expect((await h.get('/api/state')).json.running).toBe(false);
  });

  it('/report sends the session log as a CSV document', async () => {
    const { f, h, waitReply } = await setup();
    await h.post('/api/start', { settings: { ...MANUAL } });
    await h.post('/api/manual', { result: 'P' });
    f.pushUpdate(message('/report'));
    await vi.waitFor(() => expect(f.hits.some((x) => x.url.endsWith('/sendDocument'))).toBe(true), { timeout: 3000 });
    const doc = f.hits.find((x) => x.url.endsWith('/sendDocument'))!;
    expect(String(doc.headers['content-type'])).toMatch(/^multipart\/form-data/);
    expect(String(doc.body)).toContain('trade247_session_history.csv');
    expect(String(doc.body)).toContain(CHAT);
    f.pushUpdate(message('/nonsense'));
    await waitReply(/Unknown command \/nonsense/);
  });

  it('/approve and /skip say so when no bet is waiting', async () => {
    const { f, waitReply } = await setup();
    f.pushUpdate(message('/approve'));
    await waitReply(/No bet is waiting for approval/);
    f.pushUpdate(button('approve:3-P-1000000'));
    await vi.waitFor(() => expect(f.hits.filter((x) => x.url.endsWith('/answerCallbackQuery'))).toHaveLength(1));
  });

  it('stops reading commands when turned off, and never starts for a @channel chat', async () => {
    const { f, h } = await setup();
    expect((await h.get('/api/state')).json.telegram.on).toBe(true);
    await h.post('/api/settings', { telegram_control: 'off' });
    await vi.waitFor(async () => expect((await h.get('/api/state')).json.telegram.on).toBe(false));
    await h.post('/api/settings', { telegram_control: 'on', telegram_chat_id: '@some_channel' });
    await new Promise((r) => setTimeout(r, 100));
    expect((await h.get('/api/state')).json.telegram.on).toBe(false);
    const polls = f.hits.filter((x) => x.url.endsWith('/getUpdates')).length;
    await new Promise((r) => setTimeout(r, 150));
    expect(f.hits.filter((x) => x.url.endsWith('/getUpdates')).length).toBe(polls);
  });
});
