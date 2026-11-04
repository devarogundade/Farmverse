import { defineConfig } from 'vitest/config';

export default defineConfig({
  // one emulator, one test file at a time: tests clear the database between cases
  test: { include: ['test/**/*.test.ts'], environment: 'node', fileParallelism: false, testTimeout: 15_000 },
});
