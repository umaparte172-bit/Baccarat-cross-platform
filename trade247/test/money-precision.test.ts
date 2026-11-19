import { describe, expect, it } from 'vitest';
import { fmtExact, toMicro } from '../src/engine/money.js';

describe('precise monetary display', () => {
  it.each([[0, '0.00'], [1, '1.00'], [-2.5, '-2.50'], [0.095, '0.095'], [-0.095, '-0.095'], [0.000001, '0.000001'], [1000.095, '1000.095']])('preserves %s as %s', (value, expected) => {
    expect(fmtExact(toMicro(Number(value)))).toBe(expected);
  });
});
