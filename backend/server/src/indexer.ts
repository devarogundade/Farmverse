import 'dotenv/config';
import { PrismaPg } from '@prisma/adapter-pg';
import { cloudLogger } from '../../functions/src/core/logger';
import { ChainIndexer } from './chain/indexer';
import { NEARDATA_URL, neardataSource } from './chain/neardata';
import { loadConfig } from './config';
import { PrismaClient } from './generated/prisma/client';
import { redisClient, redisMarketBus } from './infra/redis';

/**
 * The chain indexer process (docs 13 §9): follows final NEAR blocks and keeps the `chain` schema
 * that the marketplace API reads. Run exactly one per database.
 */
async function main(): Promise<void> {
  const config = loadConfig();
  const log = cloudLogger.with({ service: 'farmverse-indexer', env: config.FARMVERSE_ENV });
  const prisma = new PrismaClient({ adapter: new PrismaPg({ connectionString: config.DATABASE_URL, max: 4 }) });
  const redis = redisClient(config.REDIS_URL, 'indexer');
  const bus = redisMarketBus(redis, config.REDIS_URL, (error) => log.error('market bus', { error: String(error) }));

  const watched = { items: config.NEAR_ITEMS_CONTRACT, market: config.NEAR_MARKET_CONTRACT };
  const indexer = new ChainIndexer({
    prisma,
    source: neardataSource(config.NEARDATA_URL ?? NEARDATA_URL[config.NEAR_NETWORK], {
      apiKey: config.NEARDATA_API_KEY,
      requestsPerMinute: config.NEARDATA_RPM,
      onRateLimited: (path, waitMs) => log.warn('neardata rate limit hit; lower NEARDATA_RPM if this repeats', { path, waitMs }),
    }),
    // A request in flight per ~second of pacing keeps the pacer, not the network, the bottleneck.
    concurrency: Math.max(2, Math.ceil((config.NEARDATA_RPM ?? 170) / 60)),
    batch: 60,
    watched,
    log,
    startHeight: config.INDEXER_START_BLOCK,
    publish: (messages) => bus.publish(messages),
  });

  const stop = new AbortController();
  const shutdown = (signal: string) => {
    log.info('shutting down', { signal });
    stop.abort();
  };
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));

  log.info('indexer started', { network: config.NEAR_NETWORK, ...watched, from: (await indexer.cursor()) + 1 });
  await indexer.run(stop.signal);
  await redis.quit();
  await prisma.$disconnect();
  process.exit(0);
}

main().catch((error: unknown) => {
  cloudLogger.error('indexer failed to start', { error: String(error), stack: (error as Error).stack });
  process.exit(1);
});
