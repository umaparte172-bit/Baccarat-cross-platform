import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { createEngine, unsupportedReason } from './engine/factory.js';
import { fmt2, toMicro } from './engine/money.js';
import { RiskGovernor, dailyLockReason, keepAliveBet, riskConfig, type RiskConfig } from './engine/risk.js';
import { normalizeSettings, publicSettings, type Settings } from './engine/settings.js';
import type { BetPlan, StrategyEngine } from './engine/strategy.js';
import { isResult, type NextBet, type Row } from './engine/types.js';
import { ManualFeed, ReplayFeed, SimulatorFeed, parseReplay, type Feed, type HandEvent } from './feeds/feed.js';
import { TelegramNotifier, configProblem, type Notifier, type SendResult, type TelegramOptions } from './notify/telegram.js';
import { LOG_FILE_NAME, appendHistory, buildCsv, type HandRecord } from './report/csv.js';
import { latestSessionFromHistory, tableFromRecords, type Table } from './report/table.js';
import { buildXlsx } from './report/xlsx.js';
import { localDay, type DayTotal, type SettingsStore } from './store/files.js';

const pad = (n: number): string => String(n).padStart(2, '0');

export function localTime(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

const dollars = (micro: number): number => Math.round(micro / 10_000) / 100;
const round2 = (n: number): number => Math.round(n * 100) / 100;
/** The day total is written at most this often while hands stream in, and always when a session ends. */
const DAY_WRITE_INTERVAL_MS = 1000;

export interface StartReply {
  ok: boolean;
  status: string;
}

export interface SessionOptions {
  now?: () => Date;
  /** Test hooks for the Telegram client (a local fake server, no pauses). */
  telegram?: Pick<TelegramOptions, 'baseUrl' | 'timeoutMs' | 'minGapMs' | 'fetch'>;
}

const signed = (micro: number): string => `${micro > 0 ? '+' : ''}${fmt2(micro)}`;

function toNextBet(plan: BetPlan, reason?: string): NextBet {
  return {
    mode: plan.mode,
    pick: plan.side === 'P' ? 'PLAYER' : 'BANKER',
    units: plan.units,
    stake: plan.stakeMicro / 1_000_000,
    final: plan.final,
    pattern: plan.pattern,
    reason: reason ?? plan.reason,
  };
}

export class SessionManager {
  private settings: Settings;
  private running = false;
  private paused = false;
  private status = 'IDLE';
  private error = '';

  private sessionNo = 0;
  private startedTs = 0;
  private stoppedTs = 0;
  private records: HandRecord[] = [];
  private pnlMicro = 0;
  private peakMicro = 0;
  private maxDrawdownMicro = 0;
  private maxStakeUnits = 0;
  private virtualHands = 0;
  private wins = 0;
  private losses = 0;
  private engine: StrategyEngine | null = null;
  private governor: RiskGovernor | null = null;
  private risk: RiskConfig;
  private feed: Feed | null = null;
  private logWarned = false;

  private day: DayTotal;
  private dayWrittenAt = 0;
  private dayDirty = false;

  private readonly now: () => Date;
  private readonly notifier: Notifier;
  private shoeNo = 1;
  private feedIsLost = false;

  constructor(
    private readonly store: SettingsStore,
    options: SessionOptions = {},
  ) {
    this.now = options.now ?? (() => new Date());
    this.settings = store.load();
    this.risk = riskConfig(this.settings);
    this.day = store.loadDay(localDay(this.now()));
    this.notifier = new TelegramNotifier(
      () => ({ token: this.settings.telegram_token, chatId: this.settings.telegram_chat_id }),
      {
        ...options.telegram,
        onError: (message) => {
          this.error = `Alert not delivered - ${message}`;
        },
      },
    );
  }

  get historyPath(): string {
    return join(this.store.logsDir, LOG_FILE_NAME);
  }

  // ---------------------------------------------------------------- state for the page

  getState(sinceRaw: number) {
    const since = Number.isInteger(sinceRaw) && sinceRaw > 0 && sinceRaw <= this.records.length ? sinceRaw : 0;
    const decided = this.wins + this.losses;
    const now = this.now();
    const live = this.running && this.engine !== null && this.governor !== null;
    return {
      session: this.sessionNo,
      settings: publicSettings(this.settings),
      running: this.running,
      paused: this.paused,
      status: this.status,
      error: this.error,
      strategy: this.settings.strategy,
      next: live ? this.previewNext() : null,
      hands: this.records.length,
      virtual_hands: this.virtualHands,
      win_rate: decided === 0 ? 0 : Math.round((this.wins / decided) * 1000) / 10,
      day_pnl: dollars(this.dayTotal(now)),
      max_drawdown: dollars(this.maxDrawdownMicro),
      max_stake_units: this.maxStakeUnits,
      strategy_state: this.engine && this.governor ? { ...this.engine.status(), ...this.governor.status() } : {},
      balance: dollars(toMicro(this.settings.start_balance) + this.pnlMicro),
      pnl: dollars(this.pnlMicro),
      session_no: this.sessionNo,
      started_ts: this.startedTs,
      stopped_ts: this.stoppedTs,
      now_ts: now.getTime() / 1000,
      logs_dir: this.store.logsDir,
      total_rows: this.records.length,
      rows: this.records.slice(since).map((r) => r.row),
    };
  }

  private previewNext(): NextBet {
    const engine = this.engine!;
    const hand = this.governor!.prepare(engine, this.paused);
    const plan = engine.plan(this.paused || hand.watchOnly);
    if (hand.keepAlive) {
      const stakeMicro = keepAliveBet(plan.side, 'T', this.risk).stakeMicro;
      return {
        mode: 'REAL', pick: plan.side === 'P' ? 'PLAYER' : 'BANKER', units: round2(stakeMicro / toMicro(this.settings.unit_value)),
        stake: stakeMicro / 1_000_000, final: false, pattern: 'keep-alive', reason: 'Keep-alive minimum bet',
      };
    }
    return toNextBet(plan, hand.reason);
  }

  // ---------------------------------------------------------------- commands

  start(raw: unknown): StartReply {
    if (this.running) return { ok: false, status: 'A session is already running' };
    const { settings, errors } = normalizeSettings(raw, this.settings);
    if (errors.length) return { ok: false, status: errors[0]! };
    if (settings.feed === 'csv') {
      const replay = parseReplay(settings.csv_text);
      if (replay.length === 0) {
        return {
          ok: false,
          status: settings.csv_path
            ? 'Replay from a file path is not available yet - paste the P / B / T letters into the box'
            : 'Paste P / B / T results to replay',
        };
      }
    }
    const made = createEngine(settings);
    if ('error' in made) return { ok: false, status: made.error };
    const risk = riskConfig(settings);
    const locked = dailyLockReason(risk, this.dayTotal(this.now()));
    if (locked) return { ok: false, status: locked };

    this.settings = settings;
    this.risk = risk;
    this.store.save(settings);
    this.beginSession();
    this.error = this.alertsWarning(settings);
    this.engine = made.engine;
    this.governor = new RiskGovernor(risk);
    this.feed = this.createFeed();
    this.feed.start();
    return { ok: true, status: this.status };
  }

  /** Alerts that are half set up would silently do nothing, so say so (without blocking the session). */
  private alertsWarning(s: Settings): string {
    if (!s.telegram_token && !s.telegram_chat_id) return '';
    const problem = configProblem({ token: s.telegram_token, chatId: s.telegram_chat_id });
    return problem ? `Telegram alerts are off: ${problem}` : '';
  }

  apply(raw: unknown): { ok: boolean; live: boolean } {
    const { settings, errors } = normalizeSettings(raw, this.settings);
    if (errors.length) {
      this.error = errors[0]!;
      return { ok: false, live: false };
    }
    if (this.running) {
      // The feed and the money baseline cannot change under a running session.
      settings.feed = this.settings.feed;
      settings.sim_seed = this.settings.sim_seed;
      settings.start_balance = this.settings.start_balance;
      const reason = unsupportedReason(settings);
      if (reason) {
        this.error = reason;
        return { ok: false, live: false };
      }
    }
    this.error = '';
    this.settings = settings;
    this.risk = riskConfig(settings);
    this.store.save(settings);
    if (this.running) {
      this.engine?.configure(settings);
      this.governor?.configure(this.risk);
    }
    return { ok: true, live: this.running };
  }

  stop(): void {
    if (!this.running) return;
    this.finish('STOPPED');
  }

  pause(): boolean {
    if (!this.running || this.paused) return this.running;
    this.paused = true;
    this.status = 'PAUSED';
    return true;
  }

  resume(): boolean {
    if (!this.running) return false;
    if (this.paused) {
      this.paused = false;
      this.status = 'RUNNING';
    }
    return true;
  }

  retry(): boolean {
    if (!this.running || !this.feed) return false;
    this.feed.retry();
    return true;
  }

  /** Returns the history file path when a session's rows were cleared. */
  reset(): { ok: boolean; saved?: string } {
    if (this.running) return { ok: false };
    const had = this.records.length > 0;
    this.clearSession();
    this.sessionNo = 0;
    this.startedTs = 0;
    this.stoppedTs = 0;
    this.status = 'IDLE';
    this.error = '';
    return had ? { ok: true, saved: this.historyPath } : { ok: true };
  }

  manual(result: unknown): { ok: boolean; error?: string } {
    if (!isResult(result)) return { ok: false, error: 'Result must be P, B or T' };
    if (!this.running || !this.feed) return { ok: false, error: 'Session is not running' };
    if (!this.feed.push(result)) return { ok: false, error: 'The Manual feed is not selected' };
    return { ok: true };
  }

  saveSettings(raw: unknown): { ok: boolean } {
    return { ok: this.apply(raw).ok };
  }

  // ---- reports: the live session if there is one, otherwise the latest session in the history file

  private historyReport() {
    try {
      return latestSessionFromHistory(readFileSync(this.historyPath, 'utf8'));
    } catch {
      return null;
    }
  }

  reportCsv(): string | null {
    return this.records.length > 0 ? buildCsv(this.records) : (this.historyReport()?.csv ?? null);
  }

  reportTable(): Table | null {
    return this.records.length > 0 ? tableFromRecords(this.records) : (this.historyReport()?.table ?? null);
  }

  reportXlsx(): Buffer | null {
    const table = this.reportTable();
    return table ? buildXlsx(table, this.now()) : null;
  }

  // ---- alerts

  telegramTest(): Promise<SendResult> {
    return this.notifier.test();
  }

  /** Lets queued alerts finish before the process exits. */
  async flushAlerts(): Promise<void> {
    await this.notifier.idle();
  }

  private summary(): string {
    const balance = toMicro(this.settings.start_balance) + this.pnlMicro;
    return `Session #${this.sessionNo} (${this.settings.strategy}), ${this.records.length} hands, session ${signed(this.pnlMicro)}, balance ${fmt2(balance)}`;
  }

  /** Called by a live table feed when the connection to the table drops. Alerts once per outage. */
  feedLost(reason: string): boolean {
    if (!this.running) return false;
    this.status = `FEED LOST - ${reason}`.slice(0, 80);
    if (!this.feedIsLost) {
      this.feedIsLost = true;
      this.notifier.notify('disconnected', `TRADE247: connection to the table lost (${reason}). ${this.summary()}.`);
    }
    return true;
  }

  feedRestored(): boolean {
    if (!this.running || !this.feedIsLost) return false;
    this.feedIsLost = false;
    this.status = this.paused ? 'PAUSED' : 'RUNNING';
    return true;
  }

  // ---------------------------------------------------------------- internals

  private beginSession(): void {
    this.clearSession();
    this.sessionNo = this.store.nextSessionNumber();
    this.startedTs = Math.floor(this.now().getTime() / 1000);
    this.stoppedTs = 0;
    this.running = true;
    this.paused = false;
    this.status = 'RUNNING';
    this.error = '';
    this.logWarned = false;
    this.shoeNo = 1;
    this.feedIsLost = false;
  }

  private clearSession(): void {
    this.records = [];
    this.pnlMicro = 0;
    this.peakMicro = 0;
    this.maxDrawdownMicro = 0;
    this.maxStakeUnits = 0;
    this.virtualHands = 0;
    this.wins = 0;
    this.losses = 0;
    this.engine = null;
    this.governor = null;
    this.feed = null;
  }

  private finish(status: string): void {
    this.feed?.stop();
    this.running = false;
    this.paused = false;
    this.status = status;
    this.stoppedTs = Math.floor(this.now().getTime() / 1000);
    this.persistDay(true);
  }

  private createFeed(): Feed {
    const handler = (event: HandEvent): void => this.onHand(event);
    switch (this.settings.feed) {
      case 'manual':
        return new ManualFeed(handler);
      case 'csv': {
        const feed = new ReplayFeed(handler, parseReplay(this.settings.csv_text));
        feed.onFinished = () => this.finish('STOPPED (replay finished)');
        return feed;
      }
      default:
        return new SimulatorFeed(handler, () => this.settings.sim_interval, this.settings.sim_seed);
    }
  }

  // ---- the day's profit or loss across all sessions, kept across restarts

  private dayTotal(now: Date): number {
    return this.day.day === localDay(now) ? this.day.pnlMicro : 0;
  }

  private addToDay(pnlMicro: number, when: Date): number {
    const today = localDay(when);
    if (this.day.day !== today) this.day = { day: today, pnlMicro: 0 };
    this.day.pnlMicro += pnlMicro;
    this.dayDirty = true;
    this.persistDay(false);
    return this.day.pnlMicro;
  }

  private persistDay(force: boolean): void {
    if (!this.dayDirty) return;
    const t = this.now().getTime();
    if (!force && t - this.dayWrittenAt < DAY_WRITE_INTERVAL_MS) return;
    try {
      this.store.saveDay(this.day);
      this.dayWrittenAt = t;
      this.dayDirty = false;
    } catch (e) {
      this.error = `Could not save today's total: ${(e as Error).message}`;
    }
  }

  private onHand(event: HandEvent): void {
    const engine = this.engine;
    const governor = this.governor;
    if (!this.running || !engine || !governor) return;
    const s = this.settings;
    if (event.newShoe) {
      engine.newShoe();
      governor.newShoe();
      if (this.records.length > 0) {
        this.shoeNo++;
        this.notifier.notify('shoe_change', `TRADE247: shoe change, shoe #${this.shoeNo} starts. ${this.summary()}.`);
      }
    }

    const hand = governor.prepare(engine, this.paused);
    if (hand.cutRun) engine.cutRun();
    const res = engine.settle(event.result, this.paused || hand.watchOnly);
    const when = this.now();

    // The keep-alive minimum bet rides on a hand the strategy sat out; its money is real.
    let mode: Row['mode'] = res.plan.mode;
    let outcome = res.outcome;
    let pnlMicro = res.pnlMicro;
    let stakeMicro = res.plan.mode === 'REAL' ? res.plan.stakeMicro : 0;
    let keepAliveNote = '';
    if (hand.keepAlive) {
      const k = keepAliveBet(res.plan.side, event.result, this.risk);
      mode = 'MIN';
      outcome = k.outcome;
      pnlMicro = k.pnlMicro;
      stakeMicro = k.stakeMicro;
      keepAliveNote = 'keep-alive minimum bet';
    }

    this.pnlMicro += pnlMicro;
    this.peakMicro = Math.max(this.peakMicro, this.pnlMicro);
    this.maxDrawdownMicro = Math.max(this.maxDrawdownMicro, this.peakMicro - this.pnlMicro);
    const dayMicro = this.addToDay(pnlMicro, when);

    if (mode === 'VIRTUAL') this.virtualHands++;
    else if (mode === 'REAL') {
      this.maxStakeUnits = Math.max(this.maxStakeUnits, round2(res.plan.units));
      if (outcome === 'WIN') this.wins++;
      else if (outcome === 'LOSS') this.losses++;
    }

    const row: Row = {
      n: this.records.length + 1,
      time: localTime(when),
      pick: res.plan.side,
      mode,
      stake: fmt2(stakeMicro),
      result: event.result,
      outcome,
      pattern: res.pattern,
      note: [res.note, hand.reason, keepAliveNote].filter(Boolean).join(' · '),
      single_wins: res.singleWins,
      run_net_units: round2(res.runNetMicro / toMicro(s.unit_value)),
      session_pnl: fmt2(this.pnlMicro),
      cards: event.cards,
      session: this.sessionNo,
    };
    const record: HandRecord = {
      row,
      strategy: s.strategy,
      pnlMicro,
      balanceMicro: toMicro(s.start_balance) + this.pnlMicro,
    };
    this.records.push(record);
    this.writeHistory(record);

    const verdict = governor.settled({
      res, hand, manualPause: this.paused, keepAlive: hand.keepAlive,
      sessionPnlMicro: this.pnlMicro, dayPnlMicro: dayMicro,
    });
    if (verdict.resetEngine) engine.reset();
    if (verdict.stop) {
      this.finish(verdict.stop.status);
      if (verdict.stop.alert) {
        this.notifier.notify(
          verdict.stop.alert,
          `TRADE247: ${verdict.stop.message}. The day is locked: no new session until the limit is raised or tomorrow. ${this.summary()}, today ${signed(dayMicro)}.`,
        );
      }
    }
  }

  private writeHistory(record: HandRecord): void {
    try {
      appendHistory(this.historyPath, record);
    } catch (e) {
      // A full disk or a locked file must not stop the session, but the user has to be told once.
      if (!this.logWarned) {
        this.logWarned = true;
        this.error = `Could not write the session log: ${(e as Error).message}`;
      }
    }
  }
}
