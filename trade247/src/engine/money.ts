// Money is held as integer micro-units (1e-6) so repeated 0.95 Banker payouts never drift.
export const MICRO = 1_000_000;

/** A Banker win pays 95% of the stake (5% commission); a Player win pays 1:1. */
export const BANKER_PAYOUT = 0.95;

export const toMicro =(value: number): number => Math.round(value * MICRO);
export const fromMicro = (micro: number): number => micro / MICRO;

/** Two-decimal string for display and logs (rounded half away from zero). */
export function fmt2(micro: number): string {
  const cents = Math.round(Math.abs(micro) / 10_000);
  const text = (cents / 100).toFixed(2);
  return micro < 0 && cents !== 0 ? `-${text}` : text;
}

/** Round a stake up to a multiple of the minimum chip (0 = no rounding). */
export function roundUpToChip(micro: number, chipMicro: number): number {
  if (chipMicro <= 0) return micro;
  return Math.ceil(micro / chipMicro) * chipMicro;
}
