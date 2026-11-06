import 'dotenv/config';
import { QueueEvents, Worker, type Job } from 'bullmq';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { systemClock } from '../../../game/src/core/clock';
import { memoryLogger } from '../../../game/src/core/logger';
import { webMessenger, type Runtime } from '../../src/calls';
import { redisClient } from '../../src/infra/redis';
import { bullTasks, bullWakes, jobQueues, QUEUES, type JobQueues } from '../../src/jobs/queues';
import { triggersFor } from '../../src/runtime';
import { prismaStore } from '../../src/store/prismaStore';
import { processors } from '../../src/worker';
import { resetDatabase, testPrisma } from './setup';

const prefix = `fvtest${Date.now()}`;
const url = process.env['REDIS_URL'] ?? 'redis://localhost:6379';
const prisma = testPrisma();
const connection = redisClient(url, 'jobs-test');
let queues: JobQueues;

beforeAll(async () => {
  await resetDatabase(prisma);
  queues = jobQueues(connection, prefix);
});

afterAll(async () => {
  for (const q of [queues.islandWake, queues.pushTask, queues.newsFanout, queues.cron]) await q.obliterate({ force: true });
  await queues.close();
  await connection.quit();
  await prisma.$disconnect();
});

describe('BullMQ jobs (replacing Cloud Tasks, Scheduler and triggers)', () => {
  it('booking the same island wake twice in a minute keeps one delayed job', async () => {
    const now = Date.now();
    const wakes = bullWakes(queues.islandWake, () => now);
    await wakes.schedule('uAAAAAAAAAAAAAAAAAAAA1', now + 120_000);
    await wakes.schedule('uAAAAAAAAAAAAAAAAAAAA1', now + 125_000);
    await wakes.schedule('uAAAAAAAAAAAAAAAAAAAA2', now + 120_000);
    expect(await queues.islandWake.getDelayedCount()).toBe(2);
    const job = (await queues.islandWake.getDelayed())[0]!;
    expect(job.opts.delay).toBeGreaterThanOrEqual(120_000);
  });

  it('push tasks dedupe on kind, stage, day, player and minute', async () => {
    const now = Date.now();
    const tasks = bullTasks(queues.pushTask, () => now);
    await tasks.schedule('uP1', { kind: 'raid', stage: 'warn', day: '2026-11-06' }, now + 60_000);
    await tasks.schedule('uP1', { kind: 'raid', stage: 'warn', day: '2026-11-06' }, now + 60_000);
    await tasks.schedule('uP1', { kind: 'raid', stage: 'start', day: '2026-11-06' }, now + 60_000);
    expect(await queues.pushTask.getDelayedCount()).toBe(2);
  });

  it('a published news item books the fan-out, and a worker runs it', async () => {
    const log = memoryLogger();
    const store = prismaStore(prisma, { onCommit: triggersFor(queues, log) });
    const rt: Runtime = {
      store,
      clock: systemClock,
      log,
      engine: {} as Runtime['engine'],
      wakes: { schedule: async () => {} },
      tasks: { schedule: async () => {} },
      messenger: webMessenger(log),
      deleteAccount: async () => {},
    };
    const events = new QueueEvents(QUEUES.newsFanout, { connection: redisClient(url, 'jobs-test-events'), prefix });
    await events.waitUntilReady();
    const worker = new Worker(QUEUES.newsFanout, processors(rt, log)[QUEUES.newsFanout] as (job: Job) => Promise<unknown>, {
      connection: redisClient(url, 'jobs-test-worker'),
      prefix,
    });

    await store.runTransaction(async (tx) => tx.set('news/launch', { title: 'Farmverse is live', push: false, createdAt: Date.now() }));
    const job = await queues.newsFanout.getJob('news-launch');
    expect(job?.data).toEqual({ newsId: 'launch' });
    await job!.waitUntilFinished(events, 15_000);
    expect(await job!.isCompleted()).toBe(true);

    // Updating the item later doesn't book it again.
    await store.runTransaction(async (tx) => tx.set('news/launch', { title: 'edited' }, { merge: true }));
    expect((await queues.newsFanout.getJobs(['waiting', 'delayed', 'active', 'completed'])).length).toBe(1);

    await worker.close();
    await events.close();
  });
});
