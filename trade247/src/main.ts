import { execFile } from 'node:child_process';
import { buildApp } from './server/app.js';
import { defaultDataDir } from './store/paths.js';

const HOST = '127.0.0.1';
const port = Number(process.env.T247_PORT ?? 8247);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  console.error(`T247_PORT must be a port number between 1 and 65535 (got "${process.env.T247_PORT}").`);
  process.exit(1);
}

const dataDir = defaultDataDir();
const app = buildApp({ dataDir, port, logger: false });
const url = `http://${HOST}:${port}/`;

function openBrowser(target: string): void {
  const [cmd, args]: [string, string[]] =
    process.platform === 'win32' ? ['cmd', ['/c', 'start', '', target]]
    : process.platform === 'darwin' ? ['open', [target]]
    : ['xdg-open', [target]];
  execFile(cmd, args, () => {});
}

try {
  await app.listen({ host: HOST, port });
} catch (e) {
  const code = (e as NodeJS.ErrnoException).code;
  console.error(code === 'EADDRINUSE'
    ? `Port ${port} is already in use. Is TRADE247 already running? Set T247_PORT to use another port.`
    : `Could not start the server: ${(e as Error).message}`);
  process.exit(1);
}

console.log(`TRADE247-V1 is running at ${url}`);
console.log(`Data folder: ${dataDir}`);
if (process.argv.includes('--open')) openBrowser(url);

for (const signal of ['SIGINT', 'SIGTERM'] as const) {
  process.on(signal, () => {
    void app.close().then(() => process.exit(0));
  });
}
