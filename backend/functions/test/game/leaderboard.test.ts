import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE, weekOf } from '@farmverse/contracts';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { payWeek, postToBoard, prizeFor } from '../../src/game/leaderboard';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, MAKER, World } from './world';

describe('leaderboard', () => {
  beforeEach(() => resetCatalogueCache());

  it('weeks start on Monday UTC', () => {
    const w = weekOf(Date.UTC(2026, 10, 2, 0, 0, 1));   // Monday 2 Nov 2026
    expect(new Date(w.startsAt).getUTCDay()).toBe(1);
    expect(weekOf(w.startsAt - 1).index).toBe(w.index - 1);
  });

  it('writes a row from the engine summary, with the player name and language', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await postToBoard(w.env, FARMER, { netWorth: 12_345.7, level: 7, xp: 900 });
    await postToBoard(w.env, MAKER, { netWorth: 1, level: 1, xp: 0 });   // no player: no row
    const week = weekOf(w.clock.nowMs()).id;
    expect(w.store.get(paths.boardEntry(week, FARMER))).toMatchObject({ name: w.player(FARMER).displayName, worth: 12_345, level: 7, locale: expect.any(String) });
    expect(w.store.get(paths.boardEntry(week, MAKER))).toBeUndefined();
  });

  it('pays last week by rank, once', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    await postToBoard(w.env, FARMER, { netWorth: 500, level: 3, xp: 0 });
    await postToBoard(w.env, MAKER, { netWorth: 900, level: 4, xp: 0 });
    w.clock.advance(7 * DAY);
    expect(await payWeek(w.env)).toMatchObject({ paid: 2 });
    expect((await payWeek(w.env)).paid).toBe(0);
    const rewards = (uid: string) => w.store.paths(`players/${uid}/rewards/`).map((p) => w.store.get(p)!);
    const first = prizeFor(DEFAULT_CATALOGUE.leaderboardPrizes, 1)!;
    const second = prizeFor(DEFAULT_CATALOGUE.leaderboardPrizes, 2)!;
    expect(rewards(MAKER)).toEqual([expect.objectContaining({ reason: 'leaderboard_week', coins: first.coins, gems: first.gems, about: '#1' })]);
    expect(rewards(FARMER)).toEqual([expect.objectContaining({ coins: second.coins, gems: second.gems, about: '#2' })]);
  });
});
