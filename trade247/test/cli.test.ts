import { existsSync, readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { HELP, parseArgs, parsePort } from '../src/cli.js';
import { VERSION, embeddedWorkerSource, packaged, uiHtml } from '../src/runtime.js';

const root = new URL('../', import.meta.url);
const read = (path: string): string => readFileSync(new URL(path, root), 'utf8');

describe('command line', () => {
  it('has sensible defaults', () => {
    expect(parseArgs([])).toEqual({ help: false, version: false, open: undefined, port: undefined, errors: [] });
  });

  it('understands every option', () => {
    expect(parseArgs(['--open']).open).toBe(true);
    expect(parseArgs(['--no-open']).open).toBe(false);
    expect(parseArgs(['--open', '--no-open']).open).toBe(false);
    expect(parseArgs(['--port', '9100']).port).toBe(9100);
    expect(parseArgs(['--port=9101']).port).toBe(9101);
    expect(parseArgs(['--version']).version).toBe(true);
    expect(parseArgs(['-v']).version).toBe(true);
    expect(parseArgs(['--help']).help).toBe(true);
    expect(parseArgs(['-h']).help).toBe(true);
  });

  it('rejects bad ports and unknown options with a message, never silently', () => {
    for (const bad of [['--port'], ['--port', 'abc'], ['--port', '0'], ['--port', '70000'], ['--port=1.5'], ['--port=']]) {
      const parsed = parseArgs(bad);
      expect(parsed.errors, bad.join(' ')).toHaveLength(1);
      expect(parsed.errors[0]).toMatch(/--port needs a number between 1 and 65535/);
      expect(parsed.port).toBeUndefined();
    }
    expect(parseArgs(['--bogus']).errors).toEqual(['Unknown option "--bogus"']);
    expect(parseArgs(['--no-open', '--bogus', '--port', 'x']).errors).toHaveLength(2);
  });

  it('parses the port from the environment the same way', () => {
    expect(parsePort(undefined)).toBeUndefined();
    expect(parsePort('')).toBeUndefined();
    expect(parsePort('8247')).toBe(8247);
    for (const bad of ['0', '65536', '-1', 'x', '80.5']) expect(Number.isNaN(parsePort(bad)), bad).toBe(true);
  });

  it('documents every option it accepts', () => {
    for (const option of ['--open', '--no-open', '--port', '--version', '--help', 'T247_PORT', 'T247_DATA_DIR', 'T247_NO_OPEN', 'T247_OPEN_REPORTS']) {
      expect(HELP, option).toContain(option);
    }
  });
});

describe('running from source', () => {
  it('is not the packaged program, reads the page from ui/, and has no embedded worker', () => {
    expect(packaged).toBe(false);
    expect(embeddedWorkerSource()).toBeNull();
    expect(uiHtml()).toBe(read('ui/index.html'));
    expect(uiHtml()).toContain('TRADE247-V1');
  });

  it('reports the version from package.json', () => {
    expect(VERSION).toBe(JSON.parse(read('package.json')).version);
    expect(VERSION).toMatch(/^\d+\.\d+\.\d+/);
  });
});

describe('what ships with the program', () => {
  const pkg = JSON.parse(read('package.json')) as { scripts: Record<string, string> };

  it('every file the release assembler copies exists', () => {
    for (const f of [
      'packaging/windows/Start-Trade247.bat', 'packaging/windows/install-vps-task.ps1', 'packaging/windows/uninstall-vps-task.ps1',
      'packaging/macos/Start-Trade247.command', 'packaging/README-RUN.windows.txt', 'packaging/README-RUN.macos.txt',
      'docs/deployment.md', 'README.md', 'ui/index.html', 'tools/build-app.mjs', 'tools/assemble-release.mjs', 'tools/smoke-app.mjs',
    ]) expect(existsSync(new URL(f, root)), f).toBe(true);
  });

  it('the scripts the guides tell people to run exist', () => {
    for (const name of ['start', 'test', 'typecheck', 'package', 'smoke', 'release']) expect(pkg.scripts[name], `npm run ${name}`).toBeTruthy();
    const guide = read('docs/deployment.md');
    for (const name of ['npm ci', 'npm run typecheck', 'npm test', 'npm start', 'npm run package', 'npm run smoke', 'npm run release']) {
      expect(guide, name).toContain(name);
    }
  });

  it('the run instructions use the placeholders the assembler fills in, and name no other', () => {
    for (const f of ['packaging/README-RUN.windows.txt', 'packaging/README-RUN.macos.txt']) {
      const text = read(f);
      expect(text).toContain('{{VERSION}}');
      expect(text).toContain('{{EXE}}');
      expect([...text.matchAll(/\{\{(\w+)\}\}/g)].every((m) => m[1] === 'VERSION' || m[1] === 'EXE'), f).toBe(true);
    }
  });

  it('the macOS launcher starts with a shebang and the CI workflow runs the checks the guide promises', () => {
    expect(read('packaging/macos/Start-Trade247.command').startsWith('#!/bin/bash\n')).toBe(true);
    const workflow = readFileSync(new URL('../.github/workflows/trade247.yml', root), 'utf8');
    for (const step of ['npm run typecheck', 'npm test', 'npm run package', 'npm run smoke', 'assemble-release', 'windows-latest', 'macos-14', 'codesign --verify']) {
      expect(workflow, step).toContain(step);
    }
  });

  it('the VPS task script keeps its safety checks', () => {
    const script = read('packaging/windows/install-vps-task.ps1');
    for (const part of ['-DryRun', 'T247_OPEN_REPORTS=0', '--no-open', '-RestartCount', "-RunLevel Limited", 'DataDir must not contain']) {
      expect(script, part).toContain(part);
    }
    expect(script).not.toMatch(/-RunLevel\s+Highest/);
  });
});
