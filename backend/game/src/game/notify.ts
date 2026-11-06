import { PUSH_ROUTE, PUSH_TOPIC, renderDirect, type PushKind, type SettingsDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import { inQuietHours, type Messenger } from './push';
import type { Env } from './services';

/**
 * Direct pushes (no inbox row) and the one gate every one of them passes. The rules that keep them
 * useful and never annoying:
 *  - only with the player's opt-in, and only topics they left on (Settings → Notifications);
 *  - never while the game is open (it pings every minute) and never in their quiet hours;
 *  - each kind at most once per its own gap (a day for most), offers once every three days;
 *  - at most DAILY_CAP a local day; raid warnings ride on the day's raid and don't use it up.
 */

export const MINUTE_MS = 60_000;
export const HOUR_MS = 60 * MINUTE_MS;
export const DAY_MS = 24 * HOUR_MS;

/** The game is open if it pinged this recently (it pings every minute while in front). */
export const ONLINE_MS = 150_000;
/** Direct pushes a local day, raid warnings aside. */
export const DAILY_CAP = 3;
/** The shortest time between two pushes of the same kind. */
const KIND_GAP_MS: Readonly<Record<PushKind, number>> = {
  raid_soon: 4 * MINUTE_MS,
  raid_won: 20 * HOUR_MS,
  raid_lost: 20 * HOUR_MS,
  storage_full: 20 * HOUR_MS,
  workers_hungry: 20 * HOUR_MS,
  daily_ready: 20 * HOUR_MS,
  comeback_3: 2 * DAY_MS,
  comeback_7: 3 * DAY_MS,
  comeback_14: 7 * DAY_MS,
  starter_ending: 3 * DAY_MS,
  likes: 20 * HOUR_MS,
  news: 20 * HOUR_MS,
};
/** Offers (the shop) at most this often, whatever the kind. */
const OFFER_GAP_MS = 3 * DAY_MS;
/** Pushes that don't use up the daily cap. */
const UNCAPPED: ReadonlySet<PushKind> = new Set(['raid_soon']);

/** `state/presence` (server only). */
export interface PresenceDoc {
  /** Last ping. */
  seenMs: number;
  /** When the current (or last) session started. */
  sessionMs: number;
  /** The device's UTC offset in minutes at its last ping. */
  offsetMin: number;
  /** Sessions started in each local hour (0–23), halved now and then so habits can change. */
  hours: number[];
  updatedAt: number;
}

/** `state/pushLog` (server only). */
export interface PushLogDoc {
  /** The local day `count` is for. */
  day: string;
  count: number;
  /** PushKind → when it last went. */
  last: Record<string, number>;
  /** When an offer last went. */
  offerMs: number;
}

export type DirectOutcome = 'sent' | 'disabled' | 'topic_off' | 'online' | 'quiet' | 'cooldown' | 'capped' | 'no_devices';

export function readPresence(data: Record<string, unknown> | undefined): PresenceDoc | undefined {
  if (!data) return undefined;
  const hours = Array.isArray(data['hours']) ? (data['hours'] as unknown[]).map((h) => Math.max(0, Number(h) || 0)) : [];
  while (hours.length < 24) hours.push(0);
  return {
    seenMs: Number(data['seenMs'] ?? 0),
    sessionMs: Number(data['sessionMs'] ?? 0),
    offsetMin: Math.max(-840, Math.min(840, Number(data['offsetMin'] ?? 0))),
    hours: hours.slice(0, 24),
    updatedAt: Number(data['updatedAt'] ?? 0),
  };
}

export const isOnline = (presence: PresenceDoc | undefined, nowMs: number) => !!presence && nowMs - presence.seenMs < ONLINE_MS;

/** The local calendar day (YYYY-MM-DD) at a UTC offset. */
export function localDay(nowMs: number, offsetMin: number): string {
  return new Date(nowMs + offsetMin * MINUTE_MS).toISOString().slice(0, 10);
}

/** The local hour (0–23) at a UTC offset. */
export function localHourAt(nowMs: number, offsetMin: number): number {
  return new Date(nowMs + offsetMin * MINUTE_MS).getUTCHours();
}

/** The epoch ms of a local wall-clock time on a local day. */
export function atLocal(day: string, hour: number, minute: number, offsetMin: number): number {
  const [y, m, d] = day.split('-').map(Number) as [number, number, number];
  return Date.UTC(y, m - 1, d, hour, minute) - offsetMin * MINUTE_MS;
}

/** The day after a YYYY-MM-DD. */
export function nextDay(day: string): string {
  return new Date(atLocal(day, 12, 0, 0) + DAY_MS).toISOString().slice(0, 10);
}

/** Days since 1970 for a YYYY-MM-DD (a seed that differs every day). */
export function dayNumber(day: string): number {
  return Math.floor(atLocal(day, 0, 0, 0) / DAY_MS);
}

/** Earliest and latest hours a nudge may be planned for (the player's quiet hours still apply). */
export const DAY_START_HOUR = 9;
export const DAY_END_HOUR = 21;
/** When nothing is known about the player yet: early evening, when most people play. */
export const DEFAULT_HOUR = 19;

/**
 * The hour the player usually opens the game (the busiest one in their history), kept inside the
 * day window and out of their quiet hours.
 */
export function preferredHour(presence: PresenceDoc | undefined, settings: Partial<SettingsDoc> | undefined): number {
  const quiet = (h: number) => inQuietHours(h, settings?.quietStart ?? 22, settings?.quietEnd ?? 8);
  let best = -1;
  let score = 0;
  for (let h = DAY_START_HOUR; h <= DAY_END_HOUR; h++) {
    if (quiet(h)) continue;
    const n = presence?.hours[h] ?? 0;
    if (n > score) {
      best = h;
      score = n;
    }
  }
  if (best >= 0) return best;
  if (!quiet(DEFAULT_HOUR)) return DEFAULT_HOUR;
  for (let h = DAY_START_HOUR; h <= DAY_END_HOUR; h++) if (!quiet(h)) return h;
  return DEFAULT_HOUR;
}

/**
 * The next good moment for a nudge at or after `fromMs`: the player's usual hour on that local day,
 * or the next day if it has passed. `minute` spreads players over the hour.
 */
export function nextGoodTime(fromMs: number, presence: PresenceDoc | undefined, settings: Partial<SettingsDoc> | undefined, minute: number): number {
  const offset = presence?.offsetMin ?? 0;
  const hour = preferredHour(presence, settings);
  const day = localDay(fromMs, offset);
  const today = atLocal(day, hour, minute, offset);
  return today >= fromMs ? today : atLocal(nextDay(day), hour, minute, offset);
}

/** A stable minute (0–49) for a player, so pushes to many players don't all leave at :00. */
export function minuteOf(uid: string): number {
  let h = 0;
  for (let i = 0; i < uid.length; i++) h = (h * 31 + uid.charCodeAt(i)) | 0;
  return Math.abs(h) % 50;
}

function readLog(data: Record<string, unknown> | undefined): PushLogDoc {
  return {
    day: String(data?.['day'] ?? ''),
    count: Number(data?.['count'] ?? 0),
    last: { ...((data?.['last'] as Record<string, number> | undefined) ?? {}) },
    offerMs: Number(data?.['offerMs'] ?? 0),
  };
}

/** Would this push pass the gate right now? (Pure; the reason when it wouldn't.) */
export function gate(
  kind: PushKind,
  nowMs: number,
  settings: Partial<SettingsDoc> | undefined,
  presence: PresenceDoc | undefined,
  log: PushLogDoc,
): DirectOutcome | undefined {
  if (settings?.pushEnabled !== true) return 'disabled';
  const topic = PUSH_TOPIC[kind];
  if (settings.pushTopics?.[topic] === false) return 'topic_off';
  if (isOnline(presence, nowMs)) return 'online';
  const hour = presence ? localHourAt(nowMs, presence.offsetMin) : new Date(nowMs).getUTCHours();
  if (inQuietHours(hour, settings.quietStart ?? 22, settings.quietEnd ?? 8)) return 'quiet';
  if (nowMs - (log.last[kind] ?? 0) < KIND_GAP_MS[kind]) return 'cooldown';
  if (topic === 'offers' && nowMs - log.offerMs < OFFER_GAP_MS) return 'cooldown';
  const today = localDay(nowMs, presence?.offsetMin ?? 0);
  if (!UNCAPPED.has(kind) && log.day === today && log.count >= DAILY_CAP) return 'capped';
  return undefined;
}

const MAX_DEVICES = 10;

/** Send one direct push to every phone of the player, if the gate lets it through. */
export async function sendDirect(
  env: Env,
  messenger: Messenger,
  uid: string,
  kind: PushKind,
  params: Readonly<Record<string, string>> = {},
): Promise<DirectOutcome> {
  const nowMs = env.clock.nowMs();
  const [settingsSnap, presenceSnap, logSnap] = await Promise.all([
    env.store.read(paths.settings(uid)),
    env.store.read(paths.presence(uid)),
    env.store.read(paths.pushLog(uid)),
  ]);
  const settings = settingsSnap.data as Partial<SettingsDoc> | undefined;
  const presence = readPresence(presenceSnap.data);
  const log = readLog(logSnap.data);
  const refused = gate(kind, nowMs, settings, presence, log);
  if (refused) {
    env.log.info('push held', { uid, kind, outcome: refused });
    return refused;
  }
  const devices = await env.store.query({ collection: paths.pushTokens(uid), where: [], orderBy: ['updatedAt', 'desc'], limit: MAX_DEVICES });
  const tokens = devices.map((d) => d.data['token']).filter((t): t is string => typeof t === 'string' && t.length > 0);
  if (tokens.length === 0) return 'no_devices';

  const { title, body } = renderDirect(settings?.locale ?? 'en', kind, params);
  const route = PUSH_ROUTE[kind];
  // raid pushes replace each other on the lock screen; the rest by kind
  const collapseKey = PUSH_TOPIC[kind] === 'raids' ? 'raid' : kind;
  const stale = new Set(await messenger.send(tokens.map((token) => ({ token, title, body, data: { route, kind }, collapseKey }))));
  if (stale.size > 0) {
    await env.store.writeBatch(
      devices.filter((d) => stale.has(d.data['token'] as string)).map((d) => ({ path: d.path, data: { token: '', invalidatedAt: nowMs }, merge: true })),
    );
  }

  const today = localDay(nowMs, presence?.offsetMin ?? 0);
  const counted = UNCAPPED.has(kind) ? 0 : 1;
  const next: PushLogDoc = {
    day: today,
    count: (log.day === today ? log.count : 0) + counted,
    last: { ...log.last, [kind]: nowMs },
    offerMs: PUSH_TOPIC[kind] === 'offers' ? nowMs : log.offerMs,
  };
  await env.store.writeBatch([{ path: paths.pushLog(uid), data: next as unknown as Record<string, unknown> }]);
  env.log.info('push sent', { uid, kind });
  return 'sent';
}
