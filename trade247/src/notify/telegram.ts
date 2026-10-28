export type AlertKind = 'daily_stop_loss' | 'daily_take_profit' | 'disconnected' | 'shoe_change' | 'bet_problem' | 'test';

export interface TelegramConfig {
  token: string;
  chatId: string;
}

export interface SendResult {
  ok: boolean;
  error?: string;
}

/** What the session needs from an alert channel. */
export interface Notifier {
  /** Queue an alert; never throws and never blocks the caller. */
  notify(kind: AlertKind, text: string): void;
  /** Send a test message now and report exactly what happened. */
  test(): Promise<SendResult>;
  /** Resolves when everything queued has been sent (or has failed). */
  idle(): Promise<void>;
}

// The token is placed in the request path, so only the characters Telegram itself issues are accepted.
export const TOKEN_PATTERN = /^\d{3,20}:[A-Za-z0-9_-]{20,80}$/;
export const CHAT_ID_PATTERN = /^(-?\d{1,20}|@[A-Za-z][A-Za-z0-9_]{3,31})$/;

export const DEFAULT_TELEGRAM_URL = 'https://api.telegram.org';
const MAX_TEXT = 4000;
const MAX_RESPONSE_BYTES = 64 * 1024;
const MAX_QUEUE = 30;

export interface TelegramOptions {
  /** Only for tests; real use always talks to api.telegram.org. Never taken from user settings. */
  baseUrl?: string;
  timeoutMs?: number;
  /** Pause between queued messages (Telegram allows about one per second per chat). */
  minGapMs?: number;
  fetch?: typeof fetch;
  /** Called with a human-readable reason when a queued alert could not be delivered. */
  onError?: (message: string) => void;
}

export function configProblem(config: TelegramConfig): string | null {
  if (!config.token || !config.chatId) return 'Enter the bot token and the chat ID first';
  if (!TOKEN_PATTERN.test(config.token)) return 'The bot token does not look right (expected 123456789:ABC...)';
  if (!CHAT_ID_PATTERN.test(config.chatId)) return 'The chat ID must be a number (or @channelname)';
  return null;
}

interface Queued {
  kind: AlertKind;
  text: string;
}

export class TelegramNotifier implements Notifier {
  private queue: Queued[] = [];
  private pumping: Promise<void> | null = null;

  constructor(
    private readonly getConfig: () => TelegramConfig,
    private readonly options: TelegramOptions = {},
  ) {}

  notify(kind: AlertKind, text: string): void {
    if (configProblem(this.getConfig()) !== null) return;
    // A fast simulator can change shoes every few seconds; one pending shoe alert is enough.
    if (kind === 'shoe_change' && this.queue.some((q) => q.kind === 'shoe_change')) return;
    this.queue.push({ kind, text });
    while (this.queue.length > MAX_QUEUE) {
      const stale = this.queue.findIndex((q) => q.kind === 'shoe_change');
      this.queue.splice(stale >= 0 ? stale : 0, 1);
    }
    this.pumping ??= this.pump();
  }

  async test(): Promise<SendResult> {
    return this.send(this.getConfig(), 'TRADE247: test message. Alerts are working.');
  }

  async idle(): Promise<void> {
    while (this.pumping) await this.pumping;
  }

  private async pump(): Promise<void> {
    try {
      while (this.queue.length > 0) {
        const next = this.queue.shift()!;
        const result = await this.send(this.getConfig(), next.text);
        if (!result.ok) this.options.onError?.(result.error ?? 'unknown error');
        const gap = this.options.minGapMs ?? 1100;
        if (this.queue.length > 0 && gap > 0) await new Promise((r) => setTimeout(r, gap));
      }
    } finally {
      this.pumping = null;
    }
  }

  private async send(config: TelegramConfig, text: string): Promise<SendResult> {
    const problem = configProblem(config);
    if (problem) return { ok: false, error: problem };

    const base = (this.options.baseUrl ?? DEFAULT_TELEGRAM_URL).replace(/\/+$/, '');
    const url = `${base}/bot${config.token}/sendMessage`;
    const redact = (s: string): string => s.split(config.token).join('***');
    const doFetch = this.options.fetch ?? fetch;
    try {
      const response = await doFetch(url, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ chat_id: config.chatId, text: text.slice(0, MAX_TEXT), disable_web_page_preview: true }),
        redirect: 'error',
        signal: AbortSignal.timeout(this.options.timeoutMs ?? 8000),
      });
      const raw = (await response.text()).slice(0, MAX_RESPONSE_BYTES);
      let body: { ok?: boolean; description?: string } = {};
      try {
        body = JSON.parse(raw) as typeof body;
      } catch {
        // Not JSON: fall through to the status-based message below.
      }
      if (response.ok && body.ok === true) return { ok: true };
      const why = typeof body.description === 'string' && body.description ? body.description.slice(0, 200) : `HTTP ${response.status}`;
      return { ok: false, error: redact(`Telegram refused the message: ${why}`) };
    } catch (e) {
      const name = (e as Error).name;
      if (name === 'TimeoutError' || name === 'AbortError') return { ok: false, error: 'Telegram did not answer in time' };
      return { ok: false, error: 'Telegram could not be reached (check the internet connection)' };
    }
  }
}

/** A notifier that does nothing, for sessions without alerts. */
export const silentNotifier: Notifier = {
  notify() {},
  async test() {
    return { ok: false, error: 'Alerts are not available' };
  },
  async idle() {},
};
