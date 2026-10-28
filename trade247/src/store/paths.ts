import { homedir } from 'node:os';
import { join } from 'node:path';

/** Per-user data folder: settings, secrets, logs. T247_DATA_DIR overrides it (used by tests). */
export function defaultDataDir(): string {
  const override = process.env.T247_DATA_DIR;
  if (override) return override;
  if (process.platform === 'win32') return join(process.env.APPDATA ?? join(homedir(), 'AppData', 'Roaming'), 'Trade247');
  if (process.platform === 'darwin') return join(homedir(), 'Library', 'Application Support', 'Trade247');
  return join(process.env.XDG_DATA_HOME ?? join(homedir(), '.local', 'share'), 'trade247');
}
