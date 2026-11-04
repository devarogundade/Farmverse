import { NotificationDoc, renderPush, type SettingsDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import type { Env } from './services';

/** One message to one device. */
export interface PushMessage {
  readonly token: string;
  readonly title: string;
  readonly body: string;
  /** Delivered to the app on tap: the route to open and its args (03 §6). */
  readonly data: Readonly<Record<string, string>>;
  /** Same-kind pushes replace each other on the lock screen. */
  readonly collapseKey: string;
}

/** The FCM port, so delivery rules are tested without the network. */
export interface Messenger {
  /** Send each message; returns the tokens FCM reported as no longer registered. */
  send(messages: readonly PushMessage[]): Promise<readonly string[]>;
}

export type PushOutcome = 'sent' | 'disabled' | 'quiet' | 'no_devices' | 'invalid';

const MAX_DEVICES = 10;

/** The hour (0–23) in a time zone; falls back to UTC for unknown zones. */
export function localHour(nowMs: number, timezone: string): number {
  try {
    const hour = new Intl.DateTimeFormat('en-GB', { hour: '2-digit', hourCycle: 'h23', timeZone: timezone || 'UTC' }).format(new Date(nowMs));
    return Number.parseInt(hour, 10) % 24;
  } catch {
    return new Date(nowMs).getUTCHours();
  }
}

/** Quiet hours wrap midnight when start > end (22 → 7). Equal start and end means no quiet hours. */
export function inQuietHours(hour: number, start: number, end: number): boolean {
  if (start === end) return false;
  return start < end ? hour >= start && hour < end : hour >= start || hour < end;
}

/**
 * `push-onNotificationCreated` (03 §6): turn a new inbox row into a push on
 * every registered device, honouring the player's opt-in and quiet hours, and
 * forget tokens FCM no longer knows. The inbox row itself is the record; a
 * skipped push loses nothing.
 */
export async function deliverNotification(env: Env, messenger: Messenger, uid: string, notificationId: string, raw: unknown): Promise<PushOutcome> {
  const parsed = NotificationDoc.safeParse(raw);
  if (!parsed.success) {
    env.log.warn('notification does not match its schema; no push', { uid, notificationId });
    return 'invalid';
  }
  const notification = parsed.data;
  const settings = (await env.store.read(paths.settings(uid))).data as Partial<SettingsDoc> | undefined;
  if (settings?.pushEnabled !== true) return 'disabled';
  if (inQuietHours(localHour(env.clock.nowMs(), settings.timezone ?? 'UTC'), settings.quietStart ?? 0, settings.quietEnd ?? 0)) return 'quiet';

  const devices = await env.store.query({ collection: paths.pushTokens(uid), where: [], orderBy: ['updatedAt', 'desc'], limit: MAX_DEVICES });
  if (devices.length === 0) return 'no_devices';

  const { title, body } = renderPush(settings.locale ?? 'en', notification.kind, notification.params);
  const data = { route: notification.route, notificationId, ...prefixed(notification.routeArgs) };
  const messages = devices
    .map((d) => d.data['token'])
    .filter((t): t is string => typeof t === 'string' && t.length > 0)
    .map((token) => ({ token, title, body, data, collapseKey: notification.kind }));
  const stale = new Set(await messenger.send(messages));
  const forget = devices.filter((d) => stale.has(d.data['token'] as string));
  if (forget.length > 0) {
    await env.store.writeBatch(forget.map((d) => ({ path: d.path, data: { token: '', invalidatedAt: env.clock.nowMs() }, merge: true })));
    env.log.info('forgot stale push tokens', { uid, count: forget.length });
  }
  return 'sent';
}

/** Route args travel as `arg_<name>` so they cannot collide with the reserved keys. */
function prefixed(args: Readonly<Record<string, string>>): Record<string, string> {
  return Object.fromEntries(Object.entries(args).map(([k, v]) => [`arg_${k}`, v]));
}
