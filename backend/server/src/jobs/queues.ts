import { Queue, type ConnectionOptions } from 'bullmq';
import type { WakeScheduler } from '../../../game/src/game/islands';
import type { PushTask, TaskScheduler } from '../../../game/src/game/raids';

/** The queues that replace Cloud Tasks, Cloud Scheduler and Firestore triggers. */
export const QUEUES = {
  /** A booked look at an away player's island (was Cloud Tasks `island-wake`). */
  islandWake: 'island-wake',
  /** A booked raid step or nudge (was Cloud Tasks `push-task`). */
  pushTask: 'push-task',
  /** A news item was published (was the `news/{id}` onCreate trigger). */
  newsFanout: 'news-fanout',
  /** Weekly and other clock jobs (was Cloud Scheduler). */
  cron: 'cron',
  /** After a mint voucher expires: confirm it was minted, or give the items back. */
  mintExpiry: 'mint-expiry',
} as const;

export type IslandWakeJob = { uid: string };
export type PushTaskJob = PushTask & { uid: string };
export type NewsFanoutJob = { newsId: string };
export type CronJob = { name: 'leaderboard-pay-week' | 'redeem-backstop' | 'chain-reconcile' | 'merchant-tick' };
export type MintExpiryJob = { uid: string; tokenId: string };

const RETRY = { attempts: 3, backoff: { type: 'exponential', delay: 30_000 } } as const;
/** Finished jobs are kept briefly for the admin board, then dropped. */
const CLEANUP = { removeOnComplete: { age: 24 * 3600, count: 1000 }, removeOnFail: { age: 7 * 24 * 3600 } } as const;

export interface JobQueues {
  readonly islandWake: Queue<IslandWakeJob>;
  readonly pushTask: Queue<PushTaskJob>;
  readonly newsFanout: Queue<NewsFanoutJob>;
  readonly cron: Queue<CronJob>;
  readonly mintExpiry: Queue<MintExpiryJob>;
  close(): Promise<void>;
}

/** `prefix` namespaces the Redis keys (tests use their own; production uses BullMQ's default). */
export function jobQueues(connection: ConnectionOptions, prefix?: string): JobQueues {
  const opts = { connection, defaultJobOptions: { ...RETRY, ...CLEANUP }, ...(prefix ? { prefix } : {}) };
  const q = {
    islandWake: new Queue<IslandWakeJob>(QUEUES.islandWake, opts),
    pushTask: new Queue<PushTaskJob>(QUEUES.pushTask, opts),
    newsFanout: new Queue<NewsFanoutJob>(QUEUES.newsFanout, opts),
    cron: new Queue<CronJob>(QUEUES.cron, opts),
    // the chain may be slow to answer: keep trying for a while
    mintExpiry: new Queue<MintExpiryJob>(QUEUES.mintExpiry, { ...opts, defaultJobOptions: { ...CLEANUP, attempts: 8, backoff: { type: 'exponential', delay: 60_000 } } }),
  };
  return { ...q, close: async () => void (await Promise.all(Object.values(q).map((x) => x.close()))) };
}

/** BullMQ job ids may not contain ':'; keep them to a safe alphabet. */
const safeId = (parts: readonly (string | number)[]) => parts.join('-').replace(/[^A-Za-z0-9_-]/g, '_');

/**
 * Island wakes: one job per island and minute. Re-booking the same minute is a no-op (BullMQ
 * ignores a duplicate job id), exactly like the Cloud Tasks id did.
 */
export function bullWakes(queue: Queue<IslandWakeJob>, now: () => number = Date.now): WakeScheduler {
  return {
    async schedule(uid, atMs) {
      await queue.add('wake', { uid }, { jobId: safeId(['island', uid, Math.floor(atMs / 60_000)]), delay: Math.max(0, atMs - now()) });
    },
  };
}

export function bullTasks(queue: Queue<PushTaskJob>, now: () => number = Date.now): TaskScheduler {
  return {
    async schedule(uid, task, atMs) {
      const id = safeId([task.kind, task.stage ?? '', task.day ?? '', uid, Math.floor(atMs / 60_000)]);
      await queue.add(task.kind, { uid, ...task }, { jobId: id, delay: Math.max(0, atMs - now()) });
    },
  };
}

/** Registers the clock jobs (idempotent: safe on every boot). */
export async function scheduleCron(queue: Queue<CronJob>): Promise<void> {
  // Last week's leaderboard prizes, every Monday just after the week closes (UTC).
  await queue.upsertJobScheduler('leaderboard-pay-week', { pattern: '10 0 * * 1', tz: 'Etc/UTC' }, {
    name: 'leaderboard-pay-week',
    data: { name: 'leaderboard-pay-week' },
  });
  // Redeems nobody reported (tab closed before chain-redeem): credited from the indexer's events.
  await queue.upsertJobScheduler('redeem-backstop', { every: 60_000 }, {
    name: 'redeem-backstop',
    data: { name: 'redeem-backstop' },
  });
  // The game's mints and redeems against the chain, nightly (UTC).
  await queue.upsertJobScheduler('chain-reconcile', { pattern: '30 3 * * *', tz: 'Etc/UTC' }, {
    name: 'chain-reconcile',
    data: { name: 'chain-reconcile' },
  });
  // Market Town merchants look at their stock and the market (plan 15 §13.1)
  await queue.upsertJobScheduler('merchant-tick', { every: 10 * 60_000 }, {
    name: 'merchant-tick',
    data: { name: 'merchant-tick' },
  });
}

/** One refund check per voucher, after it expired (a repeat booking is a no-op). */
export function bullMintExpiry(queue: Queue<MintExpiryJob>, now: () => number = Date.now) {
  return async (uid: string, tokenId: string, atMs: number): Promise<void> => {
    await queue.add('expire', { uid, tokenId }, { jobId: safeId(['mint', tokenId]), delay: Math.max(0, atMs - now()) });
  };
}
