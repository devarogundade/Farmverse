import { CALLABLES } from '@farmverse/contracts';
import { claimLogin, spinWheel } from '../game/daily';
import { env } from './env';
import { defineCallable, IN_EMULATOR } from './runtime';

/** `daily-claimLogin`: today's login reward. */
export const claimLoginFn = defineCallable(
  { spec: CALLABLES.dailyClaimLogin, auth: 'required', handler: async ({ uid, data, log }) => claimLogin(await env(log), uid, data.key) },
  { replayProtected: true },
);

/** `wheel-spin`: one spin per watched rewarded ad (verified by AdMob's callback, except in the emulator). */
export const spin = defineCallable(
  { spec: CALLABLES.wheelSpin, auth: 'required', handler: async ({ uid, data, log }) => spinWheel(await env(log), uid, data.key, !IN_EMULATOR) },
  { replayProtected: true },
);
