import { CALLABLES } from '@farmverse/contracts';
import { getVisit, likeIsland } from '../game/visits';
import { bookLikes } from '../game/engagement';
import { env } from './env';
import { bookingDeps } from './session';
import { defineCallable } from './runtime';

/** `visits-get`: another player's island to look at. */
export const get = defineCallable({ spec: CALLABLES.visitsGet, auth: 'required', handler: async ({ uid, data, log }) => getVisit(await env(log), uid, data.uid) });

/** `visits-like`: like an island, once a day. */
export const like = defineCallable({
  spec: CALLABLES.visitsLike,
  auth: 'required',
  handler: async ({ uid, data, log }) => {
    const result = await likeIsland(await env(log), uid, data.uid);
    if (result.fresh) {
      // the owner hears about today's likes once, later (never blocks the like)
      try {
        await bookLikes(await bookingDeps(log), data.uid);
      } catch (e) {
        log.warn('likes push not booked', { error: String(e) });
      }
    }
    return result;
  },
});
