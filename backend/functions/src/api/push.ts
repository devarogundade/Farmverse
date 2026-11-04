import { onDocumentCreated } from 'firebase-functions/v2/firestore';
import { CALLABLES } from '@farmverse/contracts';
import { cloudLogger } from '../core/logger';
import { registerPushToken } from '../game/players';
import { deliverNotification } from '../game/push';
import { fcm } from './messenger';
import { env } from './env';
import { defineCallable, REGION } from './runtime';

/** `push-registerToken`: remember this installation's FCM token. */
export const registerToken = defineCallable({
  spec: CALLABLES.pushRegisterToken,
  auth: 'required',
  handler: async ({ uid, data, log }) => registerPushToken(await env(log), uid, data.installationId, data.token, data.meta.platform),
});

/** `push-onNotificationCreated`: every new inbox row may become a push (03 §6). */
export const onNotificationCreated = onDocumentCreated({ region: REGION, document: 'players/{uid}/notifications/{notificationId}' }, async (event) => {
  const { uid, notificationId } = event.params;
  const log = cloudLogger.with({ fn: 'push-onNotificationCreated', uid, notificationId });
  const outcome = await deliverNotification(await env(log), fcm, uid, notificationId, event.data?.data());
  log.info('push', { outcome });
});
