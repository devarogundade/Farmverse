import { defineConfig } from 'vitest/config';

// Tests against real Postgres and Redis (DATABASE_URL / REDIS_URL). One file at a time: they share the database.
export default defineConfig({
  test: { include: ['test/db/**/*.test.ts'], environment: 'node', fileParallelism: false, testTimeout: 30_000, hookTimeout: 30_000 },
});
