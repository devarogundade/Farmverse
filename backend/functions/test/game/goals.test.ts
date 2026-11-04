import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { claimAchievement, islandProgress } from '../../src/game/goals';
import { paths } from '../../src/infra/paths';
import { FARMER, World, islandWith } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

describe('achievements', () => {
  beforeEach(() => resetCatalogueCache());

  it('pays an achievement once, when its stat is reached on the server island', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    const ach = DEFAULT_CATALOGUE.achievements['trader1']!;
    expect(await code(claimAchievement(w.env, FARMER, 'trader1', 'k_ach_000001'))).toBe('ACHIEVEMENT_LOCKED');
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { sold: ach.need - 1 } }));
    expect(await code(claimAchievement(w.env, FARMER, 'trader1', 'k_ach_000002'))).toBe('ACHIEVEMENT_LOCKED');
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { sold: ach.need } }));
    const start = DEFAULT_CATALOGUE.startingGems;
    expect(await claimAchievement(w.env, FARMER, 'trader1', 'k_ach_000003')).toEqual({ gems: start + ach.gems, granted: ach.gems, fresh: true });
    expect((await claimAchievement(w.env, FARMER, 'trader1', 'k_ach_000003')).fresh).toBe(false);
    expect(await code(claimAchievement(w.env, FARMER, 'trader1', 'k_ach_000004'))).toBe('ACHIEVEMENT_CLAIMED');
    expect(await code(claimAchievement(w.env, FARMER, 'nothing', 'k_ach_000005'))).toBe('INVALID_ARGUMENT');
    expect((w.store.get(paths.wallet(FARMER)) as { gems: number }).gems).toBe(start + ach.gems);
  });

  it('reads island progress defensively', () => {
    expect(islandProgress(undefined)).toBeUndefined();
    expect(islandProgress({ state: 'nope' })).toBeUndefined();
    const p = islandProgress(islandWith(0, { stats: { sold: 5 }, ordersToday: 2 }))!;
    expect(p.stat('sold')).toBe(5);
    expect(p.today('orders')).toBe(2);
    expect(p.today('sold')).toBe(0);
  });
});
