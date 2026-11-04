import { CALLABLES } from '@farmverse/contracts';
import { claimChest, claimGift, useBoost } from '../game/shop';
import { env } from './env';
import { defineCallable } from './runtime';

/** `boost-use`: use one boost from Storage. */
export const use = defineCallable({
  spec: CALLABLES.boostUse,
  auth: 'required',
  handler: async ({ uid, data, log }) => useBoost(await env(log), uid, data.boostId, data.key),
});

/** `shop-claimGift`: the free coins, every few hours. */
export const claimGiftFn = defineCallable({
  spec: CALLABLES.shopClaimGift,
  auth: 'required',
  handler: async ({ uid, data, log }) => claimGift(await env(log), uid, data.key),
});

/** `shop-claimChest`: today's order or task chest. */
export const claimChestFn = defineCallable(
  {
    spec: CALLABLES.shopClaimChest,
    auth: 'required',
    handler: async ({ uid, data, log }) => claimChest(await env(log), uid, data.chest, data.key),
  },
  { replayProtected: true },
);
