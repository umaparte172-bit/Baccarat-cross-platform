import { configProblem, telegramCall, type CallResult, type TelegramConfig, type TelegramOptions } from './telegram.js';

/**
 * Remote control over Telegram: the bot reads commands sent to it (long polling, no server or open port needed)
 * and runs them on this computer. Only messages from the configured chat are obeyed; everything else is ignored.
 * Commands that start betting ask for a confirmation button first; commands that make things safer (stop, pause,
 * skip) act at once.
 */

export interface ControlHost {
  /** Multi-line status text. */
  status(): string;
  settingsText(): string;
  history(n: number): string;
  running(): boolean;
  start(): { ok: boolean; status: string };
  stop(): void;
  pause(): boolean;
  resume(): boolean;
  retry(): boolean;
  approve(id?: string): { ok: boolean; error?: string };
  skip(): { ok: boolean; error?: string };
  /** The report as CSV text, or null when there is none. */
  reportCsv(): string | null;
  /** What starting would do, to show on the confirmation button ("Paper", "REAL MONEY - Confirm", ...). */
  startDescription(): string;
}

export interface CommandSpec {
  command: string;
  category: 'MONITOR' | 'SESSION' | 'BETTING' | 'TABLE' | 'REPORTS' | 'HELP';
  action: string;
  purpose: string;
}

/** The command list: shown by /help and registered as the bot's menu. */
export const COMMANDS: CommandSpec[] = [
  { command: 'status', category: 'MONITOR', action: 'Shows the state, P&L, balance, next bet and connection', purpose: 'Check the bot from anywhere' },
  { command: 'history', category: 'MONITOR', action: 'Lists the last 10 hands (or /history 25)', purpose: 'See what was played and the result' },
  { command: 'settings', category: 'MONITOR', action: 'Shows strategy, base unit, limits and bet mode', purpose: 'Confirm what the bot is using' },
  { command: 'start', category: 'SESSION', action: 'Starts a session with the saved settings (asks to confirm)', purpose: 'Begin play without being at the computer' },
  { command: 'stop', category: 'SESSION', action: 'Stops the session at once', purpose: 'Emergency stop' },
  { command: 'pause', category: 'SESSION', action: 'Stops real bets, keeps reading results', purpose: 'Sit out without losing the table rhythm' },
  { command: 'resume', category: 'SESSION', action: 'Continues after a pause', purpose: 'Return to betting' },
  { command: 'approve', category: 'BETTING', action: 'Places the bet waiting for approval (Confirm mode)', purpose: 'Say yes to a real bet' },
  { command: 'skip', category: 'BETTING', action: 'Skips the bet waiting for approval', purpose: 'Say no to a real bet' },
  { command: 'retry', category: 'TABLE', action: 'Reconnects to the live table now', purpose: 'Recover after a disconnection' },
  { command: 'report', category: 'REPORTS', action: 'Sends the session log as a CSV file', purpose: 'Open the trades in Excel on your phone or PC' },
  { command: 'help', category: 'HELP', action: 'Shows this list', purpose: 'Find a command' },
];

const CATEGORY_ICON: Record<CommandSpec['category'], string> = {
  MONITOR: '📊', SESSION: '🎛️', BETTING: '🎯', TABLE: '🔌', REPORTS: '📁', HELP: '❓',
};

export function helpText(): string {
  const lines = ['🤖 TRADE247 · Remote control', '━━━━━━━━━━━━━━━━', 'Format: /command - action → purpose'];
  for (const category of Object.keys(CATEGORY_ICON) as CommandSpec['category'][]) {
    const items = COMMANDS.filter((c) => c.category === category);
    lines.push('', `${CATEGORY_ICON[category]} ${category}`);
    for (const c of items) lines.push(`/${c.command} - ${c.action} → ${c.purpose}`);
  }
  lines.push('', 'Only this chat can control the bot. Start asks for a confirmation button first.');
  return lines.join('\n');
}

/** How long the "Yes, start" button stays valid. */
const CONFIRM_SECONDS = 120;

type Keyboard = { inline_keyboard: Array<Array<{ text: string; callback_data: string }>> };

const STATUS_KEYS: Keyboard = {
  inline_keyboard: [
    [{ text: '🔄 Refresh', callback_data: 'status' }, { text: '⏸ Pause', callback_data: 'pause' }, { text: '▶️ Resume', callback_data: 'resume' }],
    [{ text: '⏹ Stop', callback_data: 'stop' }, { text: '📁 Report', callback_data: 'report' }],
  ],
};

export function approvalKeyboard(id: string): Keyboard {
  return { inline_keyboard: [[{ text: '✅ Approve bet', callback_data: `approve:${id}` }, { text: '⏭ Skip', callback_data: 'skip' }]] };
}

interface Update {
  update_id: number;
  message?: { message_id: number; date: number; chat: { id: number }; text?: string };
  callback_query?: { id: string; data?: string; message?: { chat: { id: number }; date: number } };
}

export interface ControlOptions extends Pick<TelegramOptions, 'baseUrl' | 'fetch'> {
  /** Long-poll wait in seconds (Telegram holds the request open this long when there is nothing new). */
  pollSeconds?: number;
  /** Pause after a failed poll. */
  retryMs?: number;
  now?: () => number;
  onError?: (message: string) => void;
}

export class TelegramControl {
  private active = false;
  private loop: Promise<void> | null = null;
  private abort: AbortController | null = null;
  private offset = 0;
  private startedAt = 0;
  private configKey = '';
  /** Settings changes are applied one after the other, so two quick saves can never start two readers. */
  private syncing: Promise<void> = Promise.resolve();
  /** Last problem reading commands, for the page. */
  lastError = '';

  constructor(
    private readonly getConfig: () => TelegramConfig & { enabled: boolean },
    private readonly host: ControlHost,
    private readonly options: ControlOptions = {},
  ) {}

  get running(): boolean {
    return this.active;
  }

  /** Starts or stops reading commands to match the settings. Safe to call after every settings change. */
  sync(): Promise<void> {
    this.syncing = this.syncing.then(async () => {
      const c = this.getConfig();
      const usable = c.enabled && configProblem(c) === null && /^-?\d+$/.test(c.chatId);
      const key = usable ? `${c.token}|${c.chatId}` : '';
      if (key === this.configKey && this.active === usable) return;
      await this.stop();
      this.configKey = key;
      if (usable) this.begin();
    });
    return this.syncing;
  }

  async stop(): Promise<void> {
    this.active = false;
    this.abort?.abort();
    const running = this.loop;
    if (running) await running;
  }

  private begin(): void {
    this.active = true;
    this.startedAt = Math.floor((this.options.now ?? Date.now)() / 1000);
    this.lastError = '';
    this.loop = this.run().finally(() => {
      this.loop = null;
    });
  }

  private call<T>(method: string, payload: object, timeoutMs = 10_000): Promise<CallResult<T>> {
    const c = this.getConfig();
    return telegramCall<T>(c, method, payload, { baseUrl: this.options.baseUrl, fetch: this.options.fetch, timeoutMs, signal: this.abort?.signal });
  }

  private async run(): Promise<void> {
    this.abort = new AbortController();
    // Anything sent to the bot while the program was off (typed commands or button presses) is old news:
    // skip past it, so a "Stop" or "Yes, start" pressed hours ago is never acted on now.
    const backlog = await this.call<Update[]>('getUpdates', { offset: -1, timeout: 0 });
    const last = backlog.ok ? backlog.result?.at(-1) : undefined;
    if (last) this.offset = last.update_id + 1;
    await this.call('setMyCommands', { commands: COMMANDS.map((c) => ({ command: c.command, description: `${c.action}`.slice(0, 256) })) });
    const wait = this.options.pollSeconds ?? 25;
    while (this.active) {
      const r = await this.call<Update[]>(
        'getUpdates',
        { offset: this.offset, timeout: wait, allowed_updates: ['message', 'callback_query'] },
        (wait + 10) * 1000,
      );
      if (!this.active) break;
      if (!r.ok) {
        this.lastError =
          r.code === 409
            ? 'Telegram commands paused: another program (or a webhook) is reading this bot. Use a bot token that only TRADE247 uses.'
            : `Telegram commands: ${r.error ?? 'unknown error'}`;
        this.options.onError?.(this.lastError);
        await this.sleep(r.code === 409 ? 30_000 : (this.options.retryMs ?? 5000));
        continue;
      }
      this.lastError = '';
      for (const u of r.result ?? []) {
        this.offset = Math.max(this.offset, u.update_id + 1);
        try {
          await this.handle(u);
        } catch (e) {
          this.options.onError?.(`Telegram command failed: ${(e as Error).message}`);
        }
      }
    }
  }

  private sleep(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const t = setTimeout(resolve, ms);
      this.abort?.signal.addEventListener('abort', () => { clearTimeout(t); resolve(); }, { once: true });
    });
  }

  private ownChat(id: number | undefined): boolean {
    return id !== undefined && String(id) === this.getConfig().chatId;
  }

  async handle(u: Update): Promise<void> {
    if (u.callback_query) {
      const q = u.callback_query;
      if (!this.ownChat(q.message?.chat.id)) return;
      // A start button only counts shortly after it was offered.
      const nowS = Math.floor((this.options.now ?? Date.now)() / 1000);
      if (q.data === 'confirmstart' && (q.message?.date ?? 0) < nowS - CONFIRM_SECONDS) {
        await this.call('answerCallbackQuery', { callback_query_id: q.id, text: 'This button has expired. Send /start again.' });
        return;
      }
      const reply = await this.run1(q.data ?? '', true);
      await this.call('answerCallbackQuery', { callback_query_id: q.id, text: reply.toast.slice(0, 190) });
      return;
    }
    const m = u.message;
    if (!m || !this.ownChat(m.chat.id) || typeof m.text !== 'string') return;
    // Commands sent while the program was off are old news: starting or stopping on them would surprise the user.
    if (m.date < this.startedAt - 5) return;
    const text = m.text.trim();
    if (!text.startsWith('/')) {
      await this.reply('Send /help to see the commands.');
      return;
    }
    const [head, ...rest] = text.slice(1).split(/\s+/);
    const command = (head ?? '').split('@')[0]!.toLowerCase();
    await this.run1([command, ...rest].join(' '), false);
  }

  /** Runs one command (typed or from a button) and answers in the chat. Returns a short toast for buttons. */
  private async run1(input: string, fromButton: boolean): Promise<{ toast: string }> {
    const sep = input.search(/[\s:]/);
    const cmd = sep < 0 ? input : input.slice(0, sep);
    const arg = sep < 0 ? '' : input.slice(sep + 1).trim();
    const h = this.host;
    const say = async (text: string, keyboard?: Keyboard): Promise<{ toast: string }> => {
      await this.reply(text, keyboard);
      return { toast: text.split('\n')[0]! };
    };
    switch (cmd) {
      case 'help':
        return say(helpText());
      case 'status':
        return say(h.status(), STATUS_KEYS);
      case 'settings':
        return say(h.settingsText());
      case 'history': {
        const n = Math.min(50, Math.max(1, Number.parseInt(arg, 10) || 10));
        return say(h.history(n));
      }
      case 'start':
        if (h.running()) return say('ℹ️ A session is already running. /status');
        return say(`❓ Start a session with the saved settings?\nBet mode: ${h.startDescription()}`, {
          inline_keyboard: [[{ text: '▶️ Yes, start', callback_data: 'confirmstart' }, { text: 'Cancel', callback_data: 'cancel' }]],
        });
      case 'confirmstart': {
        if (!fromButton) return say('Use /start and press the button.');
        if (h.running()) return say('ℹ️ A session is already running. /status');
        const r = h.start();
        return say(r.ok ? `▶️ Session started.\n${h.status()}` : `❌ Not started: ${r.status}`, r.ok ? STATUS_KEYS : undefined);
      }
      case 'cancel':
        return say('Cancelled. Nothing was changed.');
      case 'stop':
        if (!h.running()) return say('ℹ️ No session is running.');
        h.stop();
        return say(`⏹ Session stopped by Telegram command.\n${h.status()}`);
      case 'pause':
        return say(h.pause() ? '⏸ Paused: real bets stop, results are still read. /resume to continue.' : 'ℹ️ No session is running.');
      case 'resume':
        return say(h.resume() ? '▶️ Resumed.' : 'ℹ️ No session is running.');
      case 'retry':
        return say(h.retry() ? '🔌 Reconnecting to the table…' : 'ℹ️ No session is running.');
      case 'approve': {
        const r = h.approve(fromButton && arg ? arg : undefined);
        return say(r.ok ? '✅ Approved: the bet is being placed.' : `❌ ${r.error}`);
      }
      case 'skip': {
        const r = h.skip();
        return say(r.ok ? '⏭ Bet skipped.' : `❌ ${r.error}`);
      }
      case 'report': {
        const csv = h.reportCsv();
        if (csv === null) return say('ℹ️ No log yet - start a session first.');
        const sent = await this.sendDocument('trade247_session_history.csv', csv);
        return sent.ok ? { toast: 'Report sent' } : say(`❌ Could not send the report: ${sent.error}`);
      }
      default:
        return say(`Unknown command /${cmd}. Send /help.`);
    }
  }

  private async reply(text: string, keyboard?: Keyboard): Promise<void> {
    const payload: Record<string, unknown> = { chat_id: this.getConfig().chatId, text: text.slice(0, 4000), disable_web_page_preview: true };
    if (keyboard) payload.reply_markup = keyboard;
    await this.call('sendMessage', payload);
  }

  /** Asks for approval of a waiting bet, with buttons. */
  async askApproval(id: string, text: string): Promise<void> {
    if (!this.active) return;
    await this.reply(text, approvalKeyboard(id));
  }

  private async sendDocument(name: string, csv: string): Promise<CallResult> {
    const c = this.getConfig();
    const problem = configProblem(c);
    if (problem) return { ok: false, error: problem };
    const form = new FormData();
    form.append('chat_id', c.chatId);
    form.append('caption', '📁 REPORTS · Session log (opens in Excel)');
    form.append('document', new Blob([String.fromCodePoint(0xfeff) + csv], { type: 'text/csv' }), name);
    const base = (this.options.baseUrl ?? 'https://api.telegram.org').replace(/\/+$/, '');
    try {
      const res = await (this.options.fetch ?? fetch)(`${base}/bot${c.token}/sendDocument`, {
        method: 'POST', body: form, redirect: 'error', signal: AbortSignal.timeout(30_000),
      });
      const body = (await res.json().catch(() => ({}))) as { ok?: boolean; description?: string };
      return body.ok ? { ok: true } : { ok: false, error: String(body.description ?? `HTTP ${res.status}`).split(c.token).join('***').slice(0, 200) };
    } catch {
      return { ok: false, error: 'Telegram could not be reached' };
    }
  }
}
