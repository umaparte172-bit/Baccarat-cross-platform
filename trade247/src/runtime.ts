import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { getAsset, isSea } from 'node:sea';

// Replaced by the build script (tools/build-app.mjs) with the version from package.json.
declare const __APP_VERSION__: string | undefined;

/** True when running as the single-file executable produced by `npm run package`. */
export const packaged: boolean = isSea();

function developmentVersion(): string {
  try {
    const pkg = JSON.parse(readFileSync(fileURLToPath(new URL('../package.json', import.meta.url)), 'utf8')) as { version?: string };
    return pkg.version ?? 'dev';
  } catch {
    return 'dev';
  }
}

export const VERSION: string = typeof __APP_VERSION__ === 'string' ? __APP_VERSION__ : developmentVersion();

/** The desktop app (Electron) ships the page and the worker as plain files and points the server at them. */
const assetsDir = (): string | null => process.env.T247_ASSETS_DIR || null;

/** The page the server serves: embedded in the executable, shipped beside the desktop app, or read from ui/ when running from source. */
export function uiHtml(): string {
  const dir = assetsDir();
  if (dir) return readFileSync(join(dir, 'index.html'), 'utf8');
  if (packaged) return getAsset('index.html', 'utf8');
  return readFileSync(fileURLToPath(new URL('../ui/index.html', import.meta.url)), 'utf8');
}

/** The backtest worker's code when it is embedded in the executable (worker threads cannot open embedded files). */
export function embeddedWorkerSource(): string | null {
  const dir = assetsDir();
  if (dir) return readFileSync(join(dir, 'worker.js'), 'utf8');
  return packaged ? getAsset('worker.js', 'utf8') : null;
}
