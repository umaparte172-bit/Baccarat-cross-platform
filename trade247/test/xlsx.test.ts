import { crc32, inflateRawSync } from 'node:zlib';
import ExcelJS from 'exceljs';
import { describe, expect, it } from 'vitest';
import { buildXlsx, columnLetter, escapeXml, zip } from '../src/report/xlsx.js';
import { COLUMNS, parseCsv, tableFromRecords, type Table } from '../src/report/table.js';
import { CSV_HEADER, buildCsv, type HandRecord } from '../src/report/csv.js';

const record = (over: Partial<HandRecord['row']> = {}, pnlMicro = 1_000_000, balanceMicro = 1_001_000_000): HandRecord => ({
  strategy: 'IHG',
  pnlMicro,
  balanceMicro,
  row: {
    n: 1, time: '2026-10-28 12:00:00', pick: 'P', mode: 'REAL', stake: '1.00', result: 'P', outcome: 'WIN',
    pattern: 'Follow', note: 'run closed +1u', single_wins: 0, run_net_units: 1, session_pnl: '1.00', cards: null, session: 7,
    ...over,
  },
});

async function load(buffer: Buffer): Promise<ExcelJS.Worksheet> {
  const wb = new ExcelJS.Workbook();
  await wb.xlsx.load(buffer as unknown as ExcelJS.Buffer);
  const ws = wb.getWorksheet('Trades');
  if (!ws) throw new Error('worksheet "Trades" not found');
  return ws;
}

describe('column letters and escaping', () => {
  it('names columns like a spreadsheet', () => {
    expect([0, 1, 25, 26, 27, 51, 52, 701, 702].map(columnLetter)).toEqual(['A', 'B', 'Z', 'AA', 'AB', 'AZ', 'BA', 'ZZ', 'AAA']);
  });

  it('escapes XML metacharacters and drops characters XML cannot carry', () => {
    expect(escapeXml('a & b < c > "d"')).toBe('a &amp; b &lt; c &gt; &quot;d&quot;');
    expect(escapeXml('x\u0000y\u0008z\ttab\nnl')).toBe('xyz\ttab\nnl');
  });
});

describe('the ZIP container', () => {
  it('has a valid central directory and every entry inflates to data with a matching CRC', () => {
    const files = [
      { name: 'a.txt', data: Buffer.from('hello hello hello hello') },
      { name: 'dir/ü-ñ.xml', data: Buffer.from('<x/>'.repeat(500)) },
    ];
    const archive = zip(files, new Date(2026, 9, 28, 13, 45, 30));

    const eocd = archive.length - 22;
    expect(archive.readUInt32LE(eocd)).toBe(0x06054b50);
    const count = archive.readUInt16LE(eocd + 10);
    expect(count).toBe(2);
    let at = archive.readUInt32LE(eocd + 16);
    expect(at + archive.readUInt32LE(eocd + 12)).toBe(eocd);

    for (const file of files) {
      expect(archive.readUInt32LE(at)).toBe(0x02014b50);
      const crc = archive.readUInt32LE(at + 16);
      const packed = archive.readUInt32LE(at + 20);
      const size = archive.readUInt32LE(at + 24);
      const nameLen = archive.readUInt16LE(at + 28);
      const local = archive.readUInt32LE(at + 42);
      expect(archive.subarray(at + 46, at + 46 + nameLen).toString('utf8')).toBe(file.name);

      // The local header it points to repeats the same facts, followed by the compressed bytes.
      expect(archive.readUInt32LE(local)).toBe(0x04034b50);
      expect(archive.readUInt32LE(local + 14)).toBe(crc);
      const dataAt = local + 30 + archive.readUInt16LE(local + 26) + archive.readUInt16LE(local + 28);
      const inflated = inflateRawSync(archive.subarray(dataAt, dataAt + packed));
      expect(inflated.length).toBe(size);
      expect(inflated.equals(file.data)).toBe(true);
      expect(crc32(inflated) >>> 0).toBe(crc);
      at += 46 + nameLen;
    }
  });

  it('stores the modification time as DOS date and time', () => {
    const archive = zip([{ name: 'a', data: Buffer.from('x') }], new Date(2026, 9, 28, 13, 45, 30));
    const time = archive.readUInt16LE(10);
    const date = archive.readUInt16LE(12);
    expect([time >> 11, (time >> 5) & 63, (time & 31) * 2]).toEqual([13, 45, 30]);
    expect([(date >> 9) + 1980, (date >> 5) & 15, date & 31]).toEqual([2026, 10, 28]);
  });
});

describe('the workbook, read back by an independent library', () => {
  it('opens with the right sheet, header, typed cells and formatting', async () => {
    const table = tableFromRecords([
      record(),
      record({ n: 2, pick: 'B', result: 'P', outcome: 'LOSS', stake: '2.00', session_pnl: '-1.00' }, -2_000_000, 999_000_000),
    ]);
    const ws = await load(buildXlsx(table, new Date(2026, 9, 28)));

    expect(ws.rowCount).toBe(3);
    expect(ws.columnCount).toBe(13);
    expect(ws.getRow(1).values).toEqual([undefined, ...COLUMNS.map((c) => c.title)]);
    expect(ws.getCell('A1').font?.bold).toBe(true);

    const r2 = ws.getRow(2);
    expect(r2.getCell(1).value).toBe('2026-10-28 12:00:00');
    expect(r2.getCell(2).value).toBe(7);
    expect(r2.getCell(3).value).toBe(1);
    expect(r2.getCell(4).value).toBe('IHG');
    expect(r2.getCell(5).value).toBe('PLAYER');
    expect(r2.getCell(7).value).toBe(1);
    expect(r2.getCell(10).value).toBe('Follow · run closed +1u');
    expect(r2.getCell(11).value).toBe(1);
    expect(r2.getCell(13).value).toBe(1001);

    // Money is a real number with a two-decimal format, so Excel can add it up.
    expect(typeof ws.getCell('G2').value).toBe('number');
    expect(ws.getCell('G2').numFmt).toBe('#,##0.00####');
    expect(ws.getCell('K3').value).toBe(-2);
    expect(ws.getCell('M3').value).toBe(999);
    expect(ws.getCell('B2').numFmt).not.toBe('0.00');

    expect(ws.views[0]).toMatchObject({ state: 'frozen', ySplit: 1 });
    expect(ws.autoFilter).toBe('A1:M3');
    expect(ws.getColumn(10).width).toBe(50);
  });

  it('preserves awkward text exactly: markup, quotes, accents, padding, and strips control characters', async () => {
    const note = ' <b>"R&D"</b> café · naïve\u0000 ';
    const ws = await load(buildXlsx(tableFromRecords([record({ note })]), new Date()));
    expect(ws.getCell('J2').value).toBe('Follow ·  <b>"R&D"</b> café · naïve ');
  });

  it('does not treat text that looks like a formula as a formula', async () => {
    const ws = await load(buildXlsx(tableFromRecords([record({ pattern: '=SUM(A1:A9)', note: '@cmd' })]), new Date()));
    const cell = ws.getCell('J2');
    expect(typeof cell.value).toBe('string');
    expect(cell.formula).toBeUndefined();
    expect(cell.value).toBe('=SUM(A1:A9) · @cmd');
  });

  it('handles a long session and keeps every row', async () => {
    const records = Array.from({ length: 20_000 }, (_, i) => record({ n: i + 1, session_pnl: String((i % 100) / 10) }, i, i * 10));
    const started = Date.now();
    const buffer = buildXlsx(tableFromRecords(records), new Date());
    const ws = await load(buffer);
    expect(ws.rowCount).toBe(20_001);
    expect(ws.getCell('C20001').value).toBe(20_000);
    expect(Date.now() - started).toBeLessThan(15_000);
  }, 30_000);

  it('opens a header-only sheet without error', async () => {
    const ws = await load(buildXlsx({ columns: COLUMNS, rows: [] } as Table, new Date()));
    expect(ws.rowCount).toBe(1);
  });
});

describe('table and CSV agree', () => {
  it('uses the same column titles in the same order', () => {
    expect(CSV_HEADER).toBe(COLUMNS.map((c) => c.title).join(','));
  });

  it('gives the same values in the table and in the CSV for the same records', () => {
    const records = [record(), record({ n: 2, outcome: 'LOSS', note: 'has, comma and "quote"' }, -1_000_000, 1_000_000_000)];
    const fromCsv = parseCsv(buildCsv(records));
    const table = tableFromRecords(records);
    expect(fromCsv).toHaveLength(3);
    for (let r = 0; r < records.length; r++) {
      table.rows[r]!.forEach((cell, c) => {
        const text = fromCsv[r + 1]![c]!;
        if (typeof cell === 'number') expect(Number(text), `row ${r + 1} col ${COLUMNS[c]!.title}`).toBeCloseTo(cell, 2);
        else expect(text, `row ${r + 1} col ${COLUMNS[c]!.title}`).toBe(cell);
      });
    }
  });
});
