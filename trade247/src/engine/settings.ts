export type StrategyName = 'IHG' | 'AME' | 'JJ';
export type Progression = 'recovery' | 'list' | 'flat' | 'dynamic';
export type FeedName = 'simulator' | 'manual' | 'csv' | 'live';

export interface Settings {
  strategy: StrategyName;
  progression: Progression;
  unit_value: number;
  chain: number[];
  keepalive_enabled: boolean;
  keepalive_amount: number;
  entry_loss_filter: number;
  take_profit: number;
  stop_loss: number;

  min_chip: number;
  start_balance: number;
  max_bet_mode: 'off' | 'doc' | 'custom';
  max_bet_units: number;
  hard_cap_mode: 'off' | 'custom';
  hard_cap_units: number;
  daily_stop_loss: number;
  daily_take_profit: number;

  single_wins_mode: 'off' | 'doc' | 'custom';
  single_wins_limit: number;
  keepalive_idle: number;
  round_step: number;

  cycle_preset: 'JJ20' | 'JJ30' | 'JJ50' | 'JJ70' | 'JJ80' | 'custom';
  cycle_hands: number;
  cycle_max_mult: number;
  cycle_pct_min: number;
  cycle_pct_max: number;
  cycle_max_hands: number;
  cycle_ramp: number;

  ame_window: number;
  ame_min_confidence: number;
  ame_freeze_hands: number;
  ame_stable_units: number;
  ame_recovery_rate: number;
  ame_max_step: number;
  ame_chaos_entropy: number;
  ame_chaos_fit: number;

  stop_consec_loss_count: number;
  stop_consec_loss_action: 'STOP BET' | 'WAIT FOR PATTERN';
  pause_consec_loss_count: number;
  pause_bet_count: number;
  shoe_change_policy: 'reset_pattern' | 'reset_all' | 'continue';
  end_shoe_policy: 'no_new_runs' | 'cut_run' | 'off';
  end_shoe_hand: number;

  feed: FeedName;
  sim_interval: number;
  sim_seed: number;
  csv_text: string;
  csv_path: string;
  /** Live feed: the remote debugging port of the Chrome the user opened the table in. */
  live_port: number;

  telegram_token: string;
  telegram_chat_id: string;
}

// ame_* recovery/max-step and the JJ ramp are placeholders until the client supplies the formulas.
export const DEFAULT_SETTINGS: Settings = {
  strategy: 'IHG',
  progression: 'recovery',
  unit_value: 0.1,
  chain: [],
  keepalive_enabled: false,
  keepalive_amount: 0.1,
  entry_loss_filter: 0,
  take_profit: 0,
  stop_loss: 0,

  min_chip: 0.1,
  start_balance: 1000,
  max_bet_mode: 'doc',
  max_bet_units: 45,
  hard_cap_mode: 'off',
  hard_cap_units: 200,
  daily_stop_loss: 0,
  daily_take_profit: 0,

  single_wins_mode: 'doc',
  single_wins_limit: 7,
  keepalive_idle: 10,
  round_step: 1,

  cycle_preset: 'JJ20',
  cycle_hands: 10,
  cycle_max_mult: 2,
  cycle_pct_min: 5,
  cycle_pct_max: 20,
  cycle_max_hands: 60,
  cycle_ramp: 1,

  ame_window: 12,
  ame_min_confidence: 70,
  ame_freeze_hands: 4,
  ame_stable_units: 1,
  ame_recovery_rate: 0.5,
  ame_max_step: 3,
  ame_chaos_entropy: 0.92,
  ame_chaos_fit: 0.7,

  stop_consec_loss_count: 0,
  stop_consec_loss_action: 'STOP BET',
  pause_consec_loss_count: 0,
  pause_bet_count: 0,
  shoe_change_policy: 'reset_all',
  end_shoe_policy: 'off',
  end_shoe_hand: 60,

  feed: 'simulator',
  sim_interval: 1,
  sim_seed: 0,
  csv_text: '',
  csv_path: '',
  live_port: 9014,

  telegram_token: '',
  telegram_chat_id: '',
};

/** What the server shows instead of a stored Telegram token. */
export const TOKEN_MASK = '********';

type Raw = Record<string, unknown>;

function num(raw: unknown, fallback: number, min: number, max: number): number {
  const parsed = typeof raw === 'number' ? raw : typeof raw === 'string' && raw.trim() !== '' ? Number(raw) : NaN;
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(max, Math.max(min, parsed));
}

const int = (raw: unknown, fallback: number, min: number, max: number): number =>
  Math.round(num(raw, fallback, min, max));

function pick<T extends string>(raw: unknown, allowed: readonly T[], fallback: T): T {
  return typeof raw === 'string' && (allowed as readonly string[]).includes(raw) ? (raw as T) : fallback;
}

const text = (raw: unknown, fallback: string, max: number): string =>
  typeof raw === 'string' ? raw.slice(0, max) : fallback;

function flag(raw: unknown, fallback: boolean): boolean {
  if (typeof raw === 'boolean') return raw;
  if (raw === 'true') return true;
  if (raw === 'false') return false;
  return fallback;
}

function chainOf(raw: unknown, fallback: number[]): number[] {
  if (!Array.isArray(raw)) return fallback;
  const out = raw.map((v) => Number(v)).filter((v) => Number.isFinite(v) && v > 0 && v <= 1_000_000);
  return out.slice(0, 64);
}

export interface Normalized {
  settings: Settings;
  errors: string[];
}

/**
 * Turn whatever the page posted into a complete, valid Settings object.
 * Missing keys keep their previous value. Hard errors are reported; everything else is clamped.
 */
export function normalizeSettings(input: unknown, previous: Settings = DEFAULT_SETTINGS): Normalized {
  const r: Raw = input && typeof input === 'object' ? (input as Raw) : {};
  const p = previous;
  const errors: string[] = [];
  const has = (k: string): boolean => k in r;
  const g = <K extends keyof Settings>(k: K): unknown => (has(k) ? r[k] : p[k]);

  const s: Settings = {
    strategy: pick(g('strategy'), ['IHG', 'AME', 'JJ'], p.strategy),
    progression: pick(g('progression'), ['recovery', 'list', 'flat', 'dynamic'], p.progression),
    unit_value: num(g('unit_value'), p.unit_value, 0, 1_000_000),
    chain: chainOf(g('chain'), p.chain),
    keepalive_enabled: flag(g('keepalive_enabled'), p.keepalive_enabled),
    keepalive_amount: num(g('keepalive_amount'), p.keepalive_amount, 0, 1_000_000),
    entry_loss_filter: int(g('entry_loss_filter'), p.entry_loss_filter, 0, 99),
    take_profit: num(g('take_profit'), p.take_profit, 0, 1e9),
    stop_loss: num(g('stop_loss'), p.stop_loss, 0, 1e9),

    min_chip: num(g('min_chip'), p.min_chip, 0, 1_000_000),
    start_balance: num(g('start_balance'), p.start_balance, 0, 1e9),
    max_bet_mode: pick(g('max_bet_mode'), ['off', 'doc', 'custom'], p.max_bet_mode),
    max_bet_units: num(g('max_bet_units'), p.max_bet_units, 1, 1e6),
    hard_cap_mode: pick(g('hard_cap_mode'), ['off', 'custom'], p.hard_cap_mode),
    hard_cap_units: num(g('hard_cap_units'), p.hard_cap_units, 1, 1e6),
    daily_stop_loss: num(g('daily_stop_loss'), p.daily_stop_loss, 0, 1e9),
    daily_take_profit: num(g('daily_take_profit'), p.daily_take_profit, 0, 1e9),

    single_wins_mode: pick(g('single_wins_mode'), ['off', 'doc', 'custom'], p.single_wins_mode),
    single_wins_limit: int(g('single_wins_limit'), p.single_wins_limit, 1, 10_000),
    keepalive_idle: int(g('keepalive_idle'), p.keepalive_idle, 1, 10_000),
    round_step: num(g('round_step'), p.round_step, 0, 1000),

    cycle_preset: pick(g('cycle_preset'), ['JJ20', 'JJ30', 'JJ50', 'JJ70', 'JJ80', 'custom'], p.cycle_preset),
    cycle_hands: int(g('cycle_hands'), p.cycle_hands, 1, 100_000),
    cycle_max_mult: num(g('cycle_max_mult'), p.cycle_max_mult, 0, 10_000),
    cycle_pct_min: num(g('cycle_pct_min'), p.cycle_pct_min, 0, 100_000),
    cycle_pct_max: num(g('cycle_pct_max'), p.cycle_pct_max, 0, 100_000),
    cycle_max_hands: int(g('cycle_max_hands'), p.cycle_max_hands, 0, 1_000_000),
    cycle_ramp: int(g('cycle_ramp'), p.cycle_ramp, 1, 10_000),

    ame_window: int(g('ame_window'), p.ame_window, 6, 500),
    ame_min_confidence: num(g('ame_min_confidence'), p.ame_min_confidence, 0, 100),
    ame_freeze_hands: int(g('ame_freeze_hands'), p.ame_freeze_hands, 1, 10),
    ame_stable_units: num(g('ame_stable_units'), p.ame_stable_units, 0.5, 10_000),
    ame_recovery_rate: num(g('ame_recovery_rate'), p.ame_recovery_rate, 0, 100),
    ame_max_step: num(g('ame_max_step'), p.ame_max_step, 0, 10_000),
    ame_chaos_entropy: num(g('ame_chaos_entropy'), p.ame_chaos_entropy, 0, 1),
    ame_chaos_fit: num(g('ame_chaos_fit'), p.ame_chaos_fit, 0, 1),

    stop_consec_loss_count: int(g('stop_consec_loss_count'), p.stop_consec_loss_count, 0, 10_000),
    stop_consec_loss_action: pick(g('stop_consec_loss_action'), ['STOP BET', 'WAIT FOR PATTERN'], p.stop_consec_loss_action),
    pause_consec_loss_count: int(g('pause_consec_loss_count'), p.pause_consec_loss_count, 0, 10_000),
    pause_bet_count: int(g('pause_bet_count'), p.pause_bet_count, 0, 10_000),
    shoe_change_policy: pick(g('shoe_change_policy'), ['reset_pattern', 'reset_all', 'continue'], p.shoe_change_policy),
    end_shoe_policy: pick(g('end_shoe_policy'), ['no_new_runs', 'cut_run', 'off'], p.end_shoe_policy),
    end_shoe_hand: int(g('end_shoe_hand'), p.end_shoe_hand, 1, 10_000),

    feed: pick(g('feed'), ['simulator', 'manual', 'csv', 'live'], p.feed),
    sim_interval: num(g('sim_interval'), p.sim_interval, 0, 3600),
    sim_seed: int(g('sim_seed'), p.sim_seed, 0, 2 ** 31 - 1),
    csv_text: text(g('csv_text'), p.csv_text, 200_000),
    csv_path: text(g('csv_path'), p.csv_path, 1024),
    live_port: int(g('live_port'), p.live_port, 1, 65535),

    // The mask means "unchanged"; the real token never round-trips through the page.
    telegram_token: text(g('telegram_token'), p.telegram_token, 256),
    telegram_chat_id: text(g('telegram_chat_id'), p.telegram_chat_id, 64).trim(),
  };
  if (s.telegram_token === TOKEN_MASK) s.telegram_token = p.telegram_token;

  if (!(s.unit_value > 0)) errors.push('Base Unit must be above 0');
  if (!(s.start_balance > 0)) errors.push('Start balance must be above 0');
  if (s.progression === 'list' && s.chain.length === 0) errors.push('Enter a progression sequence, e.g. 0.1, 0.2, 0.4, 0.8');
  if (s.cycle_pct_max < s.cycle_pct_min) errors.push('JJ "up to %" must not be below "Increase from %"');
  if (s.pause_consec_loss_count > 0 && s.pause_bet_count === 0) errors.push('Enter how many hands to pause for ("Pause for (hands)")');
  if (s.keepalive_enabled && s.keepalive_amount <= 0) errors.push('Keep-Alive amount must be above 0');
  return { settings: s, errors };
}

/** Settings as shown to the page: the token is masked. */
export function publicSettings(s: Settings): Settings {
  return { ...s, telegram_token: s.telegram_token ? TOKEN_MASK : '' };
}
