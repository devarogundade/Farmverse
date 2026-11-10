import 'dotenv/config';
import { defineConfig } from 'prisma/config';

export default defineConfig({
  schema: 'prisma/schema.prisma',
  migrations: { path: 'prisma/migrations' },
  // Optional so `prisma generate` (typecheck, build) works on a fresh clone with no .env;
  // migrate and the server still need DATABASE_URL.
  datasource: { url: process.env['DATABASE_URL'] },
});
