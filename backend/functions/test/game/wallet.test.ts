import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { earnGems, spendGems } from '../../src/game/wallet';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, World } from './world';

const gemsOf = (w: World) => (w.store.get(paths.wallet(FARMER)) as { gems: number } | undefined)?.gems;
const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

async function player(gems?: number) {
  const w = new World();
  await w.createPlayer(FARMER);
  if (gems !== undefined) w.patch(paths.wallet(FARMER), { gems });
  return w;
}

describe('wallet', () => {
  beforeEach(() => resetCatalogueCache());

  it('opens a new player with the starting gems', async () => {
    const w = await player();
    expect(gemsOf(w)).toBe(DEFAULT_CATALOGUE.startingGems);
  });

  it('buys an offer: takes the gems, says what to add, once per key', async () => {
    const w = await player(100);
    const first = await spendGems(w.env, FARMER, 'coins_1000', 'k_buy_000001');
    expect(first).toEqual({ gems: 90, coins: 1000, boosts: {}, fresh: true });
    const again = await spendGems(w.env, FARMER, 'coins_1000', 'k_buy_000001');
    expect(again).toEqual({ ...first, fresh: false });
    expect(gemsOf(w)).toBe(90);
  });

  it('refuses unknown offers and short balances', async () => {
    const w = await player(5);
    expect(await code(spendGems(w.env, FARMER, 'nothing_here', 'k_buy_000002'))).toBe('OFFER_UNKNOWN');
    expect(await code(spendGems(w.env, FARMER, 'coins_1000', 'k_buy_000003'))).toBe('NOT_ENOUGH_GEMS');
    expect(gemsOf(w)).toBe(5);
  });

  it('sells a once-a-day offer once per UTC day', async () => {
    const w = await player(100);
    expect((await spendGems(w.env, FARMER, 'deal_daily', 'k_deal_00001')).boosts).toEqual({ fert: 3 });
    expect((w.store.get(paths.inventory(FARMER)) as { boosts: Record<string, number> }).boosts).toEqual({ fert: 3 });
    expect(await code(spendGems(w.env, FARMER, 'deal_daily', 'k_deal_00002'))).toBe('OFFER_SOLD_OUT');
    w.clock.advance(DAY);
    expect((await spendGems(w.env, FARMER, 'deal_daily', 'k_deal_00003')).fresh).toBe(true);
    expect(gemsOf(w)).toBe(100 - 2 * 29);
  });

  it('pays earned gems up to the source cap per day', async () => {
    const w = await player(0);
    const cap = DEFAULT_CATALOGUE.earnCaps['inbox']!;
    expect(await earnGems(w.env, FARMER, 'inbox', cap, 'k_earn_00001')).toEqual({ gems: cap, granted: cap, fresh: true });
    expect(await earnGems(w.env, FARMER, 'inbox', 5, 'k_earn_00002')).toEqual({ gems: cap, granted: 0, fresh: true });
    expect((await earnGems(w.env, FARMER, 'inbox', cap, 'k_earn_00001')).fresh).toBe(false);
    w.clock.advance(DAY);
    expect((await earnGems(w.env, FARMER, 'inbox', 1, 'k_earn_00003')).granted).toBe(1);
    expect(gemsOf(w)).toBe(cap + 1);
  });

  it('uses the published catalogue when it is newer', async () => {
    const w = await player(100);
    const published = { ...DEFAULT_CATALOGUE, version: DEFAULT_CATALOGUE.version + 1, gemOffers: { ...DEFAULT_CATALOGUE.gemOffers, coins_1000: { gems: 1, coins: 5, boosts: {}, perDay: 0 } } };
    w.store.put(paths.catalogue(), published);
    resetCatalogueCache(); // an instance re-reads it within CATALOGUE_TTL_MS
    expect(await spendGems(w.env, FARMER, 'coins_1000', 'k_buy_000009')).toMatchObject({ gems: 99, coins: 5 });
  });
});

describe('documents left by the old game', () => {
  beforeEach(() => resetCatalogueCache());

  it('are replaced instead of failing', async () => {
    const w = await player(100);
    w.store.put(paths.inventory(FARMER), { items: { wheat: 3 } });   // the old grid farm's inventory
    expect((await spendGems(w.env, FARMER, 'boost_fert', 'k_old_000001')).boosts).toEqual({ fert: 1 });
    expect(w.store.get(paths.inventory(FARMER))).toMatchObject({ boosts: { fert: 1 } });
  });
});
