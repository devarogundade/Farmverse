import { defineConfig } from 'vitest/config';

// Unit tests: no database, no Redis.
export default defineConfig({
  test: { include: ['test/**/*.test.ts'], exclude: ['test/db/**'], environment: 'node' },
});
