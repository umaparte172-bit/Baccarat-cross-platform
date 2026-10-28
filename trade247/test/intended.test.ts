import { describe, expect, it } from 'vitest';
import { PlayCore } from '../src/engine/play.js';
import { DEFAULT_SETTINGS, normalizeSettings } from '../src/engine/settings.js';
import type { Result } from '../src/engine/types.js';

/**
 * The bet the program places on a live table is `intendedBet()`, worked out before the hand. The hand is then
 * settled by `play()`. They must always agree, or the books would say something other than what is on the table.
 */

const variants: Array<[string, Record<string, unknown>]> = [
  ['IHG', {}],
  ['IHG with keep-alive', { keepalive_enabled: true, keepalive_amount: 0.5, keepalive_idle: 3 }],
  ['IHG, cut the open run near the shoe end', { end_shoe_policy: 'cut_run', end_shoe_hand: 5 }],
  ['IHG, no new runs near the shoe end', { end_shoe_policy: 'no_new_runs', end_shoe_hand: 6 }],
  ['IHG, pause after two losses', { pause_consec_loss_count: 2, pause_bet_count: 3 }],
  ['IHG, wait for pattern after losses', { stop_consec_loss_count: 3, stop_consec_loss_action: 'WAIT FOR PATTERN' }],
  ['IHG with a hard cap', { hard_cap_mode: 'custom', hard_cap_units: 8 }],
  ['Martingale, cut near shoe end', { strategy: 'MARTINGALE', end_shoe_policy: 'cut_run', end_shoe_hand: 4 }],
  ['ODD Diff with keep-alive', { strategy: 'ODD_DIFF', progression: 'list', chain: [1, 2, 3], keepalive_enabled: true, keepalive_amount: 0.5, keepalive_idle: 2 }],
  ['Sequence, no new runs', { strategy: 'SEQUENCE', progression: 'list', chain: [1, 2, 4], sequence_sides: 'B,P,P', end_shoe_policy: 'no_new_runs', end_shoe_hand: 7 }],
  ['Grinding, pause after losses', { strategy: 'GRINDING', pause_consec_loss_count: 2, pause_bet_count: 2 }],
];

describe('the bet that is placed is the bet that is settled', () => {
  it.each(variants)('%s', (_name, over) => {
    const settings = normalizeSettings({ unit_value: 1, min_chip: 0.5, start_balance: 1000, ...over }, DEFAULT_SETTINGS).settings;
    const made = PlayCore.create(settings);
    if ('error' in made) throw new Error(made.error);
    const core = made.core;
    let seed = 7;
    const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 2 ** 32);
    let withBet = 0;
    for (let i = 0; i < 1500; i++) {
      if (i > 0 && i % 12 === 0) core.shoeChange();
      const intended = core.intendedBet(false);
      const before = JSON.stringify(core.engine.snapshot());
      const again = core.intendedBet(false);
      expect(JSON.stringify(core.engine.snapshot()), `hand ${i}: asking changed the engine`).toBe(before);
      expect(again).toEqual(intended);

      const r: Result = rnd() < 0.1 ? 'T' : rnd() < 0.5 ? 'P' : 'B';
      const played = core.play({ result: r }, false, 0);
      const real = played.mode === 'REAL' || played.mode === 'MIN';
      expect(intended !== null, `hand ${i}: a bet was ${real ? 'settled' : 'not settled'} but ${intended ? 'intended' : 'not intended'}`).toBe(real);
      if (intended) {
        withBet++;
        expect({ side: played.pick, stakeMicro: played.stakeMicro, keepAlive: played.mode === 'MIN' }, `hand ${i}`).toEqual(intended);
      }
      if (played.verdict.stop) break;
    }
    expect(withBet).toBeGreaterThan(20);
  });

  it('a manual pause intends nothing', () => {
    const made = PlayCore.create(normalizeSettings({ unit_value: 1 }, DEFAULT_SETTINGS).settings);
    if ('error' in made) throw new Error(made.error);
    expect(made.core.intendedBet(false)).not.toBeNull();
    expect(made.core.intendedBet(true)).toBeNull();
  });
});
