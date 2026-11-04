import { generateKeyPairSync, createSign } from 'node:crypto';
import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { claimAd, recordAdView, verifySsv } from '../../src/game/ads';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { claimLogin } from '../../src/game/daily';
import { DAY, FARMER, World } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

/** A signed SSV query, as AdMob sends it (signature over everything before &signature=). */
function signedQuery(fields: Record<string, string>, keyId = 1234) {
  const { privateKey, publicKey } = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const message = new URLSearchParams(fields).toString();
  const signer = createSign('sha256');
  signer.update(message);
  const signature = signer.sign(privateKey).toString('base64').replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return { query: `${message}&signature=${signature}&key_id=${keyId}`, keys: { keys: [{ keyId, pem: publicKey.export({ type: 'spki', format: 'pem' }).toString() }] } };
}

describe('rewarded ads', () => {
  beforeEach(() => resetCatalogueCache());

  it('accepts only AdMob-signed callbacks', () => {
    const { query, keys } = signedQuery({ ad_network: '5450213213286189855', ad_unit: '1234', custom_data: 'k_ad_00000001', reward_amount: '1', reward_item: 'coins', timestamp: '1', transaction_id: 'tx1', user_id: FARMER });
    expect(verifySsv(query, keys)).toMatchObject({ uid: FARMER, key: 'k_ad_00000001', transactionId: 'tx1' });
    expect(verifySsv(query.replace('reward_amount=1', 'reward_amount=9'), keys)).toBeNull();   // tampered
    expect(verifySsv(query, { keys: [] })).toBeNull();   // unknown key
  });

  it('pays a verified ad once, within the daily limit', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    const prize = DEFAULT_CATALOGUE.ads['free_coins']!;
    expect(await code(claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000001', true))).toBe('AD_NOT_VERIFIED');
    await recordAdView(w.env, { uid: FARMER, key: 'k_ad_00000001', transactionId: 'tx1', adUnit: 'u', rewardItem: 'coins', rewardAmount: 1 });
    expect(await claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000001', true)).toEqual({ ...prize.prize, boosts: {}, fresh: true });
    expect((await claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000001', true)).fresh).toBe(false);
    for (let i = 1; i < prize.perDay; i++) await claimAd(w.env, FARMER, 'free_coins', `k_ad_0000010${i}`, false);
    expect(await code(claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000200', false))).toBe('AD_LIMIT');
    w.clock.advance(DAY);
    expect((await claimAd(w.env, FARMER, 'free_coins', 'k_ad_00000201', false)).fresh).toBe(true);
    expect(await code(claimAd(w.env, FARMER, 'nothing', 'k_ad_00000202', false))).toBe('INVALID_ARGUMENT');
  });

  it('doubles today’s login prize, only after it was collected', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    expect(await code(claimAd(w.env, FARMER, 'daily_double', 'k_ad_00000301', false))).toBe('AD_LIMIT');
    await claimLogin(w.env, FARMER, 'k_login_0001');
    expect(await claimAd(w.env, FARMER, 'daily_double', 'k_ad_00000302', false)).toMatchObject({ coins: DEFAULT_CATALOGUE.dailyLogin[0]!.coins, fresh: true });
    expect(await code(claimAd(w.env, FARMER, 'daily_double', 'k_ad_00000303', false))).toBe('AD_LIMIT');
  });

});
