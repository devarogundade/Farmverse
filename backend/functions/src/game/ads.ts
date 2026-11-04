import { createVerify } from 'node:crypto';
import { AppError } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { preparePrize, type PrizePaid } from './prizes';
import type { Env } from './services';
import { utcDay } from './wallet';

/**
 * Rewarded ads. The game shows an AdMob rewarded ad with server-side verification options
 * (user id = uid, custom data = a fresh key); AdMob then calls `ads-ssv` with a signed URL, which is
 * checked against Google's published keys and recorded at `adViews/{uid}_{key}`. `ads-claim` pays the
 * placement's prize (catalogue `ads`) once per key, within the placement's daily limit — and, on live
 * backends, only once that verification is there. The local emulators (and the Unity Editor's
 * placeholder ads, which never call AdMob) skip the verification.
 */

const OP_KEY = /^[A-Za-z0-9_-]{8,64}$/;

// ---- AdMob server-side verification (SSV) --------------------------------------------------------

export const ADMOB_KEYS_URL = 'https://www.gstatic.com/admob/reward/verifier-keys.json';

export interface VerifierKeys {
  keys: { keyId: number; pem: string }[];
}

let keyCache: { keys: VerifierKeys; at: number } | undefined;

/** Google's SSV public keys (cached for a day). */
export async function admobKeys(fetchKeys: () => Promise<VerifierKeys> = defaultFetch, nowMs = Date.now()): Promise<VerifierKeys> {
  if (keyCache && nowMs - keyCache.at < 86_400_000) return keyCache.keys;
  const keys = await fetchKeys();
  keyCache = { keys, at: nowMs };
  return keys;
}

async function defaultFetch(): Promise<VerifierKeys> {
  const res = await fetch(ADMOB_KEYS_URL, { signal: AbortSignal.timeout(10_000) });
  if (!res.ok) throw new Error(`AdMob verifier keys: HTTP ${res.status}`);
  return (await res.json()) as VerifierKeys;
}

export interface VerifiedAdView {
  uid: string;
  key: string;
  transactionId: string;
  adUnit: string;
  rewardItem: string;
  rewardAmount: number;
}

/**
 * Check an SSV callback's query string (everything after `?`). The signed message is the query up to
 * `&signature=`; `signature` (base64url ECDSA, DER) and `key_id` come last. Returns the view, or null
 * when the signature, the key or the fields are not right.
 */
export function verifySsv(query: string, keys: VerifierKeys): VerifiedAdView | null {
  const cut = query.indexOf('&signature=');
  if (cut < 0) return null;
  const message = query.slice(0, cut);
  const params = new URLSearchParams(query);
  const signature = params.get('signature');
  const keyId = Number(params.get('key_id'));
  const key = keys.keys.find((k) => k.keyId === keyId);
  if (!signature || !key) return null;
  const verifier = createVerify('sha256');
  verifier.update(message);
  let ok = false;
  try {
    ok = verifier.verify(key.pem, Buffer.from(signature.replace(/-/g, '+').replace(/_/g, '/'), 'base64'));
  } catch {
    ok = false;
  }
  if (!ok) return null;
  const uid = params.get('user_id') ?? '';
  const opKey = params.get('custom_data') ?? '';
  const transactionId = params.get('transaction_id') ?? '';
  if (!/^[A-Za-z0-9]{20,128}$/.test(uid) || !OP_KEY.test(opKey) || !transactionId) return null;
  return {
    uid, key: opKey, transactionId,
    adUnit: params.get('ad_unit') ?? '',
    rewardItem: params.get('reward_item') ?? '',
    rewardAmount: Number(params.get('reward_amount') ?? 0),
  };
}

/** Record a verified view (idempotent: AdMob may call more than once). */
export async function recordAdView(env: Env, view: VerifiedAdView): Promise<void> {
  await env.store.writeBatch([{ path: paths.adView(view.uid, view.key), data: { ...view, at: env.clock.nowMs() } }]);
}

// ---- claiming --------------------------------------------------------------------------------------

/** `ads-claim`: the placement's prize for one watched ad. */
export async function claimAd(env: Env, uid: string, placement: string, key: string, requireVerification: boolean): Promise<PrizePaid & { fresh: boolean }> {
  const cat = await catalogue(env);
  const def = cat.ads[placement];
  if (!def) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'v_' + key);
    const [entry, view, daySnap, dailySnap, bought] = await uow.readAll([ledgerPath, paths.adView(uid, key), paths.adsDay(uid), paths.daily(uid), paths.purchases(uid)]);
    if (entry?.exists) {
      const r = entry.data?.['result'] as PrizePaid | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    // No Ads: the reward is paid without watching the ad (the daily limits still hold)
    if (requireVerification && !view?.exists && bought?.data?.['noAds'] !== true) throw new AppError('AD_NOT_VERIFIED');
    const today = utcDay(nowMs);
    const day = daySnap?.data?.['day'] === today ? { ...((daySnap.data['counts'] as Record<string, number> | undefined) ?? {}) } : {};
    const watched = day[placement] ?? 0;
    if (watched >= def.perDay) throw new AppError('AD_LIMIT');

    let prize = def.prize;
    if (placement === 'daily_double') {
      // today's login prize again: only after it was collected today
      const daily = dailySnap?.data;
      if (!daily || daily['loginOn'] !== today) throw new AppError('AD_LIMIT');
      const next = Number(daily['loginDay'] ?? 1);
      const claimedDay = next <= 1 ? 7 : next - 1;
      prize = cat.dailyLogin[claimedDay - 1] ?? def.prize;
    }
    const pay = await preparePrize(uow, cat, uid, prize, 'ad_' + key, nowMs);
    day[placement] = watched + 1;
    uow.set(paths.adsDay(uid), { day: today, counts: day, updatedAt: nowMs });
    pay.apply();
    uow.set(ledgerPath, { kind: 'ad', ref: placement, verified: view?.exists === true, at: nowMs, result: pay.paid });
    uow.commit();
    return { ...pay.paid, fresh: true };
  });
}
