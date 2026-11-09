import { GoalsDoc, type ChestId } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { MS_PER_DAY } from '../domain';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import type { Env } from './services';
import { creditInTransaction } from './wallet';

/**
 * What the server reads from its copy of the island (`state/island`, IslandState JSON) to check goals:
 * lifetime stats, and today's progress (the economy's UTC day, its daily goals and the stats counted
 * from the start of the day). The copy is the island the engine last ran — mirrored from the device
 * before each claim, and run by the server itself once the island moves there.
 */
export interface IslandProgress {
  /** The island's UTC day number (floor(seconds / 86400)), -1 when unknown. */
  readonly day: number;
  stat(name: string): number;
  /** A stat counted since the island's day began. */
  today(name: string): number;
  readonly dailies: readonly number[];
  readonly dailyClaimed: readonly number[];
  /** Island XP (the Farm Pass counts from it). */
  readonly xp: number;
  /** Story goals done. */
  readonly goalIndex: number;
  /** The best island level reached. */
  readonly bestLevel: number;
}

interface Count {
  item?: unknown;
  n?: unknown;
}

const counts = (list: unknown): Map<string, number> => {
  const map = new Map<string, number>();
  if (!Array.isArray(list)) return map;
  for (const c of list as Count[]) if (typeof c?.item === 'string' && typeof c.n === 'number' && Number.isFinite(c.n)) map.set(c.item, c.n);
  return map;
};
const ints = (list: unknown): number[] => (Array.isArray(list) ? list.filter((x): x is number => Number.isInteger(x)) : []);

/** The island's progress from the server copy, or undefined when there is no usable copy. */
export function islandProgress(islandDoc: Record<string, unknown> | undefined): IslandProgress | undefined {
  const state = islandDoc?.['state'];
  if (typeof state !== 'string') return undefined;
  let economy: Record<string, unknown> | undefined;
  try {
    economy = (JSON.parse(state) as { economy?: Record<string, unknown> }).economy;
  } catch {
    return undefined;
  }
  if (!economy || typeof economy !== 'object') return undefined;
  const stats = counts(economy['stats']);
  const base = counts(economy['dayBase']);
  const day = typeof economy['day'] === 'number' ? economy['day'] : -1;
  const stat = (name: string) => stats.get(name) ?? 0;
  return {
    day,
    stat,
    today: (name) => Math.max(0, stat(name) - (base.get(name) ?? stat(name))),
    dailies: ints(economy['dailies']),
    dailyClaimed: ints(economy['dailyClaimed']),
    goalIndex: Number.isInteger(economy['goalIndex']) ? (economy['goalIndex'] as number) : 0,
    bestLevel: Number.isInteger(economy['bestLevel']) ? (economy['bestLevel'] as number) : 1,
    xp: typeof economy['xp'] === 'number' && Number.isFinite(economy['xp']) ? Math.max(0, Math.floor(economy['xp'])) : 0,
  };
}

/** The UTC day number of a server time, as the island counts days. */
export const dayNumber = (nowMs: number): number => Math.floor(nowMs / MS_PER_DAY);

/** Is today's chest earned on the island? Throws CHEST_LOCKED with how far along it is. */
export function checkChest(progress: IslandProgress | undefined, chest: ChestId, ordersNeeded: number, nowMs: number): void {
  // the daily X share: nothing to check (the post can't be seen from here); once a UTC day like the others
  if (chest === 'share') return;
  const today = progress !== undefined && progress.day === dayNumber(nowMs);
  if (chest === 'order') {
    const have = today ? progress!.today('orders') : 0;
    if (have < ordersNeeded) throw new AppError('CHEST_LOCKED', { have, need: ordersNeeded });
    return;
  }
  const need = today ? progress!.dailies.length : 3;
  const have = today ? progress!.dailies.filter((d) => progress!.dailyClaimed.includes(d)).length : 0;
  if (need === 0 || have < need) throw new AppError('CHEST_LOCKED', { have, need: Math.max(need, 1) });
}

/** `goals-claimAchievement`: an achievement's gems, once, when its stat is reached on the island. */
export async function claimAchievement(env: Env, uid: string, id: string, key: string): Promise<{ gems: number; granted: number; fresh: boolean }> {
  const cat = await catalogue(env);
  const ach = cat.achievements[id];
  if (!ach) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'a_' + key);
    const [entry, goalsSnap, islandSnap] = await uow.readAll([ledgerPath, paths.goals(uid), paths.island(uid)]);
    if (entry?.exists) {
      const r = entry.data?.['result'] as { gems: number; granted: number } | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    let goals: GoalsDoc = { achievements: {}, updatedAt: nowMs };
    if (goalsSnap?.exists && !legacyDoc('goals', goalsSnap.data)) {
      const parsed = GoalsDoc.safeParse(goalsSnap.data);
      if (!parsed.success) throw corrupt('goals', parsed.error);
      goals = { ...parsed.data, achievements: { ...parsed.data.achievements } };
    }
    if (goals.achievements[id]) throw new AppError('ACHIEVEMENT_CLAIMED');
    const have = islandProgress(islandSnap?.data)?.stat(ach.stat) ?? 0;
    if (have < ach.need) throw new AppError('ACHIEVEMENT_LOCKED', { have, need: ach.need });

    const credit = await creditInTransaction(uow, cat, uid, ach.gems, 'ach_' + id, nowMs);
    const result = { gems: credit.balance, granted: ach.gems };
    goals.achievements[id] = nowMs;
    goals.updatedAt = nowMs;
    uow.set(paths.goals(uid), goals);
    credit.apply();
    uow.set(ledgerPath, { kind: 'achievement', ref: id, at: nowMs, result });
    uow.commit();
    return { ...result, fresh: true };
  });
}
