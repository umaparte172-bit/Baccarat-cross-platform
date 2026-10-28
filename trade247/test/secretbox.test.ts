import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { DEFAULT_SETTINGS } from '../src/engine/settings.js';
import { SettingsStore, type SecretBox } from '../src/store/files.js';

const dirs: string[] = [];
const tmp = (): string => {
  const d = mkdtempSync(join(tmpdir(), 't247-box-'));
  dirs.push(d);
  return d;
};
afterEach(() => {
  for (const d of dirs.splice(0)) rmSync(d, { recursive: true, force: true });
});

const TOKEN = '123456789:AAHexampleexampleexampleexample_12345';
/** A stand-in for the system keychain: reversible, and clearly not plain text. */
const box: SecretBox = {
  encrypt: (plain) => Buffer.from([...plain].reverse().join('')).toString('base64'),
  decrypt: (sealed) => {
    try {
      return [...Buffer.from(sealed, 'base64').toString('utf8')].reverse().join('');
    } catch {
      return null;
    }
  },
};
const secretsFile = (dir: string): string => readFileSync(join(dir, 'secrets.json'), 'utf8');

describe('sealing the Telegram token on disk', () => {
  it('stores it sealed, never as plain text, and reads it back', () => {
    const dir = tmp();
    const store = new SettingsStore(dir, box);
    store.save({ ...DEFAULT_SETTINGS, telegram_token: TOKEN });
    const raw = secretsFile(dir);
    expect(raw).not.toContain(TOKEN);
    expect(raw).toContain('telegram_token_sealed');
    expect(new SettingsStore(dir, box).load().telegram_token).toBe(TOKEN);
    expect(readFileSync(join(dir, 'settings.json'), 'utf8')).not.toContain('telegram_token');
  });

  it('reads a plain-text token left by an older version, and replaces it with a sealed one on the next save', () => {
    const dir = tmp();
    writeFileSync(join(dir, 'secrets.json'), JSON.stringify({ telegram_token: TOKEN }));
    const store = new SettingsStore(dir, box);
    const loaded = store.load();
    expect(loaded.telegram_token).toBe(TOKEN);
    store.save(loaded);
    expect(secretsFile(dir)).not.toContain(TOKEN);
    expect(new SettingsStore(dir, box).load().telegram_token).toBe(TOKEN);
  });

  it('a token that cannot be unsealed (another user or computer) reads as empty, not as an error', () => {
    const dir = tmp();
    new SettingsStore(dir, box).save({ ...DEFAULT_SETTINGS, telegram_token: TOKEN });
    const broken: SecretBox = { encrypt: box.encrypt, decrypt: () => null };
    expect(new SettingsStore(dir, broken).load().telegram_token).toBe('');
  });

  it('clearing the token clears the sealed copy too', () => {
    const dir = tmp();
    const store = new SettingsStore(dir, box);
    store.save({ ...DEFAULT_SETTINGS, telegram_token: TOKEN });
    store.save({ ...DEFAULT_SETTINGS, telegram_token: '' });
    expect(new SettingsStore(dir, box).load().telegram_token).toBe('');
  });

  it('without a keychain the token is kept as before', () => {
    const dir = tmp();
    new SettingsStore(dir).save({ ...DEFAULT_SETTINGS, telegram_token: TOKEN });
    expect(secretsFile(dir)).toContain(TOKEN);
    expect(new SettingsStore(dir).load().telegram_token).toBe(TOKEN);
  });
});
