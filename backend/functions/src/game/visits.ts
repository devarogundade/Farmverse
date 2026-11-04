import { AppError } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { islandProgress } from './goals';
import type { IslandDoc } from './islands';
import type { Env } from './services';
import { utcDay } from './wallet';

/**
 * Visits (module 8): another player's island as the server last ran it — buildings, level, net worth,
 * a few stats — and likes (one a day per visitor). Players who turned visitors off, or who have no
 * island on the server yet, can't be visited.
 */

export interface VisitBuildingOut {
  id: string;
  x: number;
  z: number;
  w: number;
  d: number;
}

const int = (v: unknown, min: number, max: number, fallback: number) =>
  typeof v === 'number' && Number.isInteger(v) && v >= min && v <= max ? v : fallback;

/** The buildings in an island save, as a visitor sees them. */
export function visitBuildings(state: string): VisitBuildingOut[] {
  try {
    const list = (JSON.parse(state) as { buildings?: unknown }).buildings;
    if (!Array.isArray(list)) return [];
    const out: VisitBuildingOut[] = [];
    for (const b of list as Record<string, unknown>[]) {
      if (typeof b?.['id'] !== 'string' || (b['id'] as string).length > 48) continue;
      out.push({ id: b['id'] as string, x: int(b['x'], -1000, 1000, 0), z: int(b['z'], -1000, 1000, 0), w: int(b['w'], 1, 64, 1), d: int(b['d'], 1, 64, 1) });
      if (out.length >= 600) break;
    }
    return out;
  } catch {
    return [];
  }
}

export async function getVisit(env: Env, visitorUid: string, ownerUid: string) {
  const [player, settings, island, visits, like] = await Promise.all([
    env.store.read(paths.player(ownerUid)),
    env.store.read(paths.settings(ownerUid)),
    env.store.read(paths.island(ownerUid)),
    env.store.read(paths.visits(ownerUid)),
    env.store.read(paths.visitLike(ownerUid, visitorUid)),
  ]);
  if (!player.exists) throw new AppError('NOT_FOUND');
  if (ownerUid !== visitorUid && settings.data?.['allowVisitors'] === false) throw new AppError('VISITS_CLOSED');
  if (!island.exists) throw new AppError('VISITS_CLOSED');
  const doc = island.data as unknown as IslandDoc;
  const progress = islandProgress(island.data);
  return {
    name: String(player.data?.['displayName'] ?? '').slice(0, 16),
    avatarId: String(player.data?.['avatarId'] ?? 'avatar_default').slice(0, 48),
    locale: String(settings.data?.['locale'] ?? 'en').slice(0, 16),
    level: Math.max(1, Math.floor(doc.summary?.level ?? progress?.bestLevel ?? 1)),
    worth: Math.max(0, Math.floor(doc.summary?.netWorth ?? 0)),
    likes: Math.max(0, Number(visits.data?.['likes'] ?? 0)),
    likedToday: like.data?.['day'] === utcDay(env.clock.nowMs()),
    crops: Math.max(0, progress?.stat('harvested') ?? 0),
    raids: Math.max(0, progress?.stat('raids_won') ?? 0),
    buildings: visitBuildings(doc.state),
  };
}

/** `visits-like`: one like a day per visitor (not your own island). */
export async function likeIsland(env: Env, visitorUid: string, ownerUid: string): Promise<{ likes: number; fresh: boolean }> {
  if (visitorUid === ownerUid) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const today = utcDay(env.clock.nowMs());
    const [owner, visits, like] = await uow.readAll([paths.player(ownerUid), paths.visits(ownerUid), paths.visitLike(ownerUid, visitorUid)]);
    if (!owner?.exists) throw new AppError('NOT_FOUND');
    const likes = Math.max(0, Number(visits?.data?.['likes'] ?? 0));
    if (like?.data?.['day'] === today) return { likes, fresh: false };
    // today's likes too: the owner gets one push a day about them
    const todays = visits?.data?.['likesDay'] === today ? Math.max(0, Number(visits?.data?.['likesToday'] ?? 0)) : 0;
    uow.set(paths.visits(ownerUid), { likes: likes + 1, likesDay: today, likesToday: todays + 1 }, { merge: true });
    uow.set(paths.visitLike(ownerUid, visitorUid), { day: today });
    uow.commit();
    return { likes: likes + 1, fresh: true };
  });
}
