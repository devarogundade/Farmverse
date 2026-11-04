import { CALLABLES } from '@farmverse/contracts';
import { claimAchievement } from '../game/goals';
import { env } from './env';
import { defineCallable } from './runtime';

/** `goals-claimAchievement`: an achievement's gems, once, checked on the server's island. */
export const claimAchievementFn = defineCallable(
  {
    spec: CALLABLES.goalsClaimAchievement,
    auth: 'required',
    handler: async ({ uid, data, log }) => claimAchievement(await env(log), uid, data.id, data.key),
  },
  { replayProtected: true },
);
