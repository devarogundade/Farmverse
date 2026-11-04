import { CALLABLES } from '@farmverse/contracts';
import { claimReferral, claimReward } from '../game/referrals';
import { env } from './env';
import { defineCallable } from './runtime';

/** `referral-claim`: the invited player reached the referral level; both players are rewarded. */
export const claim = defineCallable({
  spec: CALLABLES.referralClaim,
  auth: 'required',
  handler: async ({ uid, log }) => claimReferral(await env(log), uid),
});

/** `rewards-claim`: collect a reward once. */
export const claimRewardFn = defineCallable({
  spec: CALLABLES.rewardsClaim,
  auth: 'required',
  handler: async ({ uid, data, log }) => claimReward(await env(log), uid, data.id),
});
