import { describe, expect, it } from 'vitest';
import type { Result } from '../src/engine/types.js';
import { RoadTracker } from '../src/live/road.js';

const r = (s: string): Result[] => s.split('') as Result[];
const play = (t: RoadTracker, s: string) => t.consume(r(s)).map((h) => h.result + (h.newShoe ? '*' : '')).join(' ');

describe('road tracker', () => {
  it('treats the first reading as history', () => {
    const t = new RoadTracker();
    expect(play(t, 'PBBT')).toBe('');
    expect(play(t, 'PBBT')).toBe('');
    expect(play(t, 'PBBTP')).toBe('P');
  });

  it('reports several new hands in order, once', () => {
    const t = new RoadTracker();
    play(t, 'PB');
    expect(play(t, 'PBTBB')).toBe('T B B');
    expect(play(t, 'PBTBB')).toBe('');
  });

  it('ignores a one-off short or garbled reading (a page half redrawn) and carries on', () => {
    const t = new RoadTracker();
    play(t, 'PBTP');
    expect(play(t, 'PB')).toBe('');
    expect(play(t, 'PBTP')).toBe('');
    expect(play(t, 'PBTPB')).toBe('B');
  });

  it('starts a new shoe only when two readings in a row agree, and flags its first hand only', () => {
    const t = new RoadTracker();
    play(t, 'PBTP');
    expect(play(t, '')).toBe('');
    expect(play(t, '')).toBe('');
    expect(play(t, 'B')).toBe('B*');
    expect(play(t, 'BP')).toBe('P');
  });

  it('handles a new shoe whose first hands were dealt between two readings', () => {
    const t = new RoadTracker();
    play(t, 'PBTP');
    expect(play(t, 'BB')).toBe('');
    expect(play(t, 'BB')).toBe('B* B');
  });

  it('two different short readings do not confirm each other', () => {
    const t = new RoadTracker();
    play(t, 'PBTP');
    expect(play(t, 'P')).toBe('');
    expect(play(t, 'B')).toBe('');
    expect(play(t, 'PBTP')).toBe('');
  });

  it('forget makes the next reading history again, so hands missed in an outage are not replayed', () => {
    const t = new RoadTracker();
    play(t, 'PB');
    t.forget();
    expect(play(t, 'PBBBP')).toBe('');
    expect(play(t, 'PBBBPT')).toBe('T');
  });
});
