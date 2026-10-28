import { appendFileSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { fmt2 } from '../engine/money.js';
import type { Row } from '../engine/types.js';

/** A history row plus the figures the log needs but the page does not. */
export interface HandRecord {
  row: Row;
  strategy: string;
  pnlMicro: number;
  balanceMicro: number;
}

export const CSV_HEADER = 'Time,Session,Hand,Strategy,Bet,Mode,Stake,Winner,Outcome,Rule,PnL,SessionPnL,Balance';
export const LOG_FILE_NAME = 'trade247_session_history.csv';

const NAMES = { P: 'PLAYER', B: 'BANKER', T: 'TIE' } as const;

function cell(value: string | number): string {
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

export function csvLine(r: HandRecord): string {
  const { row } = r;
  const bet = NAMES[row.pick] + (row.mode === 'VIRTUAL' ? ' (virtual)' : row.mode === 'MIN' ? ' (keep-alive)' : '');
  const rule = [row.pattern, row.note].filter(Boolean).join(' · ');
  return [
    row.time, row.session, row.n, r.strategy, bet, row.mode, row.stake, NAMES[row.result], row.outcome,
    rule, fmt2(r.pnlMicro), row.session_pnl, fmt2(r.balanceMicro),
  ].map(cell).join(',');
}

export function buildCsv(records: readonly HandRecord[]): string {
  return [CSV_HEADER, ...records.map(csvLine)].join('\r\n') + '\r\n';
}

/** Append one hand to the running history file, writing the header on first use. */
export function appendHistory(path: string, record: HandRecord): void {
  mkdirSync(dirname(path), { recursive: true });
  if (!existsSync(path)) writeFileSync(path, CSV_HEADER + '\r\n', 'utf8');
  appendFileSync(path, csvLine(record) + '\r\n', 'utf8');
}
