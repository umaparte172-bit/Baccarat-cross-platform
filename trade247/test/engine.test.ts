import { describe, expect, it } from 'vitest';
import { Shoe, bankerDraws, dealHand, mulberry32 } from '../src/engine/dealer.js';
import { fmt2, roundUpToChip, toMicro } from '../src/engine/money.js';
import { DEFAULT_SETTINGS, TOKEN_MASK, normalizeSettings, publicSettings } from '../src/engine/settings.js';

describe('money', () => {
  it('rounds to two decimals without a negative zero', () => {
    expect(fmt2(toMicro(1.005))).toBe('1.01');
    expect(fmt2(toMicro(-2.5))).toBe('-2.50');
    expect(fmt2(-1000)).toBe('0.00');
    expect(fmt2(0)).toBe('0.00');
  });

  it('rounds stakes up to the chip and leaves them alone when the chip is 0', () => {
    expect(roundUpToChip(toMicro(0.12), toMicro(0.1))).toBe(toMicro(0.2));
    expect(roundUpToChip(toMicro(0.3), toMicro(0.1))).toBe(toMicro(0.3));
    expect(roundUpToChip(toMicro(0.123), 0)).toBe(toMicro(0.123));
  });
});

describe('dealer', () => {
  it('builds an eight-deck shoe with the right card mix', () => {
    const shoe = new Shoe(mulberry32(1));
    expect(shoe.remaining).toBe(416);
    const tally: Record<number, number> = {};
    for (let i = 0; i < 416; i++) {
      const c = shoe.draw();
      tally[c] = (tally[c] ?? 0) + 1;
    }
    expect(tally[0]).toBe(128);
    for (let v = 1; v <= 9; v++) expect(tally[v]).toBe(32);
    expect(() => shoe.draw()).toThrow('empty');
  });

  it('is reproducible for a seed and differs between seeds', () => {
    const run = (seed: number): string => {
      const shoe = new Shoe(mulberry32(seed));
      return Array.from({ length: 40 }, () => dealHand(shoe).result).join('');
    };
    expect(run(42)).toBe(run(42));
    expect(run(42)).not.toBe(run(43));
  });

  it('follows the banker drawing table', () => {
    const expected: Record<number, number[]> = {
      3: [0, 1, 2, 3, 4, 5, 6, 7, 9],
      4: [2, 3, 4, 5, 6, 7],
      5: [4, 5, 6, 7],
      6: [6, 7],
      7: [],
    };
    for (let bt = 0; bt <= 2; bt++) for (let p = 0; p <= 9; p++) expect(bankerDraws(bt, p)).toBe(true);
    for (const [bt, draws] of Object.entries(expected)) {
      for (let p = 0; p <= 9; p++) expect(bankerDraws(Number(bt), p), `banker ${bt} vs player third ${p}`).toBe(draws.includes(p));
    }
    for (let bt = 0; bt <= 9; bt++) expect(bankerDraws(bt, null)).toBe(bt <= 5);
  });

  it('deals consistent hands: totals decide the result and naturals stand', () => {
    const rng = mulberry32(7);
    let shoe = new Shoe(rng);
    const tally = { P: 0, B: 0, T: 0 };
    const total = (c: number[]): number => c.reduce((a, b) => a + b, 0) % 10;
    const hands = 20_000;
    for (let i = 0; i < hands; i++) {
      const { result, cards } = dealHand(shoe);
      const [p, b] = cards;
      const pt = total(p);
      const bt = total(b);
      expect(result).toBe(pt === bt ? 'T' : pt > bt ? 'P' : 'B');
      expect(p.length === 2 || p.length === 3).toBe(true);
      expect(b.length === 2 || b.length === 3).toBe(true);
      if (total(p.slice(0, 2)) >= 8 || total(b.slice(0, 2)) >= 8) {
        expect(p.length).toBe(2);
        expect(b.length).toBe(2);
      }
      tally[result]++;
      if (shoe.cutReached) shoe = new Shoe(rng);
    }
    // Published eight-deck frequencies: Banker 45.86%, Player 44.62%, Tie 9.52%.
    expect(Math.abs(tally.B / hands - 0.4586)).toBeLessThan(0.015);
    expect(Math.abs(tally.P / hands - 0.4462)).toBeLessThan(0.015);
    expect(Math.abs(tally.T / hands - 0.0952)).toBeLessThan(0.015);
  });
});

describe('settings', () => {
  it('accepts the defaults without errors', () => {
    expect(normalizeSettings({}).errors).toEqual([]);
  });

  it('converts the strings the page posts into numbers', () => {
    const { settings } = normalizeSettings({ unit_value: '0.25', single_wins_limit: '8', sim_interval: '0.5' });
    expect(settings.unit_value).toBe(0.25);
    expect(settings.single_wins_limit).toBe(8);
    expect(settings.sim_interval).toBe(0.5);
  });

  it('keeps the previous value for blanks and falls back for unknown choices', () => {
    const { settings } = normalizeSettings({ unit_value: '', strategy: 'NOPE', feed: 'teleport' }, { ...DEFAULT_SETTINGS, unit_value: 0.7 });
    expect(settings.unit_value).toBe(0.7);
    expect(settings.strategy).toBe('IHG');
    expect(settings.feed).toBe('simulator');
    expect(normalizeSettings({ feed: 'live' }).settings.feed).toBe('live');
  });

  it('clamps out-of-range values', () => {
    const { settings } = normalizeSettings({ ame_chaos_entropy: 5, ame_freeze_hands: 99, entry_loss_filter: -3 });
    expect(settings.ame_chaos_entropy).toBe(1);
    expect(settings.ame_freeze_hands).toBe(10);
    expect(settings.entry_loss_filter).toBe(0);
  });

  it('reports hard errors', () => {
    expect(normalizeSettings({ unit_value: 0 }).errors).toContain('Base Unit must be above 0');
    expect(normalizeSettings({ progression: 'list', chain: [] }).errors[0]).toMatch(/progression sequence/);
    expect(normalizeSettings({ cycle_pct_min: 30, cycle_pct_max: 10 }).errors[0]).toMatch(/up to %/);
  });

  it('treats the token mask as "unchanged" and never exposes the token', () => {
    const prev = { ...DEFAULT_SETTINGS, telegram_token: 'real-token' };
    expect(normalizeSettings({ telegram_token: TOKEN_MASK }, prev).settings.telegram_token).toBe('real-token');
    expect(normalizeSettings({ telegram_token: 'new-token' }, prev).settings.telegram_token).toBe('new-token');
    expect(normalizeSettings({ telegram_token: '' }, prev).settings.telegram_token).toBe('');
    expect(publicSettings(prev).telegram_token).toBe(TOKEN_MASK);
    expect(publicSettings(DEFAULT_SETTINGS).telegram_token).toBe('');
  });
});
