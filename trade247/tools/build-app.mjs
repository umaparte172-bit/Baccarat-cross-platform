// Builds the single-file program for the OS this script runs on:
//   node tools/build-app.mjs            ->  release/trade247-<os>-<arch>[.exe]
//
// It bundles the server and the backtest worker, embeds them and the page into a copy of the Node runtime
// (Node "single executable application"), and on macOS signs the result ad hoc, which Apple Silicon requires.
// A single-file program must be built on the OS it is meant for, because it contains that OS's Node binary.

import { execFileSync } from 'node:child_process';
import { copyFileSync, chmodSync, mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'esbuild';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));

const os = { win32: 'win', darwin: 'macos', linux: 'linux' }[process.platform] ?? process.platform;
const target = `${os}-${process.arch}`;
const exe = `trade247-${target}${process.platform === 'win32' ? '.exe' : ''}`;
const buildDir = join(root, 'build');
const releaseDir = join(root, 'release');
const SENTINEL = 'NODE_SEA_FUSE_fce680ab2cc467b6e072b8b5df1996b2';

const log = (msg) => console.log(`[package] ${msg}`);
const run = (cmd, args, options = {}) => execFileSync(cmd, args, { cwd: root, stdio: 'inherit', ...options });

rmSync(buildDir, { recursive: true, force: true });
mkdirSync(buildDir, { recursive: true });
mkdirSync(releaseDir, { recursive: true });

// 1. One CommonJS file each for the program and the worker thread. `import.meta.url` does not exist in a
//    CommonJS bundle, so it is replaced by a value computed once at startup.
const common = {
  bundle: true,
  platform: 'node',
  target: 'node22',
  format: 'cjs',
  legalComments: 'none',
  logLevel: 'warning',
  define: {
    __APP_VERSION__: JSON.stringify(pkg.version),
    'import.meta.url': '__importMetaUrl',
  },
  banner: {
    js: "const __importMetaUrl = typeof __filename === 'string' ? require('node:url').pathToFileURL(__filename).href : 'file:///';",
  },
};
log(`bundling ${pkg.name} ${pkg.version} for ${target}`);
await build({ ...common, entryPoints: [join(root, 'src/main.ts')], outfile: join(buildDir, 'app.cjs') });
await build({ ...common, entryPoints: [join(root, 'src/backtest/worker.ts')], outfile: join(buildDir, 'worker.cjs') });

// 2. The blob the runtime will carry: the program plus the files it cannot read from disk once packaged.
writeFileSync(
  join(buildDir, 'sea-config.json'),
  JSON.stringify({
    main: 'build/app.cjs',
    output: 'build/sea-prep.blob',
    disableExperimentalSEAWarning: true,
    assets: { 'index.html': 'ui/index.html', 'worker.js': 'build/worker.cjs' },
  }, null, 2),
);
run(process.execPath, ['--experimental-sea-config', 'build/sea-config.json']);

// 3. A copy of this machine's Node runtime, with the blob injected.
const out = join(releaseDir, exe);
rmSync(out, { force: true });
copyFileSync(process.execPath, out);
if (process.platform !== 'win32') chmodSync(out, 0o755);

if (process.platform === 'darwin') {
  run('codesign', ['--remove-signature', out]);
} else if (process.platform === 'win32') {
  // Injecting invalidates Node's own signature. Removing it is tidier but optional: Windows runs the file either way.
  try {
    execFileSync('signtool', ['remove', '/s', out], { stdio: 'ignore' });
  } catch {
    log('signtool not available; leaving the original (now invalid) signature in place');
  }
}

const postject = join(dirname(require.resolve('postject/package.json')), 'dist', 'cli.js');
const inject = [out, 'NODE_SEA_BLOB', join(buildDir, 'sea-prep.blob'), '--sentinel-fuse', SENTINEL];
if (process.platform === 'darwin') inject.push('--macho-segment-name', 'NODE_SEA');
run(process.execPath, [postject, ...inject]);

// Apple Silicon refuses to run an unsigned binary; an ad-hoc signature ("-") is enough for a program that is
// not distributed through the App Store (no Apple Developer account, certificate or notarization needed).
if (process.platform === 'darwin') run('codesign', ['--sign', '-', out]);

log(`built ${out} (${(statSync(out).size / 1024 / 1024).toFixed(1)} MB)`);
