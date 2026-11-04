import { CALLABLES } from '@farmverse/contracts';
import { getFunctions } from 'firebase-admin/functions';
import { onTaskDispatched } from 'firebase-functions/v2/tasks';
import { onDocumentCreated } from 'firebase-functions/v2/firestore';
import { fanOutNews } from '../game/news';
import { cloudLogger, type Logger } from '../core/logger';
import { nudgeTask, ping as pingSession, type EngagementDeps } from '../game/engagement';
import { finishRaid, raidTask, type PushTask, type TaskScheduler } from '../game/raids';
import { env } from './env';
import { islandEngine } from './engine';
import { fcm } from './messenger';
import { adminApp, defineCallable, REGION } from './runtime';

type Job = PushTask & { uid: string };

/** Booked nudges and raid steps go on the `push-task` queue; the id makes a repeat booking harmless. */
const cloudTasks: TaskScheduler = {
  async schedule(uid, task, atMs) {
    const queue = getFunctions(adminApp()).taskQueue<Job>(`locations/${REGION}/functions/push-task`);
    const id = [task.kind, task.stage ?? '', task.day ?? '', uid, Math.floor(atMs / 60_000)].join('-').replace(/[^A-Za-z0-9_-]/g, '_');
    try {
      await queue.enqueue({ uid, ...task }, { scheduleTime: new Date(atMs), id });
    } catch (e) {
      if ((e as { code?: string }).code === 'functions/task-already-exists') return;
      throw e;
    }
  },
};

export async function bookingDeps(log: Logger): Promise<EngagementDeps> {
  return { env: await env(log), engine: islandEngine(), tasks: cloudTasks, messenger: fcm };
}


/** `session-ping`: the game is open (start and every minute in front); returns today's raid. */
export const ping = defineCallable({
  spec: CALLABLES.sessionPing,
  auth: 'required',
  handler: async ({ uid, data, log }) => pingSession(await bookingDeps(log), uid, data.offsetMin, data.raidAck === true),
});

/** `raid-finish`: the game ran (or skipped) the day's raid with the player there. */
export const finish = defineCallable({
  spec: CALLABLES.raidFinish,
  auth: 'required',
  handler: async ({ uid, data, log }) => finishRaid(await bookingDeps(log), uid, data.day, data.outcome),
});

/** `push-task`: a booked raid step or nudge, checked again now that it's due. */
export const task = onTaskDispatched<Job>(
  {
    region: REGION,
    retryConfig: { maxAttempts: 3, minBackoffSeconds: 30 },
    rateLimits: { maxConcurrentDispatches: 50 },
    timeoutSeconds: 60,
    memory: '512MiB',
  },
  async (req) => {
    const job = req.data;
    if (!job || typeof job.uid !== 'string' || job.uid.length === 0) return;
    const log = cloudLogger.with({ fn: 'push-task', uid: job.uid, kind: job.kind, stage: job.stage ?? '' });
    const d = await bookingDeps(log);
    const outcome =
      job.kind === 'raid' ? await raidTask(d, job.uid, job.stage ?? '', job.day ?? '') : await nudgeTask(d, job.uid, job.kind, job.stage);
    log.info('push task', { outcome });
  },
);

/** `push-onNewsCreated`: a news item published with `push: true` books a push for each recent player. */
export const onNewsCreated = onDocumentCreated({ region: REGION, document: 'news/{newsId}', timeoutSeconds: 540, memory: '512MiB' }, async (event) => {
  const { newsId } = event.params;
  const log = cloudLogger.with({ fn: 'push-onNewsCreated', newsId });
  const players = await fanOutNews(await bookingDeps(log), newsId, event.data?.data());
  log.info('news push', { players });
});
