import 'dotenv/config';
import { cloudLogger } from '../../game/src/core/logger';
import { buildApp } from './app';
import { loadConfig } from './config';
import { redisClient, redisMarketBus } from './infra/redis';
import { LiveHub } from './live';
import { redisNoteMarks } from './market/marks';
import { MarketReader, MarketReaderV2 } from './market/read';
import { merchantsPage } from './chain/merchants';
import { MarketHub } from './market/routes';
import { buildServices } from './runtime';

/** The HTTP/WebSocket server process. Jobs run in worker.ts (scale them separately). */
async function main(): Promise<void> {
  const config = loadConfig();
  const log = cloudLogger.with({ service: 'farmverse-server', env: config.FARMVERSE_ENV });
  const services = buildServices(config, log);
  const hub = new LiveHub(services.reader, (t) => services.sessions.verify(t), log);
  await hub.start(services.bus);

  // The marketplace reads what the indexer process writes, and relays its updates live.
  const marketRedis = redisClient(config.REDIS_URL, 'market');
  const marketHub = new MarketHub(log);
  await marketHub.start(redisMarketBus(marketRedis, config.REDIS_URL, (error) => log.error('market bus', { error: String(error) })));

  // Maintenance, min version and the mint limits, changed live from the admin web.
  const ops = services.ops;
  await ops.start();

  const app = await buildApp({
    rpcUrl: config.NEAR_RPC_URL,
    ...(config.PRIVY_APP_ID && config.PRIVY_APP_SECRET && config.PRIVY_VERIFICATION_KEY
      ? { privy: { appId: config.PRIVY_APP_ID, appSecret: config.PRIVY_APP_SECRET, verificationKey: config.PRIVY_VERIFICATION_KEY } }
      : {}),
    rt: services.rt,
    sessions: services.sessions,
    reader: services.reader,
    hub,
    limiter: services.limiter,
    ops,
    admin: {
      prisma: services.prisma,
      env: { store: services.rt.store, clock: services.rt.clock, log },
      sessions: services.sessions,
      ops,
      limiter: services.limiter,
      itemsContract: config.NEAR_ITEMS_CONTRACT,
      adminAccounts: config.ADMIN_ACCOUNTS,
    },
    callDeps: {
      clock: services.rt.clock,
      log,
      minClientVersion: () => ops.current.minClientVersion,
      exposeIssues: config.FARMVERSE_ENV !== 'prod',
    },
    allowedOrigins: config.ALLOWED_ORIGINS,
    minClientVersion: config.MIN_CLIENT_VERSION,
    environment: config.FARMVERSE_ENV,
    market: {
      reader: new MarketReader(services.prisma, config.NEAR_ITEMS_CONTRACT),
      readerV2: new MarketReaderV2(services.prisma, config.NEAR_ITEMS_CONTRACT),
      merchants: () => merchantsPage(services.rt.store),
      marks: redisNoteMarks(marketRedis),
      limiter: services.limiter,
      hub: marketHub,
    },
  });

  const shutdown = async (signal: string) => {
    log.info('shutting down', { signal });
    await hub.stop();
    await marketHub.stop();
    ops.stop();
    await app.close();
    await marketRedis.quit();
    await services.close();
    process.exit(0);
  };
  process.on('SIGTERM', () => void shutdown('SIGTERM'));
  process.on('SIGINT', () => void shutdown('SIGINT'));

  await app.listen({ host: '0.0.0.0', port: config.PORT });
  log.info('listening', { port: config.PORT });
}

main().catch((error: unknown) => {
  cloudLogger.error('server failed to start', { error: String(error), stack: (error as Error).stack });
  process.exit(1);
});
