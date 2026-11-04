import { CALLABLES } from '@farmverse/contracts';
import { getFunctions } from 'firebase-admin/functions';
import { onTaskDispatched } from 'firebase-functions/v2/tasks';
import { cloudLogger } from '../core/logger';
import { sync as syncIsland, wake as wakeIsland, type IslandDeps, type WakeScheduler } from '../game/islands';
import { storageFull, workersHungry } from '../game/engagement';
import { islandEngine } from './engine';
import { fcm } from './messenger';
import { env } from './env';
import { adminApp, defineCallable, REGION } from './runtime';
import type { Logger } from '../core/logger';


/** Wakes go on the `island-wake` task queue, one task per island and time (the id dedupes). */
const cloudTasks: WakeScheduler = {
  async schedule(uid, atMs) {
    const queue = getFunctions(adminApp()).taskQueue<{ uid: string }>(`locations/${REGION}/functions/island-wake`);
    try {
      await queue.enqueue({ uid }, { scheduleTime: new Date(atMs), id: `island-${uid}-${Math.floor(atMs / 60_000)}` });
    } catch (e) {
      if ((e as { code?: string }).code === 'functions/task-already-exists') return;
      throw e;
    }
  },
};

async function deps(log: Logger): Promise<IslandDeps> {
  const e = await env(log);
  return { env: e, engine: islandEngine(), wakes: cloudTasks, onStorageFull: (uid) => storageFull({ env: e, messenger: fcm }, uid), onWorkersHungry: (uid) => workersHungry({ env: e, messenger: fcm }, uid) };
}

/** `island-sync`: the island as of now, with what the workers did meanwhile. */
export const sync = defineCallable(
  {
    spec: CALLABLES.islandSync,
    auth: 'required',
    handler: async ({ uid, data, log }) => syncIsland(await deps(log), uid, data.adopt, data.mirror === true, data.device),
  },
  { memory: '512MiB' },
);

/** `island-wake`: a booked look at an away player's island. */
export const wake = onTaskDispatched<{ uid: string }>(
  {
    region: REGION,
    retryConfig: { maxAttempts: 3, minBackoffSeconds: 30 },
    rateLimits: { maxConcurrentDispatches: 50 },
    timeoutSeconds: 60,
  },
  async (req) => {
    const uid = req.data?.uid;
    if (typeof uid !== 'string' || uid.length === 0) return;
    const log = cloudLogger.with({ fn: 'island-wake', uid });
    const outcome = await wakeIsland(await deps(log), uid);
    log.info('island wake', { outcome });
  },
);
