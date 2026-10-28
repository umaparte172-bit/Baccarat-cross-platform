/**
 * Which chips make up a stake: the fewest clicks that add up to it exactly (a coin-change search over cents).
 * Chips are table chips (for example 0.1, 1, 5, 25, 100). Pure; the bettor clicks what this returns.
 */

export type ChipChoice = { ok: true; chips: number[] } | { ok: false; reason: string };

const LIMIT_CENTS = 1_000_000; // 10,000.00

export function selectChips(stake: number, available: readonly number[], maxClicks = 40): ChipChoice {
  const cents = Math.round(stake * 100);
  if (!Number.isFinite(stake) || stake <= 0 || Math.abs(cents - stake * 100) > 1e-6) {
    return { ok: false, reason: 'The stake must be above 0 with at most two decimal places' };
  }
  if (cents > LIMIT_CENTS) return { ok: false, reason: 'The stake is above the chip solver limit (10,000)' };

  const chips = [...new Set(available.map((v) => Math.round(v * 100)).filter((v) => v > 0 && v <= cents))].sort((a, b) => b - a);
  if (chips.length === 0) return { ok: false, reason: 'No chip on the table is small enough for this stake' };

  const count = new Array<number>(cents + 1).fill(Infinity);
  const from = new Array<number>(cents + 1).fill(0);
  const used = new Array<number>(cents + 1).fill(0);
  count[0] = 0;
  for (let amount = 1; amount <= cents; amount++) {
    for (let i = 0; i < chips.length; i++) {
      const prior = amount - chips[i]!;
      if (prior < 0 || count[prior]! === Infinity || count[prior]! + 1 >= count[amount]!) continue;
      count[amount] = count[prior]! + 1;
      from[amount] = prior;
      used[amount] = i;
    }
  }
  if (count[cents] === Infinity) return { ok: false, reason: 'This stake cannot be made from the chips on the table' };
  if (count[cents]! > maxClicks) return { ok: false, reason: `This stake needs ${count[cents]} chip clicks (limit ${maxClicks})` };

  const out: number[] = [];
  for (let amount = cents; amount > 0; amount = from[amount]!) out.push(chips[used[amount]!]! / 100);
  return { ok: true, chips: out.sort((a, b) => b - a) };
}
