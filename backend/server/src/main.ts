import 'dotenv/config';
import { cloudLogger } from '../../game/src/core/logger';
import { buildApp } from './app';
import { loadConfig } from './config';
import { veBetterPayer } from './coupons/vebetter';
import { redisClient, redisMarketBus } from './infra/redis';
import { LiveHub } from './live';
import { redisNoteMarks } from './market/marks';
import { MarketReader, MarketReaderV2 } from './market/read';
import { merchantsPage } from './chain/merchants';
import { farmQuote, intentsTokens } from './chain/intents';
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
    ...(config.PRIVY_APP_ID && config.PRIVY_APP_SECRET
      ? {
          privy: {
            appId: config.PRIVY_APP_ID,
            appSecret: config.PRIVY_APP_SECRET,
            ...(config.PRIVY_VERIFICATION_KEY ? { verificationKey: config.PRIVY_VERIFICATION_KEY } : {}),
          },
        }
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
    coupons: {
      env: { store: services.rt.store, clock: services.rt.clock, log },
      log,
      limiter: services.limiter,
      explorerTxUrl: config.VECHAIN_EXPLORER_TX_URL,
      payers:
        config.VECHAIN_NODE_URL && config.X2EARN_REWARDS_POOL && config.X2EARN_APP_ID && config.X2EARN_SIGNER_KEY
          ? [veBetterPayer({ nodeUrl: config.VECHAIN_NODE_URL, pool: config.X2EARN_REWARDS_POOL, appId: config.X2EARN_APP_ID, signerKey: config.X2EARN_SIGNER_KEY }, log)]
          : [],
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
      intents: (() => {
        const cfg = {
          live: config.INTENTS_LIVE === 'on' && config.NEAR_NETWORK === 'mainnet',
          ...(config.ONECLICK_API_KEY ? { apiKey: config.ONECLICK_API_KEY } : {}),
          treasury: config.NEAR_TREASURY,
          farmPerNear: config.FARM_PER_NEAR,
        };
        let tokens: { at: number; data: unknown } | null = null;
        return {
          tokens: async () => {
            if (!tokens || Date.now() - tokens.at > 600_000) tokens = { at: Date.now(), data: await intentsTokens(cfg) };
            return tokens.data;
          },
          quote: (asset: string, amount: string, refundTo: string, account: string | null) =>
            farmQuote(cfg, services.rt.store, account, asset, amount, refundTo, Date.now()),
        };
      })(),
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
