import { chmodSync, existsSync, mkdirSync, readFileSync, renameSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { DEFAULT_SETTINGS, normalizeSettings, type Settings } from '../engine/settings.js';

interface Meta {
  sessionCounter: number;
}

/** Profit/loss of all sessions on one local calendar day. */
export interface DayTotal {
  /** Local date, YYYY-MM-DD. */
  day: string;
  pnlMicro: number;
}

export function localDay(d: Date): string {
  const p = (n: number): string => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

function readJson<T>(path: string): T | null {
  try {
    return JSON.parse(readFileSync(path, 'utf8')) as T;
  } catch {
    return null;
  }
}

/** Write via a temp file and rename, so a crash never leaves a half-written file. */
function writeJsonAtomic(path: string, value: unknown, mode?: number): void {
  mkdirSync(dirname(path), { recursive: true });
  const tmp = `${path}.tmp`;
  writeFileSync(tmp, JSON.stringify(value, null, 2), { encoding: 'utf8', mode });
  renameSync(tmp, path);
  if (mode !== undefined && process.platform !== 'win32') chmodSync(path, mode);
}

/** Settings and the Telegram token live in separate files; only the settings file is safe to share. */
export class SettingsStore {
  private readonly settingsPath: string;
  private readonly secretsPath: string;
  private readonly metaPath: string;

  constructor(readonly dataDir: string) {
    this.settingsPath = join(dataDir, 'settings.json');
    this.secretsPath = join(dataDir, 'secrets.json');
    this.metaPath = join(dataDir, 'meta.json');
  }

  get logsDir(): string {
    return join(this.dataDir, 'logs');
  }

  load(): Settings {
    const saved = readJson<Record<string, unknown>>(this.settingsPath) ?? {};
    const secrets = readJson<{ telegram_token?: string }>(this.secretsPath) ?? {};
    return normalizeSettings({ ...saved, telegram_token: secrets.telegram_token ?? '' }, DEFAULT_SETTINGS).settings;
  }

  save(settings: Settings): void {
    const { telegram_token, ...rest } = settings;
    writeJsonAtomic(this.settingsPath, rest);
    if (telegram_token || existsSync(this.secretsPath)) writeJsonAtomic(this.secretsPath, { telegram_token }, 0o600);
  }

  /** The saved day total, or an empty total for today when there is none (or it is from another day). */
  loadDay(today: string): DayTotal {
    const saved = readJson<DayTotal>(join(this.dataDir, 'day.json'));
    if (saved && saved.day === today && Number.isFinite(saved.pnlMicro)) return saved;
    return { day: today, pnlMicro: 0 };
  }

  saveDay(total: DayTotal): void {
    writeJsonAtomic(join(this.dataDir, 'day.json'), total);
  }

  nextSessionNumber(): number {
    const meta = readJson<Meta>(this.metaPath) ?? { sessionCounter: 0 };
    const next = (Number.isInteger(meta.sessionCounter) ? meta.sessionCounter : 0) + 1;
    writeJsonAtomic(this.metaPath, { sessionCounter: next });
    return next;
  }
}
