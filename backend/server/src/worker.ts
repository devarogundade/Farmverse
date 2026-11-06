import 'dotenv/config';
import { Worker, type Job } from 'bullmq';
import { cloudLogger, type Logger } from '../../game/src/core/logger';
import { nudgeTask } from '../../game/src/game/engagement';
import { wake as wakeIsland } from '../../game/src/game/islands';
import { payWeek } from '../../game/src/game/leaderboard';
import { fanOutNews } from '../../game/src/game/news';
import { raidTask } from '../../game/src/game/raids';
import { bookingDeps, envOf, islandDeps, mintDeps, type Runtime } from './calls';
import { backstopRedeems, expireMint } from './mint/mint';
import { loadConfig } from './config';
import { redisClient } from './infra/redis';
import { QUEUES, scheduleCron, type CronJob, type IslandWakeJob, type MintExpiryJob, type NewsFanoutJob, type PushTaskJob } from './jobs/queues';
import { buildServices } from './runtime';

/** What each queue does — the bodies of the old Cloud Tasks handlers, scheduler and trigger. */
export function processors(rt: Runtime, log: Logger) {
  return {
    [QUEUES.islandWake]: async (job: Job<IslandWakeJob>) => {
      const l = log.with({ job: 'island-wake', uid: job.data.uid });
      const outcome = await wakeIsland(islandDeps(rt, l), job.data.uid);
      l.info('island wake', { outcome });
      return outcome;
    },
    [QUEUES.pushTask]: async (job: Job<PushTaskJob>) => {
      const { uid, ...task } = job.data;
      const l = log.with({ job: 'push-task', uid, kind: task.kind, stage: task.stage ?? '' });
      const d = bookingDeps(rt, l);
      const outcome = task.kind === 'raid' ? await raidTask(d, uid, task.stage ?? '', task.day ?? '') : await nudgeTask(d, uid, task.kind, task.stage);
      l.info('push task', { outcome });
      return outcome;
    },
    [QUEUES.newsFanout]: async (job: Job<NewsFanoutJob>) => {
      const l = log.with({ job: 'news-fanout', newsId: job.data.newsId });
      const doc = await rt.store.read(`news/${job.data.newsId}`);
      const players = await fanOutNews(bookingDeps(rt, l), job.data.newsId, doc.data);
      l.info('news fan-out', { players });
      return players;
    },
    [QUEUES.mintExpiry]: async (job: Job<MintExpiryJob>) => {
      const l = log.with({ job: 'mint-expiry', uid: job.data.uid, tokenId: job.data.tokenId });
      const outcome = await expireMint(mintDeps(rt, l), job.data.uid, job.data.tokenId, l);
      // Booked a little early (clock skew): look again shortly.
      if (outcome === 'early') throw new Error('voucher not expired yet');
      l.info('mint expiry', { outcome });
      return outcome;
    },
    [QUEUES.cron]: async (job: Job<CronJob>) => {
      const l = log.with({ job: job.data.name });
      if (job.data.name === 'leaderboard-pay-week') await payWeek(envOf(rt, l));
      if (job.data.name === 'redeem-backstop' && rt.chain?.unclaimedRedeems) await backstopRedeems(envOf(rt, l), rt.chain.unclaimedRedeems, l);
    },
  };
}

async function main(): Promise<void> {
  const config = loadConfig();
  const log = cloudLogger.with({ service: 'farmverse-worker', env: config.FARMVERSE_ENV });
  const services = buildServices(config, log);
  await scheduleCron(services.queues.cron);

  const connection = redisClient(config.REDIS_URL, 'worker');
  const work = processors(services.rt, log);
  const workers = Object.entries(work).map(
    ([name, fn]) =>
      new Worker(name, fn as (job: Job) => Promise<unknown>, {
        connection,
        concurrency: name === QUEUES.cron ? 1 : 20,
      }),
  );
  for (const w of workers) {
    w.on('failed', (job, error) => log.warn('job failed', { queue: w.name, id: job?.id, attempts: job?.attemptsMade, error: String(error) }));
    w.on('error', (error) => log.error('worker error', { queue: w.name, error: String(error) }));
  }
  log.info('worker started', { queues: workers.map((w) => w.name) });

  const shutdown = async () => {
    await Promise.all(workers.map((w) => w.close()));
    await connection.quit();
    await services.close();
    process.exit(0);
  };
  process.on('SIGTERM', () => void shutdown());
  process.on('SIGINT', () => void shutdown());
}

if (process.argv[1] && /worker\.(ts|js)$/.test(process.argv[1])) {
  main().catch((error: unknown) => {
    cloudLogger.error('worker failed to start', { error: String(error), stack: (error as Error).stack });
    process.exit(1);
  });
}
