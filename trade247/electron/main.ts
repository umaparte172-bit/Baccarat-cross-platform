import { mkdirSync } from 'node:fs';
import { createServer } from 'node:net';
import { join } from 'node:path';
import { app, BrowserWindow, dialog, Menu, powerSaveBlocker, safeStorage, shell, type MenuItemConstructorOptions } from 'electron';
import { SessionAwake } from './awake.js';
import { VERSION } from '../src/runtime.js';
import { buildApp } from '../src/server/app.js';
import { SessionManager } from '../src/session.js';
import { SettingsStore, type SecretBox } from '../src/store/files.js';
import { defaultDataDir } from '../src/store/paths.js';

/**
 * The desktop app: the same local server as the command-line program, shown in a window of its own.
 * The window loads the server's page on 127.0.0.1; it can reach nothing else, and nothing else can reach the server.
 */

const HOST = '127.0.0.1';
const PREFERRED_PORT = 8247;
const SMOKE = process.argv.includes('--smoke');

// The page and the backtest worker are plain files beside this bundle (inside the app's archive when packaged).
process.env.T247_ASSETS_DIR ??= join(__dirname, 'assets');

const dataDir = defaultDataDir();
mkdirSync(dataDir, { recursive: true });
// Chromium's own files (cache, cookies) stay out of the folder that holds the user's settings, logs and reports.
app.setName('TRADE247');
// WebDriver owns a temporary Chromium profile and uses it to complete its startup handshake.
// Only the explicit desktop test may override our usual per-user profile directory.
const testProfile = process.env.T247_SELENIUM_TEST === '1' ? app.commandLine.getSwitchValue('user-data-dir') : '';
app.setPath('userData', testProfile || join(dataDir, 'electron'));

if (!SMOKE && !app.requestSingleInstanceLock()) {
  app.quit();
  process.exit(0);
}

function pickPort(): Promise<number> {
  const tryListen = (port: number): Promise<number> =>
    new Promise((resolve, reject) => {
      const probe = createServer();
      probe.once('error', reject);
      probe.listen(port, HOST, () => {
        const bound = (probe.address() as { port: number }).port;
        probe.close(() => resolve(bound));
      });
    });
  return tryListen(PREFERRED_PORT).catch(() => tryListen(0));
}

/** The system keychain (Keychain on macOS, DPAPI on Windows) seals the Telegram token on disk. */
async function keychain(): Promise<SecretBox | undefined> {
  if (!(await safeStorage.isAsyncEncryptionAvailable())) return undefined;
  return {
    encrypt: (plain) => safeStorage.encryptString(plain).toString('base64'),
    decrypt: (sealed) => {
      try {
        return safeStorage.decryptString(Buffer.from(sealed, 'base64'));
      } catch {
        return null;
      }
    },
  };
}

function buildMenu(origin: string): void {
  const mac = process.platform === 'darwin';
  const template: MenuItemConstructorOptions[] = [
    ...(mac ? [{ role: 'appMenu' } as MenuItemConstructorOptions] : []),
    {
      label: 'File',
      submenu: [
        { label: 'Open data folder', click: () => void shell.openPath(dataDir) },
        // The same server the window shows is reachable from any browser on this computer.
        { label: `Open in browser (${origin})`, click: () => void shell.openExternal(`${origin}/`) },
        { type: 'separator' },
        mac ? { role: 'close' } : { role: 'quit' },
      ],
    },
    { role: 'editMenu' },
    { label: 'View', submenu: [{ role: 'reload' }, { role: 'resetZoom' }, { role: 'zoomIn' }, { role: 'zoomOut' }, { role: 'togglefullscreen' }] },
    { role: 'windowMenu' },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

async function start(): Promise<void> {
  const port = await pickPort();
  const origin = `http://${HOST}:${port}`;
  const manager = new SessionManager(new SettingsStore(dataDir, await keychain()));
  const server = buildApp({
    dataDir,
    port,
    manager,
    logger: false,
    openFile: process.env.T247_OPEN_REPORTS === '0' ? undefined : (path) => void shell.openPath(path),
  });
  await server.listen({ host: HOST, port });
  // Same as the command-line program: the server is running and can also be opened in a browser.
  console.log(`TRADE247-V1 ${VERSION} is running at ${origin}/ (desktop window opening; the address also works in a browser)`);
  console.log(`Data folder: ${dataDir}`);
  const awake = new SessionAwake(powerSaveBlocker);
  const awakeTimer = setInterval(() => awake.update(manager.isRunning()), 1000);
  awakeTimer.unref();

  buildMenu(origin);
  const win = new BrowserWindow({
    width: 1360,
    height: 860,
    minWidth: 980,
    minHeight: 640,
    backgroundColor: '#0b1220',
    icon: join(__dirname, 'assets', 'icon.png'),
    title: `TRADE247-V1 ${VERSION} - ${origin}`,
    show: !SMOKE,
    webPreferences: { contextIsolation: true, nodeIntegration: false, sandbox: true, webSecurity: true, devTools: !app.isPackaged },
  });
  win.setMenuBarVisibility(false);

  // The window shows this one page. Links open in the system browser (https only); nothing navigates away.
  win.webContents.setWindowOpenHandler(({ url }) => {
    if (url.startsWith('https://')) void shell.openExternal(url);
    return { action: 'deny' };
  });
  win.webContents.on('will-navigate', (event, url) => {
    if (!url.startsWith(origin + '/')) event.preventDefault();
  });
  win.webContents.session.setPermissionRequestHandler((_wc, _permission, callback) => callback(false));

  // Closing the window ends the program, and with it a running session: ask first.
  let quitting = false;
  win.on('close', (event) => {
    if (quitting || SMOKE) return;
    if (manager.getState(0).running) {
      const choice = dialog.showMessageBoxSync(win, {
        type: 'warning',
        buttons: ['Keep running', 'Quit and stop the session'],
        defaultId: 0,
        cancelId: 0,
        message: 'A session is running.',
        detail: 'Closing TRADE247 stops the bot. Use PAUSE or STOP in the page first if you want a tidy end.',
      });
      if (choice === 0) {
        event.preventDefault();
        return;
      }
    }
    quitting = true;
  });
  app.on('before-quit', () => {
    quitting = true;
  });

  const shutdown = async (): Promise<void> => {
    clearInterval(awakeTimer);
    awake.update(false);
    manager.stop();
    await manager.flushAlerts().catch(() => {});
    await server.close();
  };
  app.once('will-quit', (event) => {
    event.preventDefault();
    void shutdown().finally(() => app.exit(0));
  });

  await win.loadURL(`${origin}/`);
  if (SMOKE) await smoke(win, origin);
}

/** Checks that the packaged app works end to end: the page, the API, a replay, and a backtest on worker threads. */
async function smoke(win: BrowserWindow, origin: string): Promise<void> {
  const checks: Array<[string, boolean, string?]> = [];
  const check = (name: string, ok: boolean, detail?: string): void => {
    checks.push([name, ok, detail]);
  };
  const api = async (path: string, payload?: unknown): Promise<any> => {
    const r = await fetch(origin + path, {
      method: payload === undefined ? 'GET' : 'POST',
      headers: { 'x-t247': '1', 'content-type': 'application/json' },
      body: payload === undefined ? undefined : JSON.stringify(payload),
    });
    return r.json();
  };
  try {
    const page = (await win.webContents.executeJavaScript(
      `({ title: document.title, options: document.querySelectorAll('#strategy option').length, header: !!document.querySelector('#start') })`,
    )) as { title: string; options: number; header: boolean };
    check('the window shows the page', page.title.includes('TRADE247') && page.header, JSON.stringify(page));
    check('the page lists the classic strategies', page.options >= 10, String(page.options));
    check('the page has no Node access', (await win.webContents.executeJavaScript('typeof require === "undefined" && typeof process === "undefined"')) === true);

    const started = await api('/api/start', { settings: { feed: 'csv', csv_text: 'PBBPPBTPBB', strategy: 'ODD_DIFF', progression: 'list', chain: [1, 2, 3], unit_value: 1, min_chip: 0, start_balance: 1000 } });
    check('starts a replay session with a classic strategy', started.ok === true, JSON.stringify(started));
    let state: any = null;
    for (let i = 0; i < 100 && !(state && !state.running); i++) {
      await new Promise((r) => setTimeout(r, 100));
      state = await api('/api/state?since=0');
    }
    check('plays every hand and stops', state?.hands === 10 && String(state.status).startsWith('STOPPED'), `${state?.hands} ${state?.status}`);

    const job = await api('/api/backtest/start', { settings: { unit_value: 1 }, params: { source: 'sim', shoes: 10, trials: 4, bankroll_units: 0, seed: 5 } });
    let bt: any = job;
    for (let i = 0; i < 300 && job.ok && !bt.finished; i++) {
      await new Promise((r) => setTimeout(r, 100));
      bt = await api(`/api/backtest/status?id=${job.id}`);
    }
    check('runs a backtest on worker threads', bt.ok === true && bt.done === 4, JSON.stringify(bt).slice(0, 200));
    check('knows whether the keychain is available', typeof (await safeStorage.isAsyncEncryptionAvailable()) === 'boolean');
  } catch (e) {
    check('smoke test completed without an error', false, (e as Error).stack ?? String(e));
  }
  for (const [name, ok, detail] of checks) console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok || !detail ? '' : `  (${detail})`}`);
  const failed = checks.filter(([, ok]) => !ok).length;
  console.log(failed === 0 ? `${checks.length}/${checks.length} checks passed` : `${failed} check(s) failed`);
  app.exit(failed === 0 ? 0 : 1);
}

void app.whenReady().then(start).catch((e) => {
  console.error(e instanceof Error ? e.message : String(e));
  app.exit(1);
});

app.on('window-all-closed', () => {
  if (!SMOKE) app.quit();
});

// A second launch brings the first window forward instead of starting a second bot.
app.on('second-instance', () => {
  const [win] = BrowserWindow.getAllWindows();
  if (win) {
    if (win.isMinimized()) win.restore();
    win.focus();
  }
});
