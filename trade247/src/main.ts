import { execFile } from 'node:child_process';
import { HELP, parseArgs, parsePort } from './cli.js';
import { openWithDefaultApp } from './report/export.js';
import { VERSION, packaged } from './runtime.js';
import { buildApp } from './server/app.js';
import { defaultDataDir } from './store/paths.js';

const HOST = '127.0.0.1';

function openBrowser(target: string): void {
  const [cmd, args]: [string, string[]] =
    process.platform === 'win32' ? ['explorer.exe', [target]]
    : process.platform === 'darwin' ? ['open', [target]]
    : ['xdg-open', [target]];
  execFile(cmd, args, { windowsHide: true }, () => {});
}

async function main(): Promise<void> {
  const cli = parseArgs(process.argv.slice(2));
  if (cli.errors.length > 0) {
    for (const e of cli.errors) console.error(e);
    console.error('\n' + HELP);
    process.exit(2);
  }
  if (cli.version) {
    console.log(`TRADE247-V1 ${VERSION}`);
    return;
  }
  if (cli.help) {
    console.log(HELP);
    return;
  }

  const envPort = parsePort(process.env.T247_PORT);
  if (Number.isNaN(envPort)) {
    console.error(`T247_PORT must be a port number between 1 and 65535 (got "${process.env.T247_PORT}").`);
    process.exit(1);
  }
  const port = cli.port ?? envPort ?? 8247;
  // The packaged program opens the page for you; from source, or when told not to, it only prints the address.
  const open = cli.open ?? (packaged && process.env.T247_NO_OPEN !== '1');

  const dataDir = defaultDataDir();
  // T247_OPEN_REPORTS=0: never launch a desktop application (servers, VPS); the page downloads reports instead.
  const openFile = process.env.T247_OPEN_REPORTS === '0' ? undefined : openWithDefaultApp;
  const app = buildApp({ dataDir, port, logger: false, openFile });
  const url = `http://${HOST}:${port}/`;

  try {
    await app.listen({ host: HOST, port });
  } catch (e) {
    const code = (e as NodeJS.ErrnoException).code;
    console.error(code === 'EADDRINUSE'
      ? `Port ${port} is already in use. Is TRADE247 already running? Use --port to choose another port.`
      : `Could not start the server: ${(e as Error).message}`);
    process.exit(1);
  }

  console.log(`TRADE247-V1 ${VERSION} is running at ${url}`);
  console.log(`Data folder: ${dataDir}`);
  console.log('Press Ctrl+C (or close this window) to stop.');
  if (open) openBrowser(url);

  for (const signal of ['SIGINT', 'SIGTERM'] as const) {
    process.on(signal, () => {
      void app.close().then(() => process.exit(0));
    });
  }
}

main().catch((e: unknown) => {
  console.error(e instanceof Error ? e.message : String(e));
  process.exit(1);
});
