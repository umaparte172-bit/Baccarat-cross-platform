// Puts the built program and everything that goes with it into one folder, ready to be zipped:
//   node tools/assemble-release.mjs     ->  release/trade247-<os>-<arch>-<version>/
// Run "npm run package" first.

import { createHash } from 'node:crypto';
import { chmodSync, copyFileSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const pkg = JSON.parse(readFileSync(join(root, 'package.json'), 'utf8'));
const os = { win32: 'win', darwin: 'macos', linux: 'linux' }[process.platform] ?? process.platform;
const target = `${os}-${process.arch}`;
const exe = `trade247-${target}${process.platform === 'win32' ? '.exe' : ''}`;
const built = join(root, 'release', exe);
if (!existsSync(built)) {
  console.error(`Missing ${built}. Run "npm run package" first.`);
  process.exit(2);
}

const folderName = `trade247-${target}-${pkg.version}`;
const out = join(root, 'release', folderName);
rmSync(out, { recursive: true, force: true });
mkdirSync(join(out, 'docs'), { recursive: true });

const put = (from, to, mode) => {
  copyFileSync(from, join(out, to));
  if (mode !== undefined && process.platform !== 'win32') chmodSync(join(out, to), mode);
};
put(built, exe, 0o755);

if (os === 'win') {
  for (const f of ['Start-Trade247.bat', 'Start-Chrome-for-Trade247.bat', 'install-vps-task.ps1', 'uninstall-vps-task.ps1']) put(join(root, 'packaging/windows', f), f);
} else if (os === 'macos') {
  put(join(root, 'packaging/macos/Start-Trade247.command'), 'Start-Trade247.command', 0o755);
  put(join(root, 'packaging/macos/Start-Chrome-for-Trade247.command'), 'Start-Chrome-for-Trade247.command', 0o755);
}
put(join(root, 'docs/deployment.md'), 'docs/deployment.md');
put(join(root, 'README.md'), 'README.md');

const template = join(root, 'packaging', `README-RUN.${os === 'win' ? 'windows' : 'macos'}.txt`);
if (existsSync(template)) {
  const text = readFileSync(template, 'utf8').replaceAll('{{VERSION}}', pkg.version).replaceAll('{{EXE}}', exe);
  writeFileSync(join(out, 'README-RUN.txt'), text.replace(/\r?\n/g, os === 'win' ? '\r\n' : '\n'));
}

const sha = createHash('sha256').update(readFileSync(built)).digest('hex');
writeFileSync(join(out, 'SHA256SUMS.txt'), `${sha}  ${exe}\n`);

console.log(`[release] ${out}`);
console.log(`[release] sha256 ${sha}  ${exe}`);
