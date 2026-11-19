import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { expect, it } from 'vitest';
import { startChrome } from './chrome.js';

it('reports a missing test browser as a handled startup error, without waiting for the timeout', async () => {
  const executable = join(tmpdir(), `missing-test-chrome-${randomUUID()}`);
  await expect(startChrome(executable)).rejects.toThrow(/Chrome did not start:.*ENOENT/);
}, 5000);
