import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { claimChest, claimGift, useBoost } from '../../src/game/shop';
import { spendGems } from '../../src/game/wallet';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, HOUR, MINUTE, World, islandWith } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));
const inv = (w: World) => w.store.get(paths.inventory(FARMER)) as { boosts: Record<string, number>; activeUntil: Record<string, number> } | undefined;
const gems = (w: World) => (w.store.get(paths.wallet(FARMER)) as { gems: number }).gems;

async function player() {
  const w = new World();
  await w.createPlayer(FARMER);
  return w;
}

describe('shop', () => {
  beforeEach(() => resetCatalogueCache());

  it('opens the starter pack window for a new player', async () => {
    const w = await player();
    const shop = w.store.get(paths.shop(FARMER)) as { starterEndsAt: number; nextGiftAt: number };
    expect(shop.starterEndsAt).toBe(w.clock.nowMs() + DEFAULT_CATALOGUE.starterPack.windowHours * HOUR);
    expect(shop.nextGiftAt).toBe(0);
  });

  it('gives the free gift, then not again until it is ready', async () => {
    const w = await player();
    const first = await claimGift(w.env, FARMER, 'k_gift_00001');
    expect(first).toEqual({ coins: DEFAULT_CATALOGUE.freeGift.coins, nextGiftAt: w.clock.nowMs() + DEFAULT_CATALOGUE.freeGift.everyMinutes * MINUTE, fresh: true });
    expect((await claimGift(w.env, FARMER, 'k_gift_00001')).fresh).toBe(false);
    expect(await code(claimGift(w.env, FARMER, 'k_gift_00002'))).toBe('GIFT_NOT_READY');
    w.clock.advance(DEFAULT_CATALOGUE.freeGift.everyMinutes * MINUTE);
    expect((await claimGift(w.env, FARMER, 'k_gift_00003')).fresh).toBe(true);
  });

  it('keeps the chests shut until the island shows today is done', async () => {
    const w = await player();
    expect(await code(claimChest(w.env, FARMER, 'order', 'k_chest_0000'))).toBe('CHEST_LOCKED');
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { ordersToday: 5 }));
    expect(await code(claimChest(w.env, FARMER, 'order', 'k_chest_0001'))).toBe('CHEST_LOCKED');
    expect(await code(claimChest(w.env, FARMER, 'task', 'k_chest_0002'))).toBe('CHEST_LOCKED');
    // yesterday's island doesn't count today
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs() - DAY, { ordersToday: 9, dailiesDone: true }));
    expect(await code(claimChest(w.env, FARMER, 'order', 'k_chest_0003'))).toBe('CHEST_LOCKED');
  });

  it('opens each chest once a day: gems to the wallet, boosts to the inventory, coins to the game', async () => {
    const w = await player();
    const done = () => w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { ordersToday: 6, dailiesDone: true }));
    done();
    const start = gems(w);
    const chest = DEFAULT_CATALOGUE.chests['order']!;
    expect(await claimChest(w.env, FARMER, 'order', 'k_chest_0001')).toEqual({ ...chest, fresh: true });
    expect(gems(w)).toBe(start + chest.gems);
    expect(inv(w)!.boosts).toEqual(chest.boosts);
    expect(await code(claimChest(w.env, FARMER, 'order', 'k_chest_0002'))).toBe('CHEST_CLAIMED');
    expect((await claimChest(w.env, FARMER, 'order', 'k_chest_0001')).fresh).toBe(false);
    expect((await claimChest(w.env, FARMER, 'task', 'k_chest_0003')).fresh).toBe(true);
    w.clock.advance(DAY);
    done();
    expect((await claimChest(w.env, FARMER, 'order', 'k_chest_0004')).fresh).toBe(true);
    expect(gems(w)).toBe(start + 2 * chest.gems + DEFAULT_CATALOGUE.chests['task']!.gems);
  });

  it('gives the daily X share once a UTC day with nothing to unlock: 100 coins to the game, 5 gems to the wallet', async () => {
    const w = await player();   // no island at all: the share chest checks nothing
    const start = gems(w);
    const share = DEFAULT_CATALOGUE.chests['share']!;
    expect(share).toEqual({ coins: 100, gems: 5, boosts: {} });
    expect(await claimChest(w.env, FARMER, 'share', 'k_share_0001')).toEqual({ ...share, fresh: true });
    expect(gems(w)).toBe(start + 5);
    expect(inv(w)).toBeUndefined();   // no boosts, so the inventory isn't touched
    expect((await claimChest(w.env, FARMER, 'share', 'k_share_0001')).fresh).toBe(false);   // a retry pays nothing more
    expect(await code(claimChest(w.env, FARMER, 'share', 'k_share_0002'))).toBe('CHEST_CLAIMED');
    expect(gems(w)).toBe(start + 5);
    w.clock.advance(DAY);
    expect((await claimChest(w.env, FARMER, 'share', 'k_share_0003')).fresh).toBe(true);
    expect(gems(w)).toBe(start + 10);
  });

  it('uses a boost: one fewer, running for its minutes, stacking on a running one', async () => {
    const w = await player();
    expect(await code(useBoost(w.env, FARMER, 'fert', 'k_use_00001'))).toBe('BOOST_NONE');
    await spendGems(w.env, FARMER, 'deal_daily', 'k_deal_00001');   // 3 fertilizer
    const now = w.clock.nowMs();
    const run = DEFAULT_CATALOGUE.boosts['fert']!.minutes * MINUTE;
    expect(await useBoost(w.env, FARMER, 'fert', 'k_use_00002')).toEqual({ left: 2, activeUntil: now + run, fresh: true });
    expect((await useBoost(w.env, FARMER, 'fert', 'k_use_00002')).fresh).toBe(false);
    expect((await useBoost(w.env, FARMER, 'fert', 'k_use_00003')).activeUntil).toBe(now + 2 * run);
    expect(inv(w)!.boosts['fert']).toBe(1);
  });
});
