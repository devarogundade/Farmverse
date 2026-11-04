import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { claimAd } from '../../src/game/ads';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { spinWheel } from '../../src/game/daily';
import { claimLinkReward, verifyPurchase, MAX_EXTRA_BUILDERS } from '../../src/game/purchases';
import { paths } from '../../src/infra/paths';
import { appleCheck, verifyAppleJws, type StoreVerifier } from '../../src/infra/stores';
import { FARMER, MAKER, World } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

/** A store that says what the test tells it to. */
function store(state: 'purchased' | 'pending' | 'invalid', productId: string, transactionId = 'GPA.1234'): StoreVerifier {
  return { google: async () => ({ state, productId, transactionId }), apple: async () => ({ state, productId, transactionId }) };
}
const buy = (w: World, uid: string, productId: string, tx: string, s: StoreVerifier = store('purchased', productId, tx)) =>
  verifyPurchase(w.env, uid, { store: 'google', productId, transactionId: tx, receipt: 'token-' + tx }, s, false);

describe('purchases', () => {
  beforeEach(() => resetCatalogueCache());

  it('pays the link reward once, only to linked accounts', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    expect(await code(claimLinkReward(w.env, FARMER, false))).toBe('LINK_REQUIRED');
    expect(await claimLinkReward(w.env, FARMER, true)).toMatchObject({ gems: DEFAULT_CATALOGUE.linkReward.gems, fresh: true });
    expect(await code(claimLinkReward(w.env, FARMER, true))).toBe('ALREADY_CLAIMED');
    expect(w.store.get(paths.wallet(FARMER))?.['gems']).toBe(DEFAULT_CATALOGUE.startingGems + DEFAULT_CATALOGUE.linkReward.gems);
  });

  it('credits a gem pack once per transaction, to one player only', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    expect(await buy(w, FARMER, 'gems_500', 'GPA.1')).toMatchObject({ gems: 500, fresh: true, pending: false });
    expect(await buy(w, FARMER, 'gems_500', 'GPA.1')).toMatchObject({ gems: 500, fresh: false });
    expect(await code(buy(w, MAKER, 'gems_500', 'GPA.1'))).toBe('IAP_INVALID');
    expect(w.store.get(paths.wallet(FARMER))?.['gems']).toBe(DEFAULT_CATALOGUE.startingGems + 500);
  });

  it('refuses invalid, mismatched, unknown and fake purchases; reports pending ones', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    expect(await code(buy(w, FARMER, 'gems_80', 'GPA.2', store('invalid', 'gems_80')))).toBe('IAP_INVALID');
    expect(await code(buy(w, FARMER, 'gems_80', 'GPA.3', store('purchased', 'gems_6500')))).toBe('IAP_INVALID');
    expect(await code(buy(w, FARMER, 'nope', 'GPA.4'))).toBe('IAP_UNKNOWN');
    expect(await buy(w, FARMER, 'gems_80', 'GPA.5', store('pending', 'gems_80'))).toMatchObject({ pending: true, gems: 0 });
    const fake = { store: 'fake' as const, productId: 'gems_80', transactionId: 'f1', receipt: 'x' };
    expect(await code(verifyPurchase(w.env, FARMER, fake, store('purchased', 'gems_80'), false))).toBe('IAP_INVALID');
    expect(await verifyPurchase(w.env, FARMER, fake, store('purchased', 'gems_80'), true)).toMatchObject({ gems: 80, fresh: true });
  });

  it('unlocks the pass, builders (capped) and no ads', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await buy(w, FARMER, 'farm_pass', 'GPA.10');
    expect(w.store.get(paths.pass(FARMER))?.['premiumActive']).toBe(true);
    for (let i = 0; i < MAX_EXTRA_BUILDERS + 1; i++) await buy(w, FARMER, 'builder_pass', 'GPA.2' + i);
    expect(w.store.get(paths.purchases(FARMER))?.['extraBuilders']).toBe(MAX_EXTRA_BUILDERS);

    // without No Ads a live backend needs the ad's verification; with it, the reward pays straight away
    expect(await code(claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000001', true))).toBe('AD_NOT_VERIFIED');
    expect(await code(spinWheel(w.env, FARMER, 'k_spin_000001', true))).toBe('AD_NOT_VERIFIED');
    await buy(w, FARMER, 'no_ads', 'GPA.30');
    expect(w.store.get(paths.purchases(FARMER))?.['noAds']).toBe(true);
    expect(await claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000002', true)).toMatchObject({ fresh: true });
    expect(await spinWheel(w.env, FARMER, 'k_spin_000002', true)).toMatchObject({ fresh: true });
  });

  it('the starter pack pays its bundle and closes its window', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    w.patch(paths.shop(FARMER), { nextGiftAt: 0, starterEndsAt: w.clock.nowMs() + 1e9, chestDay: {}, updatedAt: 1 });
    const r = await buy(w, FARMER, 'starter_pack', 'GPA.40');
    expect(r).toMatchObject({ coins: DEFAULT_CATALOGUE.starterPack.contents.coins, gems: DEFAULT_CATALOGUE.starterPack.contents.gems });
    expect(w.store.get(paths.shop(FARMER))?.['starterEndsAt']).toBe(w.clock.nowMs());
  });
});

describe('App Store signatures', () => {
  it('refuses anything not signed under Apple Root CA - G3', async () => {
    expect(verifyAppleJws('not.a.jws')).toBeNull();
    const header = Buffer.from(JSON.stringify({ alg: 'ES256', x5c: ['AA==', 'AA=='] })).toString('base64url');
    expect(verifyAppleJws(`${header}.e30.AA`)).toBeNull();
    expect(appleCheck('x.y.z').state).toBe('invalid');
  });
});
