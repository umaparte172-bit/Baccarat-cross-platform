import { randomInt } from 'node:crypto';
import type { Cards, Result } from './types.js';

/** Small seeded PRNG so a simulator seed reproduces the same shoes. */
export function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

export const randomSeed = (): number => randomInt(1, 2 ** 31 - 1);

const CUT_CARD_REMAINING = 14;

/** Eight decks, ranks 1-13 (A..K), 32 of each, as point values (10/J/Q/K = 0). */
export class Shoe {
  private cards: number[] = [];
  private cursor = 0;

  constructor(private readonly rng: () => number) {
    for (let deck = 0; deck < 8; deck++) {
      for (let suit = 0; suit < 4; suit++) {
        for (let rank = 1; rank <= 13; rank++) this.cards.push(rank <= 9 ? rank : 0);
      }
    }
    for (let i = this.cards.length - 1; i > 0; i--) {
      const j = Math.floor(this.rng() * (i + 1));
      [this.cards[i], this.cards[j]] = [this.cards[j]!, this.cards[i]!];
    }
  }

  get remaining(): number {
    return this.cards.length - this.cursor;
  }

  /** The cut card has been reached: the current hand is the last of the shoe. */
  get cutReached(): boolean {
    return this.remaining <= CUT_CARD_REMAINING;
  }

  draw(): number {
    const card = this.cards[this.cursor++];
    if (card === undefined) throw new Error('The shoe is empty.');
    return card;
  }
}

const total = (cards: number[]): number => cards.reduce((a, b) => a + b, 0) % 10;

/** Standard punto banco banker drawing rule once the player has drawn (or stood). */
export function bankerDraws(bankerTotal: number, playerThird: number | null): boolean {
  if (playerThird === null) return bankerTotal <= 5;
  switch (bankerTotal) {
    case 0:
    case 1:
    case 2:
      return true;
    case 3:
      return playerThird !== 8;
    case 4:
      return playerThird >= 2 && playerThird <= 7;
    case 5:
      return playerThird >= 4 && playerThird <= 7;
    case 6:
      return playerThird === 6 || playerThird === 7;
    default:
      return false;
  }
}

/**
 * Only the result of the next hand, without building card arrays. It draws cards in exactly the same
 * order as dealHand, so both give the same hands from the same shoe (the backtest relies on the speed).
 */
export function dealResult(shoe: Shoe): Result {
  const p1 = shoe.draw();
  const b1 = shoe.draw();
  const p2 = shoe.draw();
  const b2 = shoe.draw();
  let pt = (p1 + p2) % 10;
  let bt = (b1 + b2) % 10;
  if (pt < 8 && bt < 8) {
    let third: number | null = null;
    if (pt <= 5) {
      third = shoe.draw();
      pt = (pt + third) % 10;
    }
    if (bankerDraws(bt, third)) bt = (bt + shoe.draw()) % 10;
  }
  return pt === bt ? 'T' : pt > bt ? 'P' : 'B';
}

export function dealHand(shoe: Shoe): { result: Result; cards: Cards } {
  const player = [shoe.draw()];
  const banker = [shoe.draw()];
  player.push(shoe.draw());
  banker.push(shoe.draw());
  const pt = total(player);
  const bt = total(banker);
  if (pt < 8 && bt < 8) {
    let third: number | null = null;
    if (pt <= 5) {
      third = shoe.draw();
      player.push(third);
    }
    if (bankerDraws(bt, third)) banker.push(shoe.draw());
  }
  const p = total(player);
  const b = total(banker);
  return { result: p === b ? 'T' : p > b ? 'P' : 'B', cards: [player, banker] };
}
