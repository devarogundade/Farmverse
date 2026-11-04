import { CALLABLES } from '@farmverse/contracts';
import { claimCoupon, redeemCoupon } from '../game/coupons';
import { env } from './env';
import { defineCallable, IN_EMULATOR } from './runtime';
import { isLinked } from './purchases';

/** `coupons-claim`: a coupon code for a finished task. Linked accounts only where the type says so (not in the emulator). */
export const claim = defineCallable(
  {
    spec: CALLABLES.couponsClaim,
    auth: 'required',
    handler: async ({ uid, data, log, signInProvider }) =>
      claimCoupon(await env(log), uid, data.taskId, IN_EMULATOR || (await isLinked(uid, signInProvider))),
  },
  { replayProtected: true },
);

/** `coupons-redeem`: the redeem page checks a code and marks it used (admin accounts only). */
export const redeem = defineCallable({
  spec: CALLABLES.couponsRedeem,
  auth: 'admin',
  handler: async ({ data, log }) => redeemCoupon(await env(log), data.code),
});
