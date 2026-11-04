import type { RaidPlan, SettingsDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { writeMail } from './inbox';
import { DAY_MS, HOUR_MS, localHourAt, MINUTE_MS, minuteOf, nextGoodTime, readPresence, sendDirect, type DirectOutcome, type PresenceDoc } from './notify';
import { payLevelGems } from './levels';
import { newsTask } from './news';
import { ackRaid, currentRaid, toPlan, type RaidDeps } from './raids';
import { utcDay } from './wallet';

/**
 * Keeping players coming back without nagging them (module: engagement). The game pings while it's
 * open; each new session books the few nudges that might matter later, and each nudge checks again
 * when it's due whether it's still worth sending (the player may have come back, claimed, bought…):
 *  - daily_ready: next day at their usual hour, if the login reward is still unclaimed;
 *  - comeback_3 / _7 / _14: if they really have been away that long; 7 and 14 carry a gift;
 *  - starter_ending: three hours before the starter pack goes, if it hasn't been bought;
 *  - likes: one a day, at their usual hour, when visitors liked the island.
 * Raids, storage-full and the island's own timers are booked elsewhere (raids.ts, islands.ts, the game).
 */

/** A gap this long between pings starts a new session. */
export const SESSION_GAP_MS = 10 * MINUTE_MS;
/** Away at least this long, a returning player finds a welcome-back gift in the inbox. */
export const COMEBACK_GIFT_AFTER_MS = 6 * DAY_MS;
export const COMEBACK_GEMS = 20;
/** The comeback ladder: days away. */
export const COMEBACK_DAYS = [3, 7, 14] as const;
/** The starter pack nudge goes this long before it ends. */
export const STARTER_NUDGE_MS = 3 * HOUR_MS;
/** Session-hour history is halved when it reaches this many sessions, so a changed routine shows quickly. */
const HOURS_CAP = 60;

export interface EngagementDeps extends RaidDeps {}

/** Record a ping: when, the offset, and (for a new session) the hour it started. Pure. */
export function notePing(before: PresenceDoc | undefined, nowMs: number, offsetMin: number): { presence: PresenceDoc; newSession: boolean; awayMs: number } {
  const newSession = !before || nowMs - before.seenMs > SESSION_GAP_MS;
  const awayMs = before ? Math.max(0, nowMs - before.seenMs) : 0;
  let hours = before ? [...before.hours] : new Array<number>(24).fill(0);
  if (newSession) {
    hours[localHourAt(nowMs, offsetMin)]! += 1;
    if (hours.reduce((a, b) => a + b, 0) > HOURS_CAP) hours = hours.map((h) => h / 2);
  }
  return {
    presence: { seenMs: nowMs, sessionMs: newSession ? nowMs : before!.sessionMs, offsetMin, hours, updatedAt: nowMs },
    newSession,
    awayMs,
  };
}

async function book(deps: EngagementDeps, uid: string, presence: PresenceDoc, settings: Partial<SettingsDoc> | undefined): Promise<void> {
  const { env, tasks } = deps;
  const nowMs = env.clock.nowMs();
  const minute = minuteOf(uid);
  try {
    // tomorrow at their usual hour (never later today: they're playing now)
    await tasks.schedule(uid, { kind: 'daily' }, nextGoodTime(nowMs + 12 * HOUR_MS, presence, settings, minute));
    for (const days of COMEBACK_DAYS) await tasks.schedule(uid, { kind: 'comeback', stage: String(days) }, nextGoodTime(nowMs + days * DAY_MS - 2 * HOUR_MS, presence, settings, minute));
    const shop = (await env.store.read(paths.shop(uid))).data;
    const starterEnds = Number(shop?.['starterEndsAt'] ?? 0);
    if (starterEnds - STARTER_NUDGE_MS > nowMs) await tasks.schedule(uid, { kind: 'starter' }, starterEnds - STARTER_NUDGE_MS);
  } catch (e) {
    env.log.warn('nudges not booked', { uid, error: String(e) });
  }
}

/** `session-ping`: the game is open. No pushes while it is; returns today's raid. */
export async function ping(
  deps: EngagementDeps,
  uid: string,
  offsetMin: number,
  raidAck: boolean,
): Promise<{ raid: RaidPlan; levelGems: number; levelPaid: number }> {
  const { env } = deps;
  const nowMs = env.clock.nowMs();
  const [presenceSnap, settingsSnap] = await Promise.all([env.store.read(paths.presence(uid)), env.store.read(paths.settings(uid))]);
  const settings = settingsSnap.data as Partial<SettingsDoc> | undefined;
  const { presence, newSession, awayMs } = notePing(readPresence(presenceSnap.data), nowMs, offsetMin);
  await env.store.writeBatch([
    { path: paths.presence(uid), data: presence as unknown as Record<string, unknown> },
    // who has played lately (news goes to them), once a session
    ...(newSession ? [{ path: paths.player(uid), data: { lastSeenAt: nowMs }, merge: true }] : []),
  ]);

  if (newSession) {
    await book(deps, uid, presence, settings);
    if (awayMs >= COMEBACK_GIFT_AFTER_MS) {
      await env.store.runTransaction(async (tx) => {
        const uow = new UnitOfWork(tx);
        writeMail(uow, uid, { title: 'Welcome back!', body: 'Your farmers kept the island safe. A little gift for you.', coins: 0, gems: COMEBACK_GEMS, route: '', expireDays: 14 }, nowMs);
        uow.commit();
      });
    }
  }

  let raid = await currentRaid(deps, uid, presence, settings);
  if (raidAck) raid = await ackRaid(env, uid, raid);
  // gems for island levels reached (the level comes from the island's mirror)
  let paid = { gems: 0, level: 0 };
  try {
    paid = await payLevelGems(env, uid);
  } catch (e) {
    env.log.warn('level gems not paid', { uid, error: String(e) });
  }
  return { raid: toPlan(raid), levelGems: paid.gems, levelPaid: paid.level };
}

export type NudgeOutcome = DirectOutcome | 'not_needed';

/** A booked nudge, checked again now that it's due. */
export async function nudgeTask(deps: EngagementDeps, uid: string, kind: 'daily' | 'comeback' | 'starter' | 'likes' | 'news', stage?: string): Promise<NudgeOutcome> {
  const { env, messenger } = deps;
  const nowMs = env.clock.nowMs();
  const presence = readPresence((await env.store.read(paths.presence(uid))).data);
  if (!presence) return 'not_needed';
  const awayMs = nowMs - presence.seenMs;

  switch (kind) {
    case 'daily': {
      const daily = (await env.store.read(paths.daily(uid))).data;
      if (daily?.['loginOn'] === utcDay(nowMs)) return 'not_needed';
      // a long-gone player hears from the comeback ladder instead
      if (awayMs > 2 * DAY_MS) return 'not_needed';
      return sendDirect(env, messenger, uid, 'daily_ready', { day: String(Number(daily?.['loginDay'] ?? 1)) });
    }
    case 'comeback': {
      const days = Number(stage);
      if (!COMEBACK_DAYS.includes(days as (typeof COMEBACK_DAYS)[number]) || awayMs < days * DAY_MS - 4 * HOUR_MS) return 'not_needed';
      const pushKind = days === 3 ? 'comeback_3' : days === 7 ? 'comeback_7' : 'comeback_14';
      return sendDirect(env, messenger, uid, pushKind, { gems: String(COMEBACK_GEMS) });
    }
    case 'starter': {
      const ends = Number((await env.store.read(paths.shop(uid))).data?.['starterEndsAt'] ?? 0);
      if (ends - nowMs < 30 * MINUTE_MS) return 'not_needed';
      return sendDirect(env, messenger, uid, 'starter_ending', { hours: String(Math.max(1, Math.round((ends - nowMs) / HOUR_MS))) });
    }
    case 'news':
      return newsTask(deps, uid, stage ?? '');
    case 'likes': {
      const visits = (await env.store.read(paths.visits(uid))).data;
      const count = visits?.['likesDay'] === utcDay(nowMs) ? Number(visits?.['likesToday'] ?? 0) : 0;
      if (count <= 0) return 'not_needed';
      return sendDirect(env, messenger, uid, 'likes', { count: String(count) });
    }
  }
}

/** Someone liked the island: one push about it later today, at the owner's usual hour. */
export async function bookLikes(deps: EngagementDeps, ownerUid: string): Promise<void> {
  const nowMs = deps.env.clock.nowMs();
  const [presenceSnap, settingsSnap] = await Promise.all([deps.env.store.read(paths.presence(ownerUid)), deps.env.store.read(paths.settings(ownerUid))]);
  const presence = readPresence(presenceSnap.data);
  const at = nextGoodTime(nowMs + HOUR_MS, presence, settingsSnap.data as Partial<SettingsDoc> | undefined, minuteOf(ownerUid));
  // likes are counted per UTC day: a push after midnight UTC would find none, so not past it
  const endOfDay = Date.parse(utcDay(nowMs) + 'T23:30:00Z');
  await deps.tasks.schedule(ownerUid, { kind: 'likes', day: utcDay(nowMs) }, Math.min(at, Math.max(nowMs + 10 * MINUTE_MS, endOfDay)));
}

/** The island's storage filled while the player was away (an island wake noticed). */
export async function storageFull(deps: Pick<EngagementDeps, 'env' | 'messenger'>, uid: string): Promise<NudgeOutcome> {
  return sendDirect(deps.env, deps.messenger, uid, 'storage_full');
}

/** A wake found the workers stopped for want of food while the player is away (once per absence). */
export async function workersHungry(deps: Pick<EngagementDeps, 'env' | 'messenger'>, uid: string): Promise<NudgeOutcome> {
  return sendDirect(deps.env, deps.messenger, uid, 'workers_hungry');
}
