import { getMessaging } from 'firebase-admin/messaging';
import type { Messenger, PushMessage } from '../game/push';
import { adminApp, db } from './runtime';

const STALE_CODES = new Set(['messaging/registration-token-not-registered', 'messaging/invalid-registration-token']);

/** FCM through the Admin SDK: a visible notification plus routing data, high priority, collapsed per kind. */
export const fcm: Messenger = {
  async send(messages: readonly PushMessage[]) {
    if (messages.length === 0) return [];
    db(); // initialises the default app
    const response = await getMessaging(adminApp()).sendEach(
      messages.map((m) => ({
        token: m.token,
        notification: { title: m.title, body: m.body },
        data: { ...m.data },
        android: { priority: 'high' as const, collapseKey: m.collapseKey, notification: { tag: m.collapseKey } },
        apns: { headers: { 'apns-collapse-id': m.collapseKey } },
      })),
    );
    return response.responses.flatMap((r, i) => (!r.success && r.error && STALE_CODES.has(r.error.code) ? [messages[i]!.token] : []));
  },
};

