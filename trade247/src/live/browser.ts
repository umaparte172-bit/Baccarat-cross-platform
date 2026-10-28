import { spawn } from 'node:child_process';
import { existsSync, mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { listPages } from './cdp.js';

/**
 * Opens the browser window the Live table feed reads: Chrome (or Edge) with a debugging port on this computer only
 * and a profile of its own, so the user's normal browser is left alone. The same thing the "Start-Chrome-for-Trade247"
 * launcher scripts do, from inside the app.
 */

export interface BrowserLaunch {
  ok: boolean;
  /** A browser was already listening on the port, so nothing was started. */
  already?: boolean;
  error?: string;
}

export function findBrowser(env: NodeJS.ProcessEnv = process.env, exists: (p: string) => boolean = existsSync): string | null {
  const pf = env.ProgramFiles ?? 'C:\\Program Files';
  const pf86 = env['ProgramFiles(x86)'] ?? 'C:\\Program Files (x86)';
  const local = env.LOCALAPPDATA ?? '';
  const candidates =
    process.platform === 'win32'
      ? [
          join(pf, 'Google/Chrome/Application/chrome.exe'),
          join(pf86, 'Google/Chrome/Application/chrome.exe'),
          local ? join(local, 'Google/Chrome/Application/chrome.exe') : '',
          join(pf86, 'Microsoft/Edge/Application/msedge.exe'),
          join(pf, 'Microsoft/Edge/Application/msedge.exe'),
        ]
      : process.platform === 'darwin'
        ? ['/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge']
        : ['/usr/bin/google-chrome', '/usr/bin/google-chrome-stable', '/usr/bin/chromium', '/usr/bin/chromium-browser', '/usr/bin/microsoft-edge'];
  return candidates.find((c) => c !== '' && exists(c)) ?? null;
}

export interface LaunchOptions {
  port: number;
  /** Folder for the browser's own profile (cookies, sign-ins); created if missing. */
  profileDir: string;
  /** Test hooks. */
  executable?: string | null;
  extraArgs?: string[];
  waitMs?: number;
  spawnFn?: typeof spawn;
}

const sleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

async function listening(port: number): Promise<boolean> {
  try {
    await listPages(port, 1200);
    return true;
  } catch {
    return false;
  }
}

export async function openTableBrowser(options: LaunchOptions): Promise<BrowserLaunch & { pid?: number }> {
  const { port, profileDir } = options;
  if (!Number.isInteger(port) || port < 1 || port > 65535) return { ok: false, error: 'The port must be a number between 1 and 65535' };
  if (await listening(port)) return { ok: true, already: true };

  const executable = options.executable === undefined ? findBrowser() : options.executable;
  if (!executable) return { ok: false, error: 'Chrome was not found. Install Google Chrome (or Microsoft Edge) and try again' };

  try {
    mkdirSync(profileDir, { recursive: true });
  } catch (e) {
    return { ok: false, error: `Cannot create the browser profile folder: ${(e as Error).message}` };
  }

  const args = [
    `--remote-debugging-port=${port}`,
    '--remote-debugging-address=127.0.0.1',
    `--user-data-dir=${profileDir}`,
    '--no-first-run',
    '--no-default-browser-check',
    ...(options.extraArgs ?? []),
  ];
  let pid: number | undefined;
  try {
    const child = (options.spawnFn ?? spawn)(executable, args, { detached: true, stdio: 'ignore' });
    child.on('error', () => {});
    pid = child.pid;
    child.unref();
  } catch (e) {
    return { ok: false, error: `Could not start the browser: ${(e as Error).message}` };
  }

  const deadline = Date.now() + (options.waitMs ?? 10_000);
  while (Date.now() < deadline) {
    await sleep(250);
    if (await listening(port)) return { ok: true, pid };
  }
  return {
    ok: false,
    error:
      'The browser opened but is not listening on the debugging port. Close every browser window that uses the TRADE247 profile ' +
      `(${profileDir}) and try again`,
  };
}
