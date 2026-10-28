import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { BetCoordinator, type BetHost } from '../src/betting.js';
import { PlayCore } from '../src/engine/play.js';
import { DEFAULT_SETTINGS, normalizeSettings, type Settings } from '../src/engine/settings.js';
import type { Side } from '../src/engine/types.js';
import type { Placement } from '../src/live/bettor.js';

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

interface Fake {
  host: BetHost;
  core: PlayCore;
  coordinator: BetCoordinator;
  calls: Array<{ side: Side; stake: number }>;
  stops: Array<{ status: string; message: string }>;
  state: { running: boolean; paused: boolean; lost: boolean; hands: number; settings: Settings; outcome: Placement };
}

function fake(over: Record<string, unknown> = {}): Fake {
  const settings = normalizeSettings(
    { feed: 'live', bet_mode: 'auto', bet_max_stake: 10, bet_max_session: 30, bet_delay_min: 1, bet_delay_max: 1, unit_value: 1, min_chip: 1, ...over },
    DEFAULT_SETTINGS,
  ).settings;
  const made = PlayCore.create(settings);
  if ('error' in made) throw new Error(made.error);
  const state = { running: true, paused: false, lost: false, hands: 0, settings, outcome: { kind: 'accepted', chips: [1], total: 1 } as Placement };
  const calls: Fake['calls'] = [];
  const stops: Fake['stops'] = [];
  const host: BetHost = {
    settings: () => state.settings,
    core: () => made.core,
    now: () => Date.now(),
    running: () => state.running,
    paused: () => state.paused,
    feedLost: () => state.lost,
    handsRecorded: () => state.hands,
    random: () => 0.5,
    place: async (side, stake) => {
      calls.push({ side, stake });
      return state.outcome;
    },
    stopForBet: (status, message) => {
      stops.push({ status, message });
      state.running = false;
    },
  };
  return { host, core: made.core, coordinator: new BetCoordinator(host), calls, stops, state };
}

describe('when a bet is placed', () => {
  it('waits the configured pause, then places exactly the bet the strategy wants', async () => {
    const f = fake({ bet_delay_min: 1, bet_delay_max: 3 }); // random() = 0.5 -> 2 seconds
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1900);
    expect(f.calls).toHaveLength(0);
    await vi.advanceTimersByTimeAsync(200);
    expect(f.calls).toEqual([{ side: 'P', stake: 1 }]);
    expect(f.coordinator.view()).toMatchObject({ on_table: true, staked: 1 });
  });

  it('places nothing when the strategy has no bet, in paper mode, when paused, or when the feed is lost', async () => {
    const watch = fake({ strategy: 'ODD_DIFF' });
    watch.coordinator.onWindowOpen();
    const paper = fake({ bet_mode: 'paper' });
    paper.coordinator.onWindowOpen();
    const paused = fake();
    paused.state.paused = true;
    paused.coordinator.onWindowOpen();
    const lost = fake();
    lost.state.lost = true;
    lost.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(5000);
    for (const f of [watch, paper, paused, lost]) expect(f.calls).toHaveLength(0);
  });

  it('does not place a second bet while one is on the table', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(5000);
    expect(f.calls).toHaveLength(1);
  });

  it('skips the bet if a hand was settled while it waited (the situation it was prepared for is gone)', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    f.state.hands = 1; // a result arrived during the pause, and the strategy still wants the same bet
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.calls).toHaveLength(0);
    expect(f.coordinator.view().note).toContain('situation changed');
  });

  it('skips the bet if the user paused during the pause before it', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    f.state.paused = true;
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.calls).toHaveLength(0);
  });

  it('skips the bet if the strategy no longer wants it', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    f.core.play({ result: 'B' }, false, 0); // the strategy moves on without the coordinator being told
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.calls).toHaveLength(0);
  });
});

describe('the safety limits', () => {
  it('stop the session instead of placing a bet above the largest allowed stake', () => {
    const f = fake({ bet_max_stake: 1, bet_max_session: 10, strategy: 'MARTINGALE' });
    f.core.play({ result: 'B' }, false, 0); // the Martingale loses: next stake is 2
    f.coordinator.onWindowOpen();
    expect(f.stops).toHaveLength(1);
    expect(f.stops[0]!.status).toContain('safety limit');
    expect(f.calls).toHaveLength(0);
  });

  it('stop the session at the staking limit, counting what is already staked', async () => {
    const f = fake({ strategy: 'MARTINGALE', first_side: 'P', bet_max_stake: 4, bet_max_session: 3 });
    const round = async (result: 'P' | 'B') => {
      f.coordinator.onWindowOpen();
      await vi.advanceTimersByTimeAsync(1100);
      f.coordinator.beforeHand(f.core, false);
      f.core.play({ result }, false, 0);
      f.state.hands++;
    };
    await round('B'); // Player 1 loses: staked 1
    await round('P'); // Banker 2 loses: staked 3
    expect(f.calls.map((c) => c.stake)).toEqual([1, 2]);
    f.coordinator.onWindowOpen(); // the next stake is 4 (within the largest bet) but would take the session to 7
    expect(f.stops).toHaveLength(1);
    expect(f.stops[0]!.status).toContain('staking limit');
    expect(f.calls).toHaveLength(2);
  });
});

describe('what the table answers', () => {
  it('a rejected bet is noted, and the hand is later booked as only watched', async () => {
    const f = fake();
    f.state.outcome = { kind: 'rejected', reason: 'Bets are not open' };
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.coordinator.view()).toMatchObject({ on_table: false, staked: 0, note: 'bet not placed: Bets are not open' });
    expect(f.coordinator.beforeHand(f.core, false)).toEqual({ action: 'play', forcePaused: true, note: 'bet not placed: Bets are not open' });
    expect(f.coordinator.view().note).toBe('');
  });

  it('an unconfirmed bet stops the session, with the reason', async () => {
    const f = fake();
    f.state.outcome = { kind: 'unconfirmed', reason: 'total 0, expected 1' };
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.stops).toHaveLength(1);
    expect(f.stops[0]).toMatchObject({ status: expect.stringContaining('not confirmed'), message: expect.stringContaining('total 0, expected 1') });
  });

  it('a placement that throws counts as unconfirmed, never as "nothing happened"', async () => {
    const f = fake();
    f.host.place = async () => {
      throw new Error('the connection dropped');
    };
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.stops[0]!.message).toContain('the connection dropped');
  });

  it('a result that arrives while the bet is being placed stops the session (it cannot be matched to a round)', async () => {
    const f = fake();
    f.host.place = async () => {
      f.state.hands = 1;
      return f.state.outcome;
    };
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.stops[0]!.status).toContain('too late');
  });
});

describe('settling the hand', () => {
  it('a placed bet that matches the strategy is settled normally', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.coordinator.beforeHand(f.core, false)).toEqual({ action: 'play', forcePaused: false, note: '' });
    expect(f.coordinator.busy).toBe(false);
  });

  it('a placed bet that differs from the strategy stops the session', async () => {
    const f = fake({ shoe_change_policy: 'reset_all', strategy: 'MARTINGALE', first_side: 'P' });
    f.core.play({ result: 'B' }, false, 0); // loss: next bet is Banker, 2 units
    f.state.hands = 1;
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.calls).toEqual([{ side: 'B', stake: 2 }]);
    f.core.shoeChange(); // a new shoe resets the progression while the bet is on the table
    expect(f.coordinator.beforeHand(f.core, false)).toEqual({ action: 'stop' });
    expect(f.stops[0]!.status).toContain('differs from the strategy');
  });

  it('a hand with no bet wanted and none placed is settled normally', () => {
    const f = fake({ strategy: 'ODD_DIFF' });
    expect(f.coordinator.beforeHand(f.core, false)).toEqual({ action: 'play', forcePaused: false, note: '' });
  });

  it('paper mode never interferes', () => {
    const f = fake({ bet_mode: 'paper' });
    expect(f.coordinator.beforeHand(f.core, false)).toEqual({ action: 'play', forcePaused: false, note: '' });
  });
});

describe('confirm mode', () => {
  it('holds the bet until approved, then places it; skipping or timing out drops it', async () => {
    const f = fake({ bet_mode: 'confirm', bet_confirm_seconds: 5 });
    f.coordinator.onWindowOpen();
    expect(f.coordinator.view().pending).toMatchObject({ pick: 'PLAYER', stake: 1 });
    await vi.advanceTimersByTimeAsync(4000);
    expect(f.calls).toHaveLength(0);
    expect(f.coordinator.approve()).toEqual({ ok: true });
    await vi.advanceTimersByTimeAsync(10);
    expect(f.calls).toEqual([{ side: 'P', stake: 1 }]);
    expect(f.coordinator.view().pending).toBeNull();

    const g = fake({ bet_mode: 'confirm', bet_confirm_seconds: 5 });
    g.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(6000);
    expect(g.coordinator.view()).toMatchObject({ pending: null, note: 'bet skipped: not approved in time' });
    expect(g.calls).toHaveLength(0);

    const h = fake({ bet_mode: 'confirm' });
    h.coordinator.onWindowOpen();
    expect(h.coordinator.skip()).toEqual({ ok: true });
    expect(h.coordinator.approve().ok).toBe(false);
  });

  it('a keep-alive bet needs no approval: it is the anti-kick minimum bet, so it is placed by itself, while a strategy bet still waits', async () => {
    const f = fake({ bet_mode: 'confirm', strategy: 'ODD_DIFF', keepalive_enabled: true, keepalive_amount: 0.5, keepalive_idle: 1 });
    // Idle hands (the strategy has no pick yet) until the minimum bet is due.
    for (let i = 0; i < 8 && !f.core.intendedBet(false)?.keepAlive; i++) {
      f.core.play({ result: i % 2 ? 'B' : 'P' }, false, 0);
      f.state.hands++;
    }
    expect(f.core.intendedBet(false)).toMatchObject({ keepAlive: true });
    f.coordinator.onWindowOpen();
    expect(f.coordinator.view().pending).toBeNull();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.calls).toEqual([{ side: expect.any(String), stake: 1 }]);

    const g = fake({ bet_mode: 'confirm' }); // a real strategy bet in the same mode is held for approval
    g.coordinator.onWindowOpen();
    expect(g.coordinator.view().pending).toMatchObject({ keep_alive: false });
    await vi.advanceTimersByTimeAsync(1100);
    expect(g.calls).toHaveLength(0);
  });

  it('a pause is held back while a bet is on the table', async () => {
    const f = fake();
    f.coordinator.onWindowOpen();
    await vi.advanceTimersByTimeAsync(1100);
    expect(f.coordinator.busy).toBe(true);
    f.coordinator.requestPauseAfterResult();
    expect(f.coordinator.takePauseRequest()).toBe(true);
    expect(f.coordinator.takePauseRequest()).toBe(false);
  });
});
