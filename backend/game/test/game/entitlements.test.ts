import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { grantEntitlement, MAX_EXTRA_BUILDERS, readPurchases } from '../../src/game/entitlements';
import { paths } from '../../src/infra/paths';
import { FARMER, MAKER, World } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));
const gemPack = (gems: number) => ({ kind: 'bundle' as const, contents: { coins: 0, gems, boosts: {} } });

describe('entitlements', () => {
  beforeEach(() => resetCatalogueCache());

  it('credits a gem bundle once per ref, per player', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    expect(await grantEntitlement(w.env, FARMER, gemPack(500), 'nft_1')).toMatchObject({ gems: 500, fresh: true });
    expect(await grantEntitlement(w.env, FARMER, gemPack(500), 'nft_1')).toMatchObject({ gems: 500, fresh: false });
    expect(w.store.get(paths.wallet(FARMER))?.['gems']).toBe(DEFAULT_CATALOGUE.startingGems + 500);
    expect(await grantEntitlement(w.env, MAKER, gemPack(80), 'nft_1')).toMatchObject({ gems: 80, fresh: true });
    expect(await code(grantEntitlement(w.env, FARMER, gemPack(1), 'bad ref!'))).toBe('INVALID_ARGUMENT');
  });

  it('unlocks the premium pass and extra builders (capped)', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await grantEntitlement(w.env, FARMER, { kind: 'pass' }, 'pass_1');
    expect(w.store.get(paths.pass(FARMER))?.['premiumActive']).toBe(true);
    for (let i = 0; i < MAX_EXTRA_BUILDERS + 1; i++) await grantEntitlement(w.env, FARMER, { kind: 'builder' }, 'builder_' + i);
    expect(w.store.get(paths.purchases(FARMER))?.['extraBuilders']).toBe(MAX_EXTRA_BUILDERS);
  });

  it('the starter pack pays its bundle and closes its window', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    w.patch(paths.shop(FARMER), { nextGiftAt: 0, starterEndsAt: w.clock.nowMs() + 1e9, chestDay: {}, updatedAt: 1 });
    const r = await grantEntitlement(w.env, FARMER, { kind: 'starter' }, 'starter_1');
    expect(r).toMatchObject({ coins: DEFAULT_CATALOGUE.starterPack.contents.coins, gems: DEFAULT_CATALOGUE.starterPack.contents.gems });
    expect(w.store.get(paths.shop(FARMER))?.['starterEndsAt']).toBe(w.clock.nowMs());
  });

  it('reads older purchases docs without the dropped flags', () => {
    expect(readPurchases({ noAds: true, extraBuilders: 2, linkRewarded: true, updatedAt: 5 }, 9)).toEqual({ extraBuilders: 2, updatedAt: 5 });
    expect(readPurchases(undefined, 9)).toEqual({ extraBuilders: 0, updatedAt: 9 });
  });
});
