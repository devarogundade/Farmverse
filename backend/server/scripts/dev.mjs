// Dev runner: esbuild in watch mode (the same bundle as `npm run build`, so modules load exactly as
// in production; tsx can't import the TypeScript-source workspace packages as ES modules) and
// node --watch on the output. Usage: node scripts/dev.mjs main|worker|indexer
import { spawn } from 'node:child_process';
import { context } from 'esbuild';

const entry = process.argv[2] ?? 'main';
const outfile = `dist/dev/${entry}.js`;

const ctx = await context({
  entryPoints: [`src/${entry}.ts`],
  bundle: true,
  platform: 'node',
  target: 'node22',
  format: 'esm',
  sourcemap: true,
  outfile,
  external: ['@prisma/client', '@prisma/adapter-pg', 'pg', 'bullmq', 'ioredis', 'fastify', '@fastify/websocket', 'dotenv', 'jose'],
  logLevel: 'info',
});
await ctx.rebuild();
await ctx.watch();

const node = spawn(process.execPath, ['--enable-source-maps', '--watch', outfile], { stdio: 'inherit' });
const stop = async () => {
  node.kill();
  await ctx.dispose();
  process.exit(0);
};
process.on('SIGINT', stop);
process.on('SIGTERM', stop);
node.on('exit', (code) => process.exit(code ?? 0));
