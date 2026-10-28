import { execFile } from 'node:child_process';
import { existsSync, mkdirSync, readdirSync, statSync, unlinkSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const KEEP_SNAPSHOTS = 20;
const SNAPSHOT_NAME = /^trade247_report_\d{8}_\d{6}(_\d+)?\.(csv|xlsx)$/;

const pad = (n: number): string => String(n).padStart(2, '0');

export function reportsDir(dataDir: string): string {
  return join(dataDir, 'reports');
}

/**
 * Save the report as it is right now. Every snapshot gets its own file name because Excel keeps an
 * opened file locked, so "open a newer copy" must never try to overwrite the one still on screen.
 */
export function writeSnapshot(dataDir: string, ext: 'csv' | 'xlsx', data: string | Buffer, now: Date): string {
  const dir = reportsDir(dataDir);
  mkdirSync(dir, { recursive: true });
  const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}_${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;
  let path = join(dir, `trade247_report_${stamp}.${ext}`);
  for (let n = 1; existsSync(path); n++) path = join(dir, `trade247_report_${stamp}_${n}.${ext}`);
  writeFileSync(path, data, { flag: 'wx' });
  prune(dir);
  return path;
}

/** Keep the newest few snapshots. A file Excel still holds open simply stays. */
function prune(dir: string): void {
  try {
    const files = readdirSync(dir)
      .filter((f) => SNAPSHOT_NAME.test(f))
      .map((f) => ({ path: join(dir, f), time: statSync(join(dir, f)).mtimeMs }))
      .sort((a, b) => b.time - a.time);
    for (const old of files.slice(KEEP_SNAPSHOTS)) {
      try {
        unlinkSync(old.path);
      } catch {
        // locked or already gone: leave it
      }
    }
  } catch {
    // the folder is only a convenience
  }
}

/** Open a file we just wrote in the user's default application. No shell is involved. */
export function openWithDefaultApp(path: string): void {
  const [command, args]: [string, string[]] =
    process.platform === 'win32' ? ['explorer.exe', [path]]
    : process.platform === 'darwin' ? ['open', [path]]
    : ['xdg-open', [path]];
  // explorer.exe reports exit code 1 even when it worked, so the exit status is not used.
  execFile(command, args, { windowsHide: true }, () => {});
}
