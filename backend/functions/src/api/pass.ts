import { CALLABLES } from '@farmverse/contracts';
import { claimPass, joinPass } from '../game/pass';
import { env } from './env';
import { defineCallable } from './runtime';

/** `pass-join`: join this season's Farm Pass (idempotent). */
export const join = defineCallable({ spec: CALLABLES.passJoin, auth: 'required', handler: async ({ uid, log }) => joinPass(await env(log), uid) });

/** `pass-claim`: collect a Farm Pass tier. */
export const claim = defineCallable(
  { spec: CALLABLES.passClaim, auth: 'required', handler: async ({ uid, data, log }) => claimPass(await env(log), uid, data.tier, data.premium, data.key) },
  { replayProtected: true },
);
