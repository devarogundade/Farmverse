import type { PlayerDoc } from '@farmverse/contracts';
import { FixedClock } from '../../src/core/clock';
import { memoryLogger } from '../../src/core/logger';
import * as players from '../../src/game/players';
import type { Env } from '../../src/game/services';
import { paths } from '../../src/infra/paths';
import { MemoryStore } from '../../src/infra/memoryStore';

export const MINUTE = 60_000;
export const HOUR = 60 * MINUTE;
export const DAY = 24 * HOUR;

export const FARMER = 'farmerAAAAAAAAAAAAAAAAAA';
export const MAKER = 'makerAAAAAAAAAAAAAAAAAAA';

/** The backend in memory: a MemoryStore and a hand-driven clock. */
export class World {
  readonly store = new MemoryStore();
  readonly clock = new FixedClock(Date.UTC(2026, 9, 21, 8, 0, 0));
  readonly log = memoryLogger();

  get env(): Env {
    return { store: this.store, clock: this.clock, log: this.log };
  }

  /** A new player, named after their uid. */
  async createPlayer(uid: string, displayName = `farm_${uid.slice(0, 6)}`): Promise<void> {
    await players.create(this.env, uid, { displayName, avatarId: 'avatar_default' });
  }

  player(uid: string): PlayerDoc {
    return this.store.get(paths.player(uid)) as PlayerDoc;
  }

  patch(path: string, fields: Record<string, unknown>): void {
    this.store.put(path, { ...(this.store.get(path) ?? {}), ...fields });
  }
}

/** A server copy of the island with these stats, on the island day of `nowMs` (dailies all claimed when `dailiesDone`). */
export function islandWith(nowMs: number, opts: { stats?: Record<string, number>; ordersToday?: number; dailiesDone?: boolean; xp?: number } = {}) {
  const stats = { ...(opts.stats ?? {}) };
  const orders = stats['orders'] ?? 0;
  const today = opts.ordersToday ?? 0;
  stats['orders'] = orders + today;
  return {
    rev: 1,
    state: JSON.stringify({
      rulesVersion: 1,
      economy: {
        bestLevel: 5,
        xp: opts.xp ?? 0,
        day: Math.floor(nowMs / 86_400_000),
        stats: Object.entries(stats).map(([item, n]) => ({ item, n })),
        dayBase: [{ item: 'orders', n: orders }],
        dailies: [0, 1, 2],
        dailyClaimed: opts.dailiesDone ? [0, 1, 2] : [0],
      },
    }),
  };
}
