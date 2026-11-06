import { describe, expect, it } from 'vitest';
import { AppError } from '../../src/core/errors';
import { getVisit, likeIsland } from '../../src/game/visits';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, MAKER, World, islandWith } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

describe('visits', () => {
  it('shows an island the server ran: buildings, level, worth, stats, likes once a day', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    expect(await code(getVisit(w.env, MAKER, FARMER))).toBe('VISITS_CLOSED');   // no island yet
    const island = islandWith(w.clock.nowMs(), { stats: { harvested: 42, raids_won: 3 } });
    const state = JSON.parse(island.state);
    state.buildings = [{ id: 'farmhouse', uid: 1, x: 4, z: 5, w: 3, d: 3, price: 0 }, { id: 'x'.repeat(60), x: 0, z: 0 }];
    w.store.put(paths.island(FARMER), { ...island, state: JSON.stringify(state), summary: { netWorth: 1234, level: 6, xp: 1 } });
    const seen = await getVisit(w.env, MAKER, FARMER);
    expect(seen).toMatchObject({ name: w.player(FARMER).displayName, level: 6, worth: 1234, crops: 42, raids: 3, likes: 0, likedToday: false });
    expect(seen.buildings).toEqual([{ id: 'farmhouse', x: 4, z: 5, w: 3, d: 3 }]);
    expect(await likeIsland(w.env, MAKER, FARMER)).toEqual({ likes: 1, fresh: true });
    expect(await likeIsland(w.env, MAKER, FARMER)).toEqual({ likes: 1, fresh: false });
    expect((await getVisit(w.env, MAKER, FARMER)).likedToday).toBe(true);
    w.clock.advance(DAY);
    expect((await likeIsland(w.env, MAKER, FARMER)).likes).toBe(2);
    expect(await code(likeIsland(w.env, FARMER, FARMER))).toBe('INVALID_ARGUMENT');
    w.patch(paths.settings(FARMER), { allowVisitors: false });
    expect(await code(getVisit(w.env, MAKER, FARMER))).toBe('VISITS_CLOSED');
  });
});
