// Builds the desktop app (Electron):
//   node tools/build-electron.mjs          ->  build/electron/  (run it with: electron build/electron/main.cjs)
//   node tools/build-electron.mjs --dist   ->  also packages it for THIS operating system into release/desktop/
//
// The server, the strategies and the backtest worker are bundled into plain files, so the app needs no
// node_modules at run time. A Mac app has to be built on a Mac (CI does that), a Windows app on Windows.

import { execFileSync } from 'node:child_process';
import { chmodSync, copyFileSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const out = join(root, 'build', 'electron');
const dist = process.argv.includes('--dist');
const log = (msg) => console.log(`[electron] ${msg}`);

rmSync(out, { recursive: true, force: true });
mkdirSync(join(out, 'assets'), { recursive: true });

const common = {
  bundle: true,
  platform: 'node',
  target: 'node22',
  format: 'cjs',
  legalComments: 'none',
  logLevel: 'warning',
  define: { __APP_VERSION__: JSON.stringify(pkg.version), 'import.meta.url': '__importMetaUrl' },
  banner: { js: "const __importMetaUrl = typeof __filename === 'string' ? require('node:url').pathToFileURL(__filename).href : 'file:///';" },
};
log(`bundling ${pkg.name} ${pkg.version}`);
await build({ ...common, entryPoints: [join(root, 'electron/main.ts')], outfile: join(out, 'main.cjs'), external: ['electron'] });
await build({ ...common, entryPoints: [join(root, 'src/backtest/worker.ts')], outfile: join(out, 'assets/worker.js') });
copyFileSync(join(root, 'ui/index.html'), join(out, 'assets/index.html'));
copyFileSync(join(root, 'build-resources/icon.png'), join(out, 'assets/icon.png'));

// Selenium's helper that finds (and if needed downloads) a ChromeDriver matching the user's Chrome. It is a program,
// so it cannot run from inside the bundle or the asar archive: it ships beside them (see asarUnpack).
const managerDir = { win32: 'windows', darwin: 'macos', linux: 'linux-x86_64' }[process.platform];
const managerFile = process.platform === 'win32' ? 'selenium-manager.exe' : 'selenium-manager';
mkdirSync(join(out, 'assets/selenium-manager'), { recursive: true });
copyFileSync(join(root, 'node_modules/selenium-webdriver/bin', managerDir, managerFile), join(out, 'assets/selenium-manager', managerFile));
if (process.platform !== 'win32') chmodSync(join(out, 'assets/selenium-manager', managerFile), 0o755);
// The installer goes into the DMG as it is in the checkout; make sure it is executable there.
if (process.platform === 'darwin') chmodSync(join(root, 'packaging/macos/Install-TRADE247.command'), 0o755);

// What the packager sees: the program only, with no dependencies of its own.
writeFileSync(
  join(out, 'package.json'),
  JSON.stringify({ name: 'trade247', productName: 'TRADE247', version: pkg.version, description: pkg.description, main: 'main.cjs', author: 'TRADE247', license: 'UNLICENSED' }, null, 2),
);
log(`built ${out}`);

if (dist) {
  const cli = join(root, 'node_modules', 'electron-builder', 'cli.js');
  const args = ['--config', 'electron-builder.yml', '--publish', 'never'];
  if (process.platform === 'win32') args.push('--win');
  else if (process.platform === 'darwin') args.push('--mac');
  else args.push('--linux');
  log(`packaging with electron-builder (${args.join(' ')})`);
  execFileSync(process.execPath, [cli, ...args], { cwd: root, stdio: 'inherit' });
}
