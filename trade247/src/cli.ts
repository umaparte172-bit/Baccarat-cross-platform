export interface CliOptions {
  help: boolean;
  version: boolean;
  /** undefined = use the default (open the page when running as the packaged program). */
  open: boolean | undefined;
  port: number | undefined;
  /** Calibration capture of the table open in Chrome instead of starting the server. */
  inspectTable: boolean;
  /** Read the road and the betting window of the open table with the current profile (clicks nothing). */
  checkTable: boolean;
  /** Chrome debugging port for --inspect-table. */
  livePort: number | undefined;
  errors: string[];
}

export const HELP = `TRADE247-V1 - local baccarat strategy bot

Usage: trade247 [options]

  --open           Open the page in your browser when the server is ready
  --no-open        Do not open the browser (servers, VPS, scheduled tasks)
  --port <number>  Port to listen on (default 8247; also T247_PORT)
  --inspect-table  Save a description of the table open in Chrome (for calibration), then exit
  --check-table    Show what the table profile can read from the open table (clicks nothing), then exit
  --live-port <n>  Chrome debugging port for --inspect-table and --check-table (default 9014)
  --version, -v    Print the version and exit
  --help, -h       Print this help and exit

Environment:
  T247_PORT          Port (default 8247)
  T247_DATA_DIR      Where settings, logs and reports are kept
  T247_NO_OPEN=1     Same as --no-open
  T247_OPEN_REPORTS=0  Never launch Excel/Notepad; the page downloads reports instead

The page is served on 127.0.0.1 only and cannot be reached from other computers.`;

export function parsePort(value: string | undefined): number | undefined {
  if (value === undefined || value === '') return undefined;
  const n = Number(value);
  return Number.isInteger(n) && n >= 1 && n <= 65535 ? n : NaN;
}

export function parseArgs(argv: readonly string[]): CliOptions {
  const out: CliOptions = { help: false, version: false, open: undefined, port: undefined, inspectTable: false, checkTable: false, livePort: undefined, errors: [] };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]!;
    if (arg === '--help' || arg === '-h') out.help = true;
    else if (arg === '--version' || arg === '-v') out.version = true;
    else if (arg === '--open') out.open = true;
    else if (arg === '--no-open') out.open = false;
    else if (arg === '--inspect-table') out.inspectTable = true;
    else if (arg === '--check-table') out.checkTable = true;
    else if (arg === '--live-port' || arg.startsWith('--live-port=')) {
      const value = arg === '--live-port' ? argv[++i] : arg.slice('--live-port='.length);
      const port = parsePort(value);
      if (port === undefined || Number.isNaN(port)) out.errors.push(`--live-port needs a number between 1 and 65535 (got "${value ?? ''}")`);
      else out.livePort = port;
    } else if (arg === '--port' || arg.startsWith('--port=')) {
      const value = arg === '--port' ? argv[++i] : arg.slice('--port='.length);
      const port = parsePort(value);
      if (port === undefined || Number.isNaN(port)) out.errors.push(`--port needs a number between 1 and 65535 (got "${value ?? ''}")`);
      else out.port = port;
    } else out.errors.push(`Unknown option "${arg}"`);
  }
  return out;
}
