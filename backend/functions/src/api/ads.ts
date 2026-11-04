import { onRequest } from 'firebase-functions/v2/https';
import { CALLABLES } from '@farmverse/contracts';
import { systemClock } from '../core/clock';
import { cloudLogger } from '../core/logger';
import { admobKeys, claimAd, recordAdView, verifySsv } from '../game/ads';
import { env } from './env';
import { defineCallable, IN_EMULATOR, REGION } from './runtime';
import { services } from './services';

/** `ads-claim`: a watched rewarded ad's prize (verified by AdMob's callback, except in the emulator). */
export const claim = defineCallable(
  {
    spec: CALLABLES.adsClaim,
    auth: 'required',
    handler: async ({ uid, data, log }) => claimAd(await env(log), uid, data.placement, data.key, !IN_EMULATOR),
  },
  { replayProtected: true },
);

/**
 * `ads-ssv`: AdMob's server-side verification callback (set this function's URL as the rewarded ad
 * unit's SSV callback in the AdMob console). A bad signature gets 400 and is not recorded.
 */
export const ssv = onRequest({ region: REGION, memory: '256MiB', timeoutSeconds: 15 }, async (req, res) => {
  const query = req.originalUrl.includes('?') ? req.originalUrl.slice(req.originalUrl.indexOf('?') + 1) : '';
  // AdMob's console "verify URL" check calls without parameters: answer OK
  if (query === '') {
    res.status(200).send('ok');
    return;
  }
  try {
    const view = verifySsv(query, await admobKeys());
    if (!view) {
      cloudLogger.warn('ads-ssv: bad or unsigned callback', {});
      res.status(400).send('bad signature');
      return;
    }
    await recordAdView({ store: services().store, clock: systemClock, log: cloudLogger }, view);
    res.status(200).send('ok');
  } catch (e) {
    cloudLogger.error('ads-ssv failed', { error: String(e) });
    res.status(500).send('error');
  }
});
