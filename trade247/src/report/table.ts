import { fmt2 } from '../engine/money.js';
import { CSV_HEADER, betLabel, type HandRecord } from './csv.js';

export type CellKind = 'text' | 'int' | 'money';
export type Cell = string | number;

export interface Column {
  title: string;
  kind: CellKind;
  /** Column width in characters. */
  width: number;
}

export interface Table {
  columns: Column[];
  rows: Cell[][];
}

/** The report's columns; the CSV header (csv.ts) must list the same titles in the same order. */
export const COLUMNS: Column[] = [
  { title: 'Time', kind: 'text', width: 20 },
  { title: 'Session', kind: 'int', width: 9 },
  { title: 'Hand', kind: 'int', width: 7 },
  { title: 'Strategy', kind: 'text', width: 10 },
  { title: 'Bet', kind: 'text', width: 22 },
  { title: 'Mode', kind: 'text', width: 9 },
  { title: 'Stake', kind: 'money', width: 10 },
  { title: 'Winner', kind: 'text', width: 10 },
  { title: 'Outcome', kind: 'text', width: 10 },
  { title: 'Rule', kind: 'text', width: 50 },
  { title: 'PnL', kind: 'money', width: 10 },
  { title: 'SessionPnL', kind: 'money', width: 12 },
  { title: 'Balance', kind: 'money', width: 12 },
];

const NAMES = { P: 'PLAYER', B: 'BANKER', T: 'TIE' } as const;

export function tableFromRecords(records: readonly HandRecord[]): Table {
  const rows = records.map(({ row, strategy, pnlMicro, balanceMicro }): Cell[] => {
    const bet = betLabel(row);
    return [
      row.time, row.session, row.n, strategy, bet, row.mode, Number(row.stake), NAMES[row.result], row.outcome,
      [row.pattern, row.note].filter(Boolean).join(' · '), Number(fmt2(pnlMicro)), Number(row.session_pnl), Number(fmt2(balanceMicro)),
    ];
  });
  return { columns: COLUMNS, rows };
}

/** Minimal CSV reader for the files this program writes (quoted fields, doubled quotes, CRLF or LF). */
export function parseCsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i]!;
    if (quoted) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else quoted = false;
      } else field += ch;
    } else if (ch === '"') quoted = true;
    else if (ch === ',') {
      row.push(field);
      field = '';
    } else if (ch === '\n' || ch === '\r') {
      if (ch === '\r' && text[i + 1] === '\n') i++;
      row.push(field);
      field = '';
      if (row.length > 1 || row[0] !== '') rows.push(row);
      row = [];
    } else field += ch;
  }
  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

export interface HistoryReport {
  table: Table;
  csv: string;
  session: number;
}

const HEADER_TITLES = CSV_HEADER.split(',');

/** The most recent session found in the append-only history file, or null when there is none. */
export function latestSessionFromHistory(text: string): HistoryReport | null {
  const parsed = parseCsv(text);
  const header = parsed[0];
  if (!header || header.join(',') !== HEADER_TITLES.join(',')) return null;
  const data = parsed.slice(1).filter((r) => r.length === COLUMNS.length);
  const last = data.at(-1);
  if (!last) return null;
  const session = Number(last[1]);
  const mine = data.filter((r) => Number(r[1]) === session);

  const rows = mine.map((r) =>
    r.map((value, i): Cell => {
      const kind = COLUMNS[i]!.kind;
      if (kind === 'text') return value;
      const n = Number(value);
      return Number.isFinite(n) && value.trim() !== '' ? n : value;
    }),
  );
  const quote = (v: string): string => (/[",\r\n]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
  const csv = [CSV_HEADER, ...mine.map((r) => r.map(quote).join(','))].join('\r\n') + '\r\n';
  return { table: { columns: COLUMNS, rows }, csv, session };
}
