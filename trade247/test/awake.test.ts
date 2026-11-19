import { expect, it, vi } from 'vitest';
import { SessionAwake } from '../electron/awake.js';

it('holds one sleep blocker for a running session and releases it on stop/shutdown', () => {
  const blocker = { start: vi.fn(() => 0), stop: vi.fn() };
  const awake = new SessionAwake(blocker);
  awake.update(false);
  expect(blocker.start).not.toHaveBeenCalled();
  awake.update(true);
  awake.update(true);
  expect(blocker.start).toHaveBeenCalledExactlyOnceWith('prevent-app-suspension');
  awake.update(false);
  awake.update(false);
  expect(blocker.stop).toHaveBeenCalledExactlyOnceWith(0);
  awake.update(true);
  expect(blocker.start).toHaveBeenCalledTimes(2);
});
