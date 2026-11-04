import type { RaidPlan, RaidResult, SettingsDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import type { IslandEngine, IslandSave } from '../infra/islandEngine';
import type { IslandDoc } from './islands';
import {
  atLocal,
  DAY_MS,
  dayNumber,
  isOnline,
  localDay,
  MINUTE_MS,
  minuteOf,
  nextDay,
  preferredHour,
  readPresence,
  sendDirect,
  type PresenceDoc,
} from './notify';
import type { Messenger } from './push';
import type { Env } from './services';

/**
 * One raid a day (module: raids). The game runs it while the player is there, which is how it's meant
 * to be played: they defend. The server plans a time for each local day (the hour the player usually
 * plays); if they haven't been in by then it warns them 60, 30 and 5 minutes before, and if they still
 * aren't there at that time it runs the raid itself (the island engine) and keeps the losses for the
 * game to apply and show on the next session.
 */

/** A job the server books for later (Cloud Tasks in production; the id makes a repeat booking harmless). */
export interface PushTask {
  readonly kind: 'raid' | 'daily' | 'comeback' | 'starter' | 'likes' | 'news';
  readonly stage?: string;
  readonly day?: string;
}

export interface TaskScheduler {
  schedule(uid: string, task: PushTask, atMs: number): Promise<void>;
}

export interface RaidDeps {
  readonly env: Env;
  readonly engine: IslandEngine;
  readonly tasks: TaskScheduler;
  readonly messenger: Messenger;
}

/** `state/raid` (server only). */
export interface RaidDoc {
  day: string;
  dueMs: number;
  done: boolean;
  by: RaidPlan['by'];
  /** A raid the server ran that the game hasn't applied yet (kept across days until it has). */
  result: RaidResult | null;
  /** Tomorrow's raid, booked once today's was settled. */
  next?: { day: string; dueMs: number };
  updatedAt: number;
}

/** Warnings before the raid, in minutes. */
export const WARN_MINUTES = [60, 30, 5] as const;
/** The soonest a newly planned raid comes, so the first warning still has time to go. */
export const MIN_LEAD_MS = 65 * MINUTE_MS;
/** A player who stayed away longer than this gets no more raids (and no more raid pushes) until they're back. */
export const RAID_HORIZON_MS = 3 * DAY_MS;
/** If the game is open at the raid's time, look again this much later (the game should have run it by then). */
export const RECHECK_MS = 10 * MINUTE_MS;
/** Raids start at this island level (GameplayRules.RaidStartLevel). */
export const RAID_START_LEVEL = 5;
/** Buildings an island needs before raiders bother with it (GameplayRules.RaidMinBuildings). */
export const RAID_MIN_BUILDINGS = 4;

export const toPlan = (d: RaidDoc): RaidPlan => ({ day: d.day, dueMs: d.dueMs, done: d.done, by: d.by, result: d.result });

function readRaid(data: Record<string, unknown> | undefined): RaidDoc | undefined {
  if (!data || typeof data['day'] !== 'string') return undefined;
  return {
    day: data['day'],
    dueMs: Number(data['dueMs'] ?? 0),
    done: data['done'] === true,
    by: (data['by'] as RaidPlan['by']) ?? 'none',
    result: (data['result'] as RaidResult | null | undefined) ?? null,
    ...(data['next'] ? { next: data['next'] as { day: string; dueMs: number } } : {}),
    updatedAt: Number(data['updatedAt'] ?? 0),
  };
}

/** When a day's raid comes: the player's usual hour that day, but never sooner than the first warning needs. */
export function dueFor(day: string, uid: string, presence: PresenceDoc | undefined, settings: Partial<SettingsDoc> | undefined, nowMs: number): number {
  const at = atLocal(day, preferredHour(presence, settings), minuteOf(uid), presence?.offsetMin ?? 0);
  return Math.max(at, nowMs + MIN_LEAD_MS);
}

async function book(deps: RaidDeps, uid: string, day: string, dueMs: number): Promise<void> {
  const nowMs = deps.env.clock.nowMs();
  try {
    for (const m of WARN_MINUTES) {
      const at = dueMs - m * MINUTE_MS;
      if (at > nowMs) await deps.tasks.schedule(uid, { kind: 'raid', stage: `w${m}`, day }, at);
    }
    await deps.tasks.schedule(uid, { kind: 'raid', stage: 'due', day }, dueMs);
  } catch (e) {
    deps.env.log.warn('raid not booked', { uid, day, error: String(e) });
  }
}

async function context(env: Env, uid: string) {
  const [presenceSnap, settingsSnap] = await Promise.all([env.store.read(paths.presence(uid)), env.store.read(paths.settings(uid))]);
  return { presence: readPresence(presenceSnap.data), settings: settingsSnap.data as Partial<SettingsDoc> | undefined };
}

/** Today's raid for the player (planned and booked if it's a new day). Writes when it changed. */
export async function currentRaid(deps: RaidDeps, uid: string, presence: PresenceDoc | undefined, settings: Partial<SettingsDoc> | undefined): Promise<RaidDoc> {
  const { env } = deps;
  const nowMs = env.clock.nowMs();
  const before = readRaid((await env.store.read(paths.raid(uid))).data);
  const today = localDay(nowMs, presence?.offsetMin ?? 0);
  if (before?.day === today) return before;
  const booked = before?.next?.day === today ? before.next : undefined;
  const doc: RaidDoc = {
    day: today,
    dueMs: booked?.dueMs ?? dueFor(today, uid, presence, settings, nowMs),
    done: false,
    by: 'none',
    result: before?.result ?? null,
    updatedAt: nowMs,
  };
  if (!booked) await book(deps, uid, today, doc.dueMs);
  await env.store.writeBatch([{ path: paths.raid(uid), data: doc as unknown as Record<string, unknown> }]);
  return doc;
}

/** Today is settled: book tomorrow's raid, unless the player has drifted away. */
async function planTomorrow(deps: RaidDeps, uid: string, doc: RaidDoc, presence: PresenceDoc | undefined, settings: Partial<SettingsDoc> | undefined): Promise<void> {
  const nowMs = deps.env.clock.nowMs();
  if (!presence || nowMs - presence.seenMs > RAID_HORIZON_MS) return;
  const day = nextDay(doc.day);
  if (doc.next?.day === day) return;
  const dueMs = dueFor(day, uid, presence, settings, nowMs);
  doc.next = { day, dueMs };
  await book(deps, uid, day, dueMs);
}

const OUTCOME_BY: Readonly<Record<string, RaidPlan['by']>> = { won: 'client', lost: 'client', shield: 'shield', skipped: 'skipped' };

/** `raid-finish`: the game ran (or skipped) the day's raid with the player there. */
export async function finishRaid(deps: RaidDeps, uid: string, day: string, outcome: 'won' | 'lost' | 'shield' | 'skipped'): Promise<{ raid: RaidPlan }> {
  const { presence, settings } = await context(deps.env, uid);
  const doc = await currentRaid(deps, uid, presence, settings);
  if (doc.day === day && !doc.done) {
    doc.done = true;
    doc.by = OUTCOME_BY[outcome] ?? 'client';
    doc.updatedAt = deps.env.clock.nowMs();
    await planTomorrow(deps, uid, doc, presence, settings);
    await deps.env.store.writeBatch([{ path: paths.raid(uid), data: doc as unknown as Record<string, unknown> }]);
  }
  return { raid: toPlan(doc) };
}

/** The game applied and showed the server's raid: forget it. */
export async function ackRaid(env: Env, uid: string, doc: RaidDoc): Promise<RaidDoc> {
  if (!doc.result) return doc;
  const next = { ...doc, result: null, updatedAt: env.clock.nowMs() };
  await env.store.writeBatch([{ path: paths.raid(uid), data: { result: null, updatedAt: next.updatedAt }, merge: true }]);
  return next;
}

export type RaidTaskOutcome = 'stale' | 'done' | 'warned' | 'held' | 'early' | 'online' | 'shield' | 'skipped' | 'raided';

/** A booked raid task: a warning (`w60`, `w30`, `w5`) or the raid itself (`due`). */
export async function raidTask(deps: RaidDeps, uid: string, stage: string, day: string): Promise<RaidTaskOutcome> {
  const { env } = deps;
  const nowMs = env.clock.nowMs();
  let doc = readRaid((await env.store.read(paths.raid(uid))).data);
  // the player hasn't opened the game since this day's raid was booked: today's plan is still "next"
  if (doc && doc.day !== day && doc.next?.day === day) doc = { day, dueMs: doc.next.dueMs, done: false, by: 'none', result: doc.result, updatedAt: nowMs };
  if (!doc || doc.day !== day) return 'stale';
  if (doc.done) return 'done';
  const { presence, settings } = await context(env, uid);
  if (presence && nowMs - presence.seenMs > RAID_HORIZON_MS) return 'stale';

  if (stage.startsWith('w')) {
    const minutes = Math.max(1, Math.round((doc.dueMs - nowMs) / MINUTE_MS));
    // a warning booked for an older plan of the day
    if (Math.abs(minutes - Number(stage.slice(1))) > 2) return 'stale';
    const sent = await sendDirect(env, deps.messenger, uid, 'raid_soon', { minutes: String(minutes) });
    return sent === 'sent' ? 'warned' : 'held';
  }

  if (nowMs < doc.dueMs - MINUTE_MS) return 'early';
  if (isOnline(presence, nowMs)) {
    // they're playing: the game runs the raid; look again in case they leave first
    await deps.tasks.schedule(uid, { kind: 'raid', stage: 'due', day }, nowMs + RECHECK_MS);
    return 'online';
  }

  const settle = async (by: RaidPlan['by'], result: RaidResult | null) => {
    doc!.done = true;
    doc!.by = by;
    if (result) doc!.result = result;
    doc!.updatedAt = nowMs;
    await planTomorrow(deps, uid, doc!, presence, settings);
    await env.store.writeBatch([{ path: paths.raid(uid), data: doc as unknown as Record<string, unknown> }]);
  };

  const inventory = (await env.store.read(paths.inventory(uid))).data;
  const shieldUntil = Number((inventory?.['activeUntil'] as Record<string, number> | undefined)?.['shield'] ?? 0);
  if (shieldUntil > nowMs) {
    await settle('shield', null);
    return 'shield';
  }

  const island = (await env.store.read(paths.island(uid))).data as unknown as IslandDoc | undefined;
  const save = island ? (JSON.parse(island.state) as IslandSave & { buildings?: unknown[] }) : undefined;
  const islandLevel = Number(island?.summary?.level ?? RAID_START_LEVEL);
  if (!island || !save || (save.buildings?.length ?? 0) < RAID_MIN_BUILDINGS || islandLevel < RAID_START_LEVEL || !deps.engine.raid) {
    await settle('skipped', null);
    return 'skipped';
  }

  const at = Math.max(nowMs, Math.ceil(save.simAt * 1000));
  const run = await deps.engine.raid(save, at / 1000, dayNumber(day));
  const result: RaidResult = {
    size: run.raid.size,
    won: run.raid.won,
    stolen: run.raid.stolen.filter((c) => c.n > 0).slice(0, 64).map((c) => ({ item: c.item, n: c.n })),
    coins: run.raid.coins,
    trampled: run.raid.trampled.slice(0, 64),
    ruined: run.raid.ruined.slice(0, 8),
    atMs: nowMs,
  };
  // the server's copy takes the losses too (leaderboard, profile); a sync that got there first wins
  await env.store.runTransaction(async (tx) => {
    const now = await tx.get(paths.island(uid));
    if (!now.exists || (now.data as unknown as IslandDoc).rev !== island.rev) return;
    tx.set(
      paths.island(uid),
      {
        rev: island.rev + 1,
        state: JSON.stringify(run.state),
        simAtMs: Math.round(run.state.simAt * 1000),
        ...(run.summary ? { summary: run.summary } : {}),
        updatedAt: nowMs,
      },
      { merge: true },
    );
  });
  await settle('server', result);
  await sendDirect(env, deps.messenger, uid, result.won ? 'raid_won' : 'raid_lost', { size: String(result.size) });
  return 'raided';
}
