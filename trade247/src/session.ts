import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { BetCoordinator, type BetHost } from './betting.js';
import { unsupportedReason } from './engine/factory.js';
import { fmt2, fmtExact, fromMicro, toMicro } from './engine/money.js';
import { PlayCore } from './engine/play.js';
import { dailyLockReason, keepAliveBet, riskConfig } from './engine/risk.js';
import { normalizeSettings, publicSettings, type Settings } from './engine/settings.js';
import type { BetPlan } from './engine/strategy.js';
import { isResult, type NextBet, type Row } from './engine/types.js';
import { ManualFeed, ReplayFeed, SimulatorFeed, parseReplay, type Feed, type HandEvent } from './feeds/feed.js';
import { LiveFeed } from './feeds/live.js';
import { loadProfiles, type LiveProfile } from './live/profile.js';
import { TelegramNotifier, configProblem, type Notifier, type SendResult, type TelegramOptions } from './notify/telegram.js';
import { LOG_FILE_NAME, appendHistory, buildCsv, type HandRecord } from './report/csv.js';
import { latestSessionFromHistory, tableFromRecords, tableCsv, type Table } from './report/table.js';
import { buildXlsx } from './report/xlsx.js';
import { localDay, type DayTotal, type SettingsStore } from './store/files.js';

const pad = (n: number): string => String(n).padStart(2, '0');

export function localTime(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`;
}

const dollars = fromMicro;
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

const signed = (micro: number): string => `${micro > 0 ? '+' : ''}${fmtExact(micro)}`;

function toNextBet(plan: BetPlan, reason?: string): NextBet {
  return {
    mode: plan.mode,
    pick: plan.side === null ? '' : plan.side === 'P' ? 'PLAYER' : 'BANKER',
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
  /** The engine, the risk layer and the money of the current session (shared with the backtest). */
  private core: PlayCore | null = null;
  private feed: Feed | null = null;
  private logWarned = false;

  private day: DayTotal;
  private dayWrittenAt = 0;
  private dayDirty = false;

  private readonly now: () => Date;
  private readonly notifier: Notifier;
  private shoeNo = 1;
  private feedIsLost = false;
  private liveProfile: LiveProfile | null = null;
  private liveProfiles: LiveProfile[] = [];
  private readonly bets: BetCoordinator;

  constructor(
    private readonly store: SettingsStore,
    options: SessionOptions = {},
  ) {
    this.now = options.now ?? (() => new Date());
    this.settings = store.load();
    this.day = store.loadDay(localDay(this.now()));
    this.bets = new BetCoordinator(this.betHost());
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

  isRunning(): boolean { return this.running; }

  getState(sinceRaw: number) {
    const since = Number.isInteger(sinceRaw) && sinceRaw > 0 && sinceRaw <= this.records.length ? sinceRaw : 0;
    const core = this.core;
    const decided = core ? core.wins + core.losses : 0;
    const now = this.now();
    const live = this.running && core !== null;
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
      virtual_hands: core?.virtualHands ?? 0,
      win_rate: decided === 0 ? 0 : Math.round(((core?.wins ?? 0) / decided) * 1000) / 10,
      day_pnl: dollars(this.dayTotal(now)),
      max_drawdown: dollars(core?.maxDrawdownMicro ?? 0),
      max_stake_units: core?.maxStakeUnits ?? 0,
      strategy_state: core ? { ...core.engine.status(), ...core.governor.status() } : {},
      balance: dollars(toMicro(this.settings.start_balance) + (core?.pnlMicro ?? 0)),
      pnl: dollars(core?.pnlMicro ?? 0),
      session_no: this.sessionNo,
      started_ts: this.startedTs,
      stopped_ts: this.stoppedTs,
      now_ts: now.getTime() / 1000,
      logs_dir: this.store.logsDir,
      total_rows: this.records.length,
      rows: this.records.slice(since).map((r) => r.row),
      bet: this.bets.view(),
    };
  }

  private previewNext(): NextBet {
    const core = this.core!;
    const hand = core.governor.prepare(core.engine, this.paused);
    const plan = core.engine.plan(this.paused || hand.watchOnly);
    if (hand.keepAlive) {
      const stakeMicro = keepAliveBet(plan.side ?? 'P', 'T', core.risk).stakeMicro;
      return {
        mode: 'REAL', pick: (plan.side ?? 'P') === 'P' ? 'PLAYER' : 'BANKER', units: round2(stakeMicro / toMicro(this.settings.unit_value)),
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
    if (settings.feed === 'live') {
      const profiles = loadProfiles(this.store.dataDir);
      if ('error' in profiles) return { ok: false, status: profiles.error };
      this.liveProfiles = profiles;
      this.liveProfile = profiles[0]!;
    }
    if (settings.bet_mode !== 'paper') {
      if (settings.feed !== 'live') return { ok: false, status: 'Betting needs the Live table feed' };
      if (!this.liveProfile?.betting) {
        return { ok: false, status: 'The table profile has no "betting" settings, so this table cannot be bet on (see docs/deployment.md, Betting)' };
      }
      if (!(settings.bet_max_stake > 0)) return { ok: false, status: 'Set the largest allowed stake before betting' };
      if (!(settings.bet_max_session >= settings.bet_max_stake)) return { ok: false, status: 'The session staking limit must be at least the largest stake' };
    }
    const made = PlayCore.create(settings);
    if ('error' in made) return { ok: false, status: made.error };
    const locked = dailyLockReason(riskConfig(settings), this.dayTotal(this.now()));
    if (locked) return { ok: false, status: locked };

    this.settings = settings;
    this.store.save(settings);
    this.beginSession();
    this.error = this.alertsWarning(settings);
    this.core = made.core;
    this.feed = this.createFeed();
    if (settings.feed === 'live') this.status = 'FEED WAITING - connecting to the table';
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
      settings.live_port = this.settings.live_port;
      settings.bet_mode = this.settings.bet_mode;
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
    this.store.save(settings);
    if (this.running) this.core?.configure(settings);
    return { ok: true, live: this.running };
  }

  stop(): void {
    if (!this.running) return;
    this.finish('STOPPED');
  }

  pause(): boolean {
    if (!this.running || this.paused) return this.running;
    if (this.bets.active) {
      this.bets.skip();
      if (this.bets.busy) {
        // A bet is on the table: pausing now would leave it unaccounted for, so the pause starts after its result.
        this.bets.requestPauseAfterResult();
        this.status = 'PAUSING - a bet is on the table';
        return true;
      }
    }
    this.paused = true;
    this.status = 'PAUSED';
    return true;
  }

  resume(): boolean {
    if (!this.running) return false;
    this.bets.takePauseRequest();
    if (this.paused || this.status.startsWith('PAUSING')) {
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

  /** The settings as last applied (the Backtest borrows them as defaults; it never changes them). */
  currentSettings(): Settings {
    return { ...this.settings };
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
    if (this.settings.csv_style === 'vn') {
      const table = this.reportTable();
      return table ? tableCsv(table, 'vn') : null;
    }
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
    const pnl = this.core?.pnlMicro ?? 0;
    const balance = toMicro(this.settings.start_balance) + pnl;
    return `Session #${this.sessionNo} (${this.settings.strategy}), ${this.records.length} hands, session ${signed(pnl)}, balance ${fmt2(balance)}`;
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

  /** A live table is attached: clears "waiting", and an earlier outage if there was one. */
  feedConnected(): void {
    if (!this.running) return;
    if (this.feedIsLost) this.feedRestored();
    else if (this.status.startsWith('FEED')) this.status = this.paused ? 'PAUSED' : 'RUNNING';
  }

  /** Still looking for the live table: shown in the status so the user knows what to do. */
  feedWaiting(reason: string): void {
    if (!this.running || this.feedIsLost || this.paused) return;
    this.status = `FEED WAITING - ${reason}`.slice(0, 400);
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
    this.bets.clear();
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
    this.core = null;
    this.feed = null;
  }

  private finish(status: string): void {
    this.feed?.stop();
    this.bets.clear();
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
      case 'live':
        return new LiveFeed(
          handler,
          { port: this.settings.live_port, profile: this.liveProfile!, profiles: this.liveProfiles, watchWindow: this.settings.bet_mode !== 'paper' },
          {
            connected: () => this.feedConnected(),
            waiting: (reason) => this.feedWaiting(reason),
            lost: (reason) => void this.feedLost(reason),
            shoeChange: () => this.onShoeChange(),
            windowOpen: () => this.bets.onWindowOpen(),
          },
        );
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

  /** A live table announced a new shoe (simulated and replayed feeds mark it on the first hand instead). */
  private onShoeChange(): void {
    const core = this.core;
    if (!this.running || !core) return;
    const counted = core.hands > 0;
    core.shoeChange();
    if (counted) {
      this.shoeNo++;
      this.notifier.notify('shoe_change', `TRADE247: shoe change, shoe #${this.shoeNo} starts. ${this.summary()}.`);
    }
  }

  private onHand(event: HandEvent): void {
    const core = this.core;
    if (!this.running || !core) return;
    const s = this.settings;
    // Capital guarding is an opt-in practice feature; it never controls the external table adapter.
    if (s.feed !== 'live' && s.paper_bankroll_guard) {
      if (event.newShoe) {
        this.onShoeChange();
        event = { ...event, newShoe: false };
      }
      const intended = core.intendedBet(this.paused);
      if (intended && intended.stakeMicro > toMicro(s.start_balance) + core.pnlMicro) {
        this.finish('STOPPED (SHORT OF STAKE)');
        return;
      }
    }
    if (core.startsNewShoe(event)) {
      this.shoeNo++;
      this.notifier.notify('shoe_change', `TRADE247: shoe change, shoe #${this.shoeNo} starts. ${this.summary()}.`);
    }

    // On a live table with betting on, what was placed must be what is settled (see betting.ts).
    const decision = this.bets.beforeHand(core, this.paused);
    if (decision.action === 'stop') return;

    const when = this.now();
    const played = core.play(event, this.paused || decision.forcePaused, this.dayTotal(when));
    const { res, hand } = played;
    const dayMicro = this.addToDay(played.pnlMicro, when);

    const row: Row = {
      n: this.records.length + 1,
      time: localTime(when),
      pick: played.pick,
      mode: played.mode,
      stake: fmtExact(played.stakeMicro),
      result: event.result,
      outcome: played.outcome,
      pattern: res.pattern,
      note: [res.note, hand.reason, played.keepAliveNote, decision.note].filter(Boolean).join(' · '),
      single_wins: res.singleWins,
      run_net_units: round2(res.runNetMicro / toMicro(s.unit_value)),
      session_pnl: fmtExact(core.pnlMicro),
      cards: event.cards,
      session: this.sessionNo,
    };
    const record: HandRecord = {
      row,
      strategy: s.strategy,
      pnlMicro: played.pnlMicro,
      balanceMicro: toMicro(s.start_balance) + core.pnlMicro,
    };
    this.records.push(record);
    this.writeHistory(record);

    if (this.running && this.bets.takePauseRequest()) {
      this.paused = true;
      this.status = 'PAUSED';
    }
    const stop = played.verdict.stop;
    if (stop) {
      this.finish(stop.status);
      if (stop.alert) {
        this.notifier.notify(
          stop.alert,
          `TRADE247: ${stop.message}. The day is locked: no new session until the limit is raised or tomorrow. ${this.summary()}, today ${signed(dayMicro)}.`,
        );
      }
    }
  }

  // ---- real betting (the coordinator does the work; see betting.ts)

  approveBet(): { ok: boolean; error?: string } {
    return this.bets.approve();
  }

  skipBet(): { ok: boolean; error?: string } {
    return this.bets.skip();
  }

  private betHost(): BetHost {
    return {
      settings: () => this.settings,
      core: () => this.core,
      now: () => this.now().getTime(),
      running: () => this.running,
      paused: () => this.paused,
      feedLost: () => this.feedIsLost,
      handsRecorded: () => this.records.length,
      random: () => Math.random(),
      place: (side, stake, shouldAbort) =>
        this.feed instanceof LiveFeed
          ? this.feed.place(side, stake, shouldAbort)
          : Promise.resolve({ kind: 'rejected' as const, reason: 'The table is not connected' }),
      stopForBet: (status, message) => {
        const summary = this.summary();
        this.finish(status);
        this.error = `Bet problem: ${message}`;
        this.notifier.notify('bet_problem', `TRADE247: ${message}. The session was stopped; check the table. ${summary}.`);
      },
    };
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
