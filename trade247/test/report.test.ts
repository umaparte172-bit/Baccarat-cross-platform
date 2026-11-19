import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import ExcelJS from 'exceljs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CSV_HEADER } from '../src/report/csv.js';
import { latestSessionFromHistory, parseCsv, tableCsv } from '../src/report/table.js';
import { XLSX_MIME } from '../src/report/xlsx.js';
import { MANUAL, cleanup, harness, type Harness } from './helpers.js';

afterEach(cleanup);

describe('CSV locale export', () => {
  it('preserves fractional cents and protects spreadsheet text values', () => {
    const csv = tableCsv({ columns: [{ title: 'Balance', kind: 'money', width: 12 }, { title: 'Rule', kind: 'text', width: 20 }], rows: [[1234.095, '=1+1'], [-0.095, '2-2']] }, 'vn');
    expect(csv).toContain("1.234,095;'=1+1");
    expect(csv).toContain('-0,095;2-2 cycle');
  });
  it('serves the selected locale through the report endpoint', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...MANUAL, csv_style: 'vn' } });
    await h.post('/api/manual', { result: 'P' });
    const csv = await h.get('/api/report?fmt=csv');
    expect(csv.text).toContain('Time;Session;Hand');
    expect(csv.text).toContain('1.001,00');
    await h.post('/api/apply', { csv_style: 'plain' });
    expect((await h.get('/api/report?fmt=csv')).text).toContain(CSV_HEADER);
  });
});

const BASE = { unit_value: 1, min_chip: 0, start_balance: 1000 };
const BOM = String.fromCodePoint(0xfeff);

const sheetOf = async (bytes: Buffer): Promise<ExcelJS.Worksheet> => {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(bytes as unknown as ExcelJS.Buffer);
  return wb.getWorksheet('Trades')!;
};

const replay = async (h: Harness, letters: string, extra: Record<string, unknown> = {}) => {
  expect((await h.post('/api/start', { settings: { ...BASE, feed: 'csv', csv_text: letters, ...extra } })).json.ok).toBe(true);
  await vi.waitFor(async () => expect((await h.get('/api/state')).json.running).toBe(false), { timeout: 5000, interval: 10 });
};

const manualSession = async (h: Harness, letters = 'PPB') => {
  await h.post('/api/start', { settings: MANUAL });
  for (const result of letters) await h.post('/api/manual', { result });
};

describe('downloading the report', () => {
  it('retains fractional-cent Banker payouts in state, both CSV styles, and Excel', async () => {
    const h = harness();
    await h.post('/api/start', { settings: { ...MANUAL, strategy: 'MARTINGALE', unit_value: 0.1, first_side: 'B', min_chip: 0 } });
    await h.post('/api/manual', { result: 'B' });
    const state = (await h.get('/api/state')).json;
    expect(state.rows[0].session_pnl).toBe('0.095');
    const csv = parseCsv((await h.get('/api/report?fmt=csv')).text.replace(BOM, ''));
    expect(csv[1]![10]).toBe('0.095');
    expect(csv[1]![12]).toBe('1000.095');
    const ws = await sheetOf((await h.get('/api/report?fmt=xlsx')).raw);
    expect(ws.getRow(2).getCell(11).value).toBe(0.095);
    expect(ws.getRow(2).getCell(13).value).toBe(1000.095);
    await h.post('/api/apply', { csv_style: 'vn' });
    expect((await h.get('/api/report?fmt=csv')).text).toContain('1.000,095');
  });

  it('serves a real .xlsx whose cells match the CSV', async () => {
    const h = harness();
    await manualSession(h);

    const xlsx = await h.get('/api/report?fmt=xlsx');
    expect(xlsx.status).toBe(200);
    expect(xlsx.headers['content-type']).toBe(XLSX_MIME);
    expect(xlsx.headers['content-disposition']).toBe('attachment; filename="trade247_report.xlsx"');
    expect(xlsx.raw.subarray(0, 4)).toEqual(Buffer.from([0x50, 0x4b, 0x03, 0x04]));

    const ws = await sheetOf(xlsx.raw);
    expect(ws.rowCount).toBe(4);
    const csv = parseCsv((await h.get('/api/report?fmt=csv')).text.replace(BOM, ''));
    expect(csv).toHaveLength(4);
    for (let r = 1; r < 4; r++) {
      const row = ws.getRow(r + 1);
      expect(String(row.getCell(1).value)).toBe(csv[r]![0]);
      expect(row.getCell(2).value).toBe(Number(csv[r]![1]));
      expect(row.getCell(7).value).toBeCloseTo(Number(csv[r]![6]), 2);
      expect(row.getCell(9).value).toBe(csv[r]![8]);
      expect(row.getCell(11).value).toBeCloseTo(Number(csv[r]![10]), 2);
      expect(row.getCell(13).value).toBeCloseTo(Number(csv[r]![12]), 2);
    }
    // P wins, P wins, B beats the Player bet: +1, +1, -1.
    expect([2, 3, 4].map((r) => ws.getRow(r).getCell(11).value)).toEqual([1, 1, -1]);
    expect(ws.getRow(4).getCell(13).value).toBe(1001);
  });

  it('answers 404 for both formats before any session exists', async () => {
    const h = harness();
    expect((await h.get('/api/report?fmt=xlsx')).status).toBe(404);
    expect((await h.get('/api/report?fmt=csv')).status).toBe(404);
    expect((await h.get('/api/report?fmt=pdf')).status).toBe(404);
  });
});

describe('Excel Log (open)', () => {
  it('saves a snapshot and lets the page download it when no application launcher is available', async () => {
    const h = harness();
    await manualSession(h);
    const reply = (await h.post('/api/report/open', { fmt: 'xlsx' })).json;
    expect(reply).toMatchObject({ ok: true, opened: false });
    expect(reply.path).toMatch(/trade247_report_\d{8}_\d{6}\.xlsx$/);
    expect(reply.path.startsWith(join(h.dir, 'reports'))).toBe(true);
    const ws = await sheetOf(readFileSync(reply.path));
    expect(ws.rowCount).toBe(4);
  });

  it('opens the saved file in the default application when a launcher is provided', async () => {
    const opened: string[] = [];
    const h = harness({ app: { openFile: (p) => void opened.push(p) } });
    await manualSession(h);
    const csv = (await h.post('/api/report/open', { fmt: 'csv' })).json;
    expect(csv).toMatchObject({ ok: true, opened: true });
    expect(opened).toEqual([csv.path]);
    const text = readFileSync(csv.path, 'utf8');
    expect(text.charCodeAt(0)).toBe(0xfeff);
    expect(text.split(/\r\n/)[0]).toBe(BOM + CSV_HEADER);

    const xlsx = (await h.post('/api/report/open', { fmt: 'xlsx' })).json;
    expect(xlsx).toMatchObject({ ok: true, opened: true });
    expect(opened).toEqual([csv.path, xlsx.path]);
  });

  it('falls back to a download when the launcher fails', async () => {
    const h = harness({ app: { openFile: () => { throw new Error('no application'); } } });
    await manualSession(h);
    expect((await h.post('/api/report/open', { fmt: 'xlsx' })).json).toMatchObject({ ok: true, opened: false });
  });

  it('gives every snapshot its own file so one still open in Excel is never overwritten', async () => {
    const h = harness();
    await manualSession(h);
    const a = (await h.post('/api/report/open', { fmt: 'xlsx' })).json.path;
    const b = (await h.post('/api/report/open', { fmt: 'xlsx' })).json.path;
    const c = (await h.post('/api/report/open', { fmt: 'xlsx' })).json.path;
    expect(new Set([a, b, c]).size).toBe(3);
    for (const p of [a, b, c]) expect(existsSync(p)).toBe(true);
  });

  it('keeps only the newest snapshots', async () => {
    const h = harness();
    await manualSession(h);
    for (let i = 0; i < 26; i++) await h.post('/api/report/open', { fmt: 'csv' });
    expect(readdirSync(join(h.dir, 'reports')).length).toBeLessThanOrEqual(20);
    expect(readdirSync(join(h.dir, 'reports')).length).toBeGreaterThan(0);
  });

  it('does not touch files in the reports folder that it did not create', async () => {
    const h = harness();
    await manualSession(h);
    await h.post('/api/report/open', { fmt: 'csv' });
    const mine = join(h.dir, 'reports', 'notes.txt');
    writeFileSync(mine, 'keep me');
    for (let i = 0; i < 26; i++) await h.post('/api/report/open', { fmt: 'csv' });
    expect(readFileSync(mine, 'utf8')).toBe('keep me');
  });

  it('refuses unknown formats and an empty log', async () => {
    const h = harness();
    expect((await h.post('/api/report/open', { fmt: 'pdf' })).json).toEqual({ ok: false, error: 'Unknown report format' });
    expect((await h.post('/api/report/open', { fmt: 'xlsx' })).json).toEqual({ ok: false, error: 'No log yet - press START first' });
    expect((await h.post('/api/report/open', {})).json.ok).toBe(false);
  });
});

describe('the report survives a restart and a Reset', () => {
  it('rebuilds the latest session from the history file, identical to the live report', async () => {
    const first = harness();
    await replay(first, 'PP');
    await first.post('/api/reset');
    await replay(first, 'BPP');
    const liveCsv = (await first.get('/api/report?fmt=csv')).text;
    const liveSheet = await sheetOf((await first.get('/api/report?fmt=xlsx')).raw);
    expect(liveSheet.rowCount).toBe(4);

    const dir = first.dir;
    await first.close();
    const second = harness(dir);
    expect((await second.get('/api/state')).json.hands).toBe(0); // nothing in memory any more

    const restoredCsv = (await second.get('/api/report?fmt=csv')).text;
    expect(restoredCsv).toBe(liveCsv);
    const restored = await sheetOf((await second.get('/api/report?fmt=xlsx')).raw);
    expect(restored.rowCount).toBe(4);
    for (let r = 1; r <= 4; r++) {
      for (let c = 1; c <= 13; c++) {
        expect(restored.getRow(r).getCell(c).value, `r${r} c${c}`).toEqual(liveSheet.getRow(r).getCell(c).value);
      }
    }
    // Only the latest session is in the report, not the earlier one.
    expect(restored.getRow(2).getCell(2).value).toBe(restored.getRow(4).getCell(2).value);
  });

  it('still reports after Reset has cleared the page', async () => {
    const h = harness();
    await replay(h, 'PPP');
    await h.post('/api/reset');
    expect((await h.get('/api/state')).json.hands).toBe(0);
    const ws = await sheetOf((await h.get('/api/report?fmt=xlsx')).raw);
    expect(ws.rowCount).toBe(4);
  });

  it('says "no log yet" when the history file is unusable', async () => {
    const h = harness();
    writeFileSync(join(h.dir, 'dummy'), '');
    const { mkdirSync } = await import('node:fs');
    mkdirSync(join(h.dir, 'logs'), { recursive: true });
    writeFileSync(join(h.dir, 'logs', 'trade247_session_history.csv'), 'not,the,right,header\r\n1,2,3,4\r\n');
    expect((await h.post('/api/report/open', { fmt: 'csv' })).json.ok).toBe(false);
    expect((await h.get('/api/report?fmt=xlsx')).status).toBe(404);
  });
});

describe('history parsing', () => {
  const line = (session: number, hand: number, rule = 'Follow'): string =>
    `2026-10-28 12:00:0${hand},${session},${hand},IHG,PLAYER,REAL,1.00,PLAYER,WIN,${rule},1.00,${hand}.00,100${hand}.00`;

  it('returns only the latest session and quotes the CSV it rebuilds', () => {
    const text = [CSV_HEADER, line(1, 1), line(1, 2), line(2, 1, '"a, b"'), line(2, 2)].join('\r\n') + '\r\n';
    const found = latestSessionFromHistory(text)!;
    expect(found.session).toBe(2);
    expect(found.table.rows).toHaveLength(2);
    expect(found.table.rows[0]![9]).toBe('a, b');
    expect(typeof found.table.rows[0]![6]).toBe('number');
    expect(found.csv.split('\r\n')).toEqual([CSV_HEADER, line(2, 1, '"a, b"'), line(2, 2), '']);
  });

  it('returns null for an empty file, a wrong header, or a header with no rows', () => {
    expect(latestSessionFromHistory('')).toBeNull();
    expect(latestSessionFromHistory('a,b\r\n1,2\r\n')).toBeNull();
    expect(latestSessionFromHistory(CSV_HEADER + '\r\n')).toBeNull();
  });

  it('ignores a half-written last line', () => {
    const text = [CSV_HEADER, line(3, 1), '2026-10-28 12:00:09,3,2,IHG,PLA'].join('\r\n');
    expect(latestSessionFromHistory(text)!.table.rows).toHaveLength(1);
  });

  it('parses quotes, embedded commas, doubled quotes and newlines, with CRLF or LF', () => {
    expect(parseCsv('a,"b,c","d ""q"""\r\n1,2,3\n')).toEqual([['a', 'b,c', 'd "q"'], ['1', '2', '3']]);
    expect(parseCsv('x,"line1\nline2"\r\n')).toEqual([['x', 'line1\nline2']]);
    expect(parseCsv('')).toEqual([]);
  });
});
