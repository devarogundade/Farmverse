import { CALLABLES } from '@farmverse/contracts';
import { getAuth } from 'firebase-admin/auth';
import { claimCoupon, redeemCoupon } from '../game/coupons';
import { env } from './env';
import { adminApp, defineCallable, IN_EMULATOR } from './runtime';

/**
 * Linked to Google or Apple (coupon types marked `linkedOnly`): the token says so when the player
 * signed in with one; a token from before the link still says "anonymous", so the account is asked then.
 */
async function isLinked(uid: string, provider: string | undefined): Promise<boolean> {
  if (provider !== undefined && provider !== '' && provider !== 'anonymous') return true;
  const user = await getAuth(adminApp()).getUser(uid);
  return user.providerData.some((p) => p.providerId === 'google.com' || p.providerId === 'apple.com');
}

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
