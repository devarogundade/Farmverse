import { CALLABLES } from '@farmverse/contracts';
import { getAuth } from 'firebase-admin/auth';
import { claimLinkReward, verifyPurchase } from '../game/purchases';
import { liveStores } from '../infra/stores';
import { env } from './env';
import { adminApp, defineCallable, IN_EMULATOR } from './runtime';

/**
 * Linked to Google or Apple: the token says so when the player signed in with one; a token from before
 * the link still says "anonymous", so the account itself is asked then.
 */
export async function isLinked(uid: string, provider: string | undefined): Promise<boolean> {
  if (provider !== undefined && provider !== '' && provider !== 'anonymous') return true;
  const user = await getAuth(adminApp()).getUser(uid);
  return user.providerData.some((p) => p.providerId === 'google.com' || p.providerId === 'apple.com');
}

/** `player-claimLinkReward`: the thank-you for Save Farm (a Google or Apple link), once. */
export const claimLink = defineCallable(
  {
    spec: CALLABLES.playerClaimLinkReward,
    auth: 'required',
    handler: async ({ uid, log, signInProvider }) => claimLinkReward(await env(log), uid, IN_EMULATOR || (await isLinked(uid, signInProvider))),
  },
  { replayProtected: true },
);

/** `iap-verify`: a store purchase checked with Google Play or the App Store, then paid once. Fake purchases only in the emulator. */
export const verify = defineCallable(
  {
    spec: CALLABLES.iapVerify,
    auth: 'required',
    handler: async ({ uid, data, log }) =>
      verifyPurchase(await env(log), uid, { store: data.store, productId: data.productId, transactionId: data.transactionId, receipt: data.receipt }, liveStores, IN_EMULATOR),
  },
  { timeoutSeconds: 30 },
);
