import { PrismaPg } from '@prisma/adapter-pg';
import { systemClock } from '../../game/src/core/clock';
import { cloudLogger, type Logger } from '../../game/src/core/logger';
import { usernameKey } from '../../game/src/game/onboarding';
import { httpEngine } from '../../game/src/infra/islandEngine';
import { paths } from '../../game/src/infra/paths';
import { httpNearRpc } from './auth/nearRpc';
import { Sessions } from './auth/sessions';
import { OpsConfig } from './admin/ops';
import { webMessenger, type Runtime } from './calls';
import type { Config } from './config';
import { PrismaClient } from './generated/prisma/client';
import { redisChangeBus, redisClient, redisNonces, redisRateLimiter, type ChangeBus, type RateLimiter } from './infra/redis';
import { bullMintExpiry, bullTasks, bullWakes, jobQueues, type JobQueues } from './jobs/queues';
import { rpcChainReader } from './mint/nearChain';
import { VoucherSigner } from './mint/voucher';
import type { DocReader } from './live';
import { prismaStore, type DocChange } from './store/prismaStore';
import { queryDocuments } from './store/query';
import { nearFaucetSender } from './chain/faucet';
import { unclaimedRedeems } from './chain/unclaimed';

export interface Services {
  readonly rt: Runtime;
  readonly prisma: PrismaClient;
  readonly sessions: Sessions;
  readonly reader: DocReader;
  readonly bus: ChangeBus;
  readonly limiter: RateLimiter;
  readonly queues: JobQueues;
  /** The admin web's live switches (started by the API process; others read the defaults). */
  readonly ops: OpsConfig;
  close(): Promise<void>;
}

export function prismaReader(prisma: PrismaClient): DocReader {
  return {
    async get(path) {
      const row = await prisma.document.findUnique({ where: { path }, select: { data: true, rev: true } });
      return row ? { exists: true, rev: Number(row.rev), data: row.data as Record<string, unknown> } : { exists: false, rev: 0, data: undefined };
    },
    query: (q) => queryDocuments(prisma, q),
  };
}

/**
 * What a commit sets off besides live updates — the Firestore triggers of the old backend:
 * a news item published books the news fan-out.
 */
export function triggersFor(queues: JobQueues, log: Logger) {
  return (changes: readonly DocChange[]): void => {
    for (const c of changes) {
      if (c.created && c.collection === 'news') {
        const newsId = c.path.slice('news/'.length);
        queues.newsFanout.add('fanout', { newsId }, { jobId: `news-${newsId}` }).catch((error: unknown) => log.error('news fan-out not booked', { newsId, error: String(error) }));
      }
    }
  };
}

/** Builds every long-lived dependency of a server or worker process from its configuration. */
export function buildServices(config: Config, log: Logger = cloudLogger): Services {
  const prisma = new PrismaClient({ adapter: new PrismaPg({ connectionString: config.DATABASE_URL, max: 20 }) });
  const redis = redisClient(config.REDIS_URL, 'main');
  const queues = jobQueues(redisClient(config.REDIS_URL, 'queues'));
  const bus = redisChangeBus(redis, config.REDIS_URL, (error) => log.error('change bus', { error: String(error) }));
  const triggers = triggersFor(queues, log);

  const store = prismaStore(prisma, {
    onCommit: (changes) => {
      bus.publish(changes);
      triggers(changes);
    },
  });
  // maintenance, min version and the mint limits, changed live from the admin web
  const ops = new OpsConfig(store, config.MIN_CLIENT_VERSION, log, {
    minAccountHours: config.MINT_MIN_ACCOUNT_HOURS,
    dailyValueCap: config.MINT_DAILY_VALUE_CAP,
  });
  const sessions = new Sessions(prisma, redisNonces(redis), httpNearRpc(config.NEAR_RPC_URL), {
    jwtSeed: Buffer.from(config.JWT_SEED, 'base64'),
    recipient: config.AUTH_RECIPIENT,
    network: config.NEAR_NETWORK,
    adminAccounts: config.ADMIN_ACCOUNTS,
    allowDevSignIn: config.FARMVERSE_ENV === 'dev',
  });

  const rt: Runtime = {
    store,
    clock: systemClock,
    log,
    engine: httpEngine(config.ISLAND_ENGINE_URL),
    wakes: bullWakes(queues.islandWake),
    tasks: bullTasks(queues.pushTask),
    messenger: webMessenger(log),
    deleteAccount: async (uid, l) => {
      const player = await store.read(paths.player(uid));
      const name = (player.data?.['displayName'] as string | undefined) ?? undefined;
      if (name) await store.runTransaction(async (tx) => tx.delete(paths.username(usernameKey(name))));
      const removed = await prisma.document.deleteMany({ where: { uid } });
      await sessions.signOutEverywhere(uid);
      l.info('account deleted', { documents: removed.count });
    },
    ...(config.MINT_SIGNER_SEED
      ? {
          chain: {
            reader: rpcChainReader(config.NEAR_RPC_URL, config.NEAR_ITEMS_CONTRACT),
            signer: new VoucherSigner(Buffer.from(config.MINT_SIGNER_SEED, 'base64')),
            itemsContract: config.NEAR_ITEMS_CONTRACT,
            // read on every mint, so an admin change applies at once
            limits: {
              get dailyValueCap() {
                return ops.current.mintDailyValueCap;
              },
              get minAccountHours() {
                return ops.current.mintMinAccountHours;
              },
            },
            scheduleExpiry: bullMintExpiry(queues.mintExpiry),
            accountOf: async (uid: string) => (await prisma.account.findUnique({ where: { uid }, select: { accountId: true } }))?.accountId ?? null,
            unclaimedRedeems: unclaimedRedeems(prisma, config.NEAR_ITEMS_CONTRACT),
            ...(config.NEAR_NETWORK === 'testnet' && config.FAUCET_ACCOUNT && config.FAUCET_PRIVATE_KEY
              ? {
                  faucet: {
                    sender: nearFaucetSender(config.NEAR_RPC_URL, config.FAUCET_ACCOUNT, config.FAUCET_PRIVATE_KEY, config.NEAR_FARM_CONTRACT),
                    farmUnits: BigInt(config.FAUCET_FARM) * 10n ** 18n,
                    nearYocto: BigInt(Math.round(config.FAUCET_NEAR * 1000)) * 10n ** 21n,
                    dailyCap: config.FAUCET_DAILY_CAP,
                  },
                }
              : {}),
          },
        }
      : {}),
  };

  return {
    rt,
    prisma,
    sessions,
    reader: prismaReader(prisma),
    bus,
    limiter: redisRateLimiter(redis),
    queues,
    ops,
    async close() {
      ops.stop();
      await queues.close();
      await redis.quit();
      await prisma.$disconnect();
    },
  };
}
