import { PurchasesDoc, passSeason, type Bundle, type CatalogueDoc } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { islandProgress } from './goals';
import { seasonDoc } from './pass';
import { preparePrize, type PrizePaid } from './prizes';
import type { Env } from './services';

/**
 * Lasting entitlements: gems or a bundle credited, the starter pack, the premium Farm Pass for the
 * current season, an extra builder. Internal only — no callable grants these directly; a redemption
 * flow (e.g. an NFT pack) calls {@link grantEntitlement} with its own unique `ref`, which makes the
 * grant idempotent (a second call with the same ref returns the first answer, `fresh: false`).
 *
 * `state/purchases` (client-readable, PurchasesDoc) keeps what was unlocked for good.
 */

/** Builders a player may add for good. */
export const MAX_EXTRA_BUILDERS = 3;

/** What an entitlement grants. */
export type Entitlement =
  /** A bundle paid out (gem packs: gems only). */
  | { kind: 'bundle'; contents: Bundle }
  /** The catalogue's starter pack contents; its shop window closes. */
  | { kind: 'starter' }
  /** The premium Farm Pass for the current season. */
  | { kind: 'pass' }
  /** One more builder, up to {@link MAX_EXTRA_BUILDERS}. */
  | { kind: 'builder' };

export function readPurchases(data: Record<string, unknown> | undefined, nowMs: number): PurchasesDoc {
  if (!data || legacyDoc('purchases', data)) return { extraBuilders: 0, updatedAt: nowMs };
  // older docs carried noAds / linkRewarded: dropped here
  const parsed = PurchasesDoc.safeParse({ extraBuilders: data['extraBuilders'], updatedAt: data['updatedAt'] });
  if (!parsed.success) throw corrupt('purchases', parsed.error);
  return { ...parsed.data };
}

/**
 * Grant an entitlement inside a transaction. Reads everything it needs first, so call it before the
 * caller's first write; `apply()` performs the writes. `ref` tags the wallet/inventory credit.
 */
export async function prepareEntitlement(
  uow: UnitOfWork, cat: CatalogueDoc, uid: string, grant: Entitlement, ref: string, nowMs: number,
): Promise<{ paid: PrizePaid; apply: () => void }> {
  switch (grant.kind) {
    case 'bundle':
      return preparePrize(uow, cat, uid, grant.contents, ref, nowMs);
    case 'starter': {
      const shopSnap = await uow.read(paths.shop(uid));
      const pay = await preparePrize(uow, cat, uid, cat.starterPack.contents, ref, nowMs);
      return {
        paid: pay.paid,
        apply: () => {
          pay.apply();
          // the starter pack is given once: its window closes
          if (shopSnap.exists) uow.set(paths.shop(uid), { starterEndsAt: nowMs, updatedAt: nowMs }, { merge: true });
        },
      };
    }
    case 'pass': {
      const [passSnap, islandSnap] = await uow.readAll([paths.pass(uid), paths.island(uid)]);
      const season = passSeason(cat.pass, nowMs);
      const xp = islandProgress(islandSnap?.data)?.xp ?? 0;
      const { doc } = seasonDoc(passSnap?.data, season.id, xp, nowMs);
      return { paid: { coins: 0, gems: 0, boosts: {} }, apply: () => uow.set(paths.pass(uid), { ...doc, premiumActive: true, updatedAt: nowMs }) };
    }
    case 'builder': {
      const purchases = readPurchases((await uow.read(paths.purchases(uid))).data, nowMs);
      const extraBuilders = Math.min(MAX_EXTRA_BUILDERS, purchases.extraBuilders + 1);
      return { paid: { coins: 0, gems: 0, boosts: {} }, apply: () => uow.set(paths.purchases(uid), { ...purchases, extraBuilders, updatedAt: nowMs }) };
    }
  }
}

const REF = /^[A-Za-z0-9_-]{1,120}$/;

/**
 * Grant an entitlement once per `ref` (its own transaction; ledger `players/{uid}/ledger/e_{ref}`).
 * A repeat with the same ref returns the first answer with `fresh: false`.
 */
export async function grantEntitlement(env: Env, uid: string, grant: Entitlement, ref: string): Promise<PrizePaid & { fresh: boolean }> {
  if (!REF.test(ref)) throw new AppError('INVALID_ARGUMENT');
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'e_' + ref);
    const entry = await uow.read(ledgerPath);
    if (entry.exists) {
      const r = entry.data?.['result'] as PrizePaid | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    const pay = await prepareEntitlement(uow, cat, uid, grant, 'ent_' + ref, nowMs);
    pay.apply();
    uow.set(ledgerPath, { kind: 'entitlement', ref: grant.kind, at: nowMs, result: pay.paid });
    uow.commit();
    return { ...pay.paid, fresh: true };
  });
}
