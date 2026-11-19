import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    include: ['test/**/*.test.ts'],
    environment: 'node',
    testTimeout: 15000,
    // The Chrome integration suites each own a temporary browser. Running those files in parallel
    // can exhaust/terminate Chrome on Windows and make otherwise healthy CDP tests fail randomly.
    fileParallelism: false,
  },
});
