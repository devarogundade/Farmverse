import { PurchasesDoc, passSeason } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { tokenId, type StoreVerifier } from '../infra/stores';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { islandProgress } from './goals';
import { seasonDoc } from './pass';
import { preparePrize, type PrizePaid } from './prizes';
import type { Env } from './services';

/**
 * Real-money purchases and lasting unlocks. `state/purchases` (client-readable) holds what was
 * unlocked for good: no ads, extra builders, the link reward. Every store transaction is bound to the
 * first player who verified it (`iapTransactions/{store}_{id}`), so a receipt pays once, ever.
 */

/** Builders a player may add with Builder's Passes. */
export const MAX_EXTRA_BUILDERS = 3;

export function readPurchases(data: Record<string, unknown> | undefined, nowMs: number): PurchasesDoc {
  if (!data || legacyDoc('purchases', data)) return { noAds: false, extraBuilders: 0, linkRewarded: false, updatedAt: nowMs };
  const parsed = PurchasesDoc.safeParse(data);
  if (!parsed.success) throw corrupt('purchases', parsed.error);
  return { ...parsed.data };
}

/** True when the player bought No Ads (ad rewards pay without an ad). */
export async function hasNoAds(env: Env, uid: string): Promise<boolean> {
  const snap = await env.store.read(paths.purchases(uid));
  return snap.exists && snap.data?.['noAds'] === true;
}

/** `player-claimLinkReward`: the catalogue's thank-you for linking Google or Apple, once per account. */
export async function claimLinkReward(env: Env, uid: string, linked: boolean): Promise<PrizePaid & { fresh: boolean }> {
  if (!linked) throw new AppError('LINK_REQUIRED');
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const snap = await uow.read(paths.purchases(uid));
    const doc = readPurchases(snap.data, nowMs);
    if (doc.linkRewarded) throw new AppError('ALREADY_CLAIMED');
    const pay = await preparePrize(uow, cat, uid, cat.linkReward, 'link', nowMs);
    pay.apply();
    uow.set(paths.purchases(uid), { ...doc, linkRewarded: true, updatedAt: nowMs });
    uow.set(paths.ledger(uid, 'l_link'), { kind: 'link', at: nowMs, result: pay.paid });
    uow.commit();
    return { ...pay.paid, fresh: true };
  });
}

export interface IapInput {
  store: 'google' | 'apple' | 'fake';
  productId: string;
  transactionId: string;
  receipt: string;
}

/** `iap-verify`: check a store purchase and pay it once (a repeat by the same player returns the first answer). */
export async function verifyPurchase(
  env: Env, uid: string, input: IapInput, stores: StoreVerifier, allowFake: boolean,
): Promise<PrizePaid & { fresh: boolean; pending: boolean }> {
  const cat = await catalogue(env);
  const product = cat.iap[input.productId];
  if (!product) throw new AppError('IAP_UNKNOWN');

  let transactionId = input.transactionId;
  if (input.store === 'fake') {
    if (!allowFake) throw new AppError('IAP_INVALID');
  } else {
    const check = input.store === 'google' ? await stores.google(input.productId, input.receipt) : await stores.apple(input.receipt);
    if (check.state === 'invalid' || check.productId !== input.productId) throw new AppError('IAP_INVALID');
    if (check.state === 'pending') return { coins: 0, gems: 0, boosts: {}, fresh: false, pending: true };
    if (check.transactionId) transactionId = check.transactionId;
  }
  // Google order ids can be missing for some test purchases: the token itself is unique
  const txKey = `${input.store}_${tokenId(transactionId || input.receipt)}`;

  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const [bound, purchasesSnap, passSnap, islandSnap, shopSnap] = await uow.readAll([
      paths.iapTransaction(txKey), paths.purchases(uid), paths.pass(uid), paths.island(uid), paths.shop(uid),
    ]);
    if (bound?.exists) {
      if (bound.data?.['uid'] !== uid) throw new AppError('IAP_INVALID');
      const r = bound.data?.['result'] as PrizePaid | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false, pending: false };
    }
    const purchases = readPurchases(purchasesSnap?.data, nowMs);
    let paid: PrizePaid = { coins: 0, gems: 0, boosts: {} };
    switch (product.kind) {
      case 'gems':
      case 'starter': {
        const pay = await preparePrize(uow, cat, uid, product.contents, 'iap_' + txKey, nowMs);
        pay.apply();
        paid = pay.paid;
        // the starter pack is sold once: its window closes
        if (product.kind === 'starter' && shopSnap?.exists) uow.set(paths.shop(uid), { starterEndsAt: nowMs, updatedAt: nowMs }, { merge: true });
        break;
      }
      case 'pass': {
        const season = passSeason(cat.pass, nowMs);
        const xp = islandProgress(islandSnap?.data)?.xp ?? 0;
        const { doc } = seasonDoc(passSnap?.data, season.id, xp, nowMs);
        uow.set(paths.pass(uid), { ...doc, premiumActive: true, updatedAt: nowMs });
        break;
      }
      case 'builder':
        purchases.extraBuilders = Math.min(MAX_EXTRA_BUILDERS, purchases.extraBuilders + 1);
        break;
      case 'noads':
        purchases.noAds = true;
        break;
    }
    if (product.kind === 'builder' || product.kind === 'noads') uow.set(paths.purchases(uid), { ...purchases, updatedAt: nowMs });
    uow.set(paths.iapTransaction(txKey), {
      uid, store: input.store, productId: input.productId, transactionId, at: nowMs, result: paid,
    });
    uow.set(paths.ledger(uid, 'i_' + txKey), { kind: 'iap', ref: input.productId, at: nowMs, result: paid });
    uow.commit();
    return { ...paid, fresh: true, pending: false };
  });
}
