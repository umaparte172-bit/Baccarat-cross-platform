import { describe, expect, it } from 'vitest';
import { selectChips } from '../src/engine/chips.js';

const sum = (a: number[]) => Math.round(a.reduce((x, y) => x + y, 0) * 100) / 100;

describe('chip selection', () => {
  const table = [0.1, 1, 5, 25, 100];

  it('makes the stake exactly, with the fewest chips', () => {
    expect(selectChips(7, table)).toEqual({ ok: true, chips: [5, 1, 1] });
    expect(selectChips(125, table)).toEqual({ ok: true, chips: [100, 25] });
    expect(selectChips(0.3, table)).toEqual({ ok: true, chips: [0.1, 0.1, 0.1] });
    const r = selectChips(30, [1, 10, 25]);
    expect(r).toEqual({ ok: true, chips: [10, 10, 10] }); // 3 chips beat 25 + five 1s
  });

  it('works where greedy would fail', () => {
    const r = selectChips(6, [1, 3, 4]);
    expect(r).toEqual({ ok: true, chips: [3, 3] }); // greedy takes 4 + 1 + 1
  });

  it('always adds up to the stake, whatever the chips', () => {
    for (const stake of [0.1, 0.9, 1.5, 2.35, 13.7, 99.95, 480]) {
      const r = selectChips(stake, [0.05, 0.5, 2, 7, 20, 50]);
      expect(r.ok, String(stake)).toBe(true);
      if (r.ok) expect(sum(r.chips), String(stake)).toBe(stake);
    }
  });

  it('says why it cannot', () => {
    expect(selectChips(0, table)).toMatchObject({ ok: false });
    expect(selectChips(-1, table)).toMatchObject({ ok: false });
    expect(selectChips(Number.NaN, table)).toMatchObject({ ok: false });
    expect(selectChips(1.005, table)).toMatchObject({ ok: false, reason: expect.stringContaining('two decimal') });
    expect(selectChips(0.05, [1, 5])).toMatchObject({ ok: false, reason: expect.stringContaining('small enough') });
    expect(selectChips(3, [2, 4])).toMatchObject({ ok: false, reason: expect.stringContaining('cannot be made') });
    expect(selectChips(20_000, table)).toMatchObject({ ok: false, reason: expect.stringContaining('limit') });
    expect(selectChips(1000, [1], 10)).toMatchObject({ ok: false, reason: expect.stringContaining('10') });
  });

  it('ignores chips twice listed or larger than the stake', () => {
    expect(selectChips(5, [5, 5, 100, 1000])).toEqual({ ok: true, chips: [5] });
  });
});
