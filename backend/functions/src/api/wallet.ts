import { CALLABLES } from '@farmverse/contracts';
import { earnGems, spendGems } from '../game/wallet';
import { env } from './env';
import { defineCallable } from './runtime';

/** `wallet-spend`: buy a catalogue offer with gems. */
export const spend = defineCallable(
  {
    spec: CALLABLES.walletSpend,
    auth: 'required',
    handler: async ({ uid, data, log }) => spendGems(await env(log), uid, data.offerId, data.key),
  },
  { replayProtected: true },
);

/** `wallet-earn`: gems earned in the game, capped per source per day. */
export const earn = defineCallable({
  spec: CALLABLES.walletEarn,
  auth: 'required',
  handler: async ({ uid, data, log }) => earnGems(await env(log), uid, data.source, data.amount, data.key),
});
