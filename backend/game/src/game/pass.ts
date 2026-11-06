import { PassDoc, passSeason, type CatalogueDoc, type PurchasesDoc } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { readPurchases } from './entitlements';
import { islandProgress } from './goals';
import { preparePrize, type PrizePaid } from './prizes';
import type { Env } from './services';

/**
 * The Farm Pass (module 5). Seasons roll by themselves (catalogue `pass`); `state/pass` holds the
 * player's season: the island XP they joined at, tiers collected, premium bought. Pass XP is read from
 * the server's copy of the island, so a tier pays only when the island really earned it.
 */

/** This season's doc for the player: a new one (joined at `xp`) when the stored one is from another season. */
export function seasonDoc(data: Record<string, unknown> | undefined, seasonId: string, xp: number, nowMs: number): { doc: PassDoc; fresh: boolean } {
  if (data && !legacyDoc('pass', data)) {
    const parsed = PassDoc.safeParse(data);
    if (!parsed.success) throw corrupt('pass', parsed.error);
    if (parsed.data.seasonId === seasonId) return { doc: { ...parsed.data, free: [...parsed.data.free], premium: [...parsed.data.premium] }, fresh: false };
  }
  return { doc: { seasonId, baseXp: xp, free: [], premium: [], premiumActive: false, updatedAt: nowMs }, fresh: true };
}

/**
 * A new season's doc with premium switched on from a banked Farm Pass, if the player has one
 * (`state/purchases.passCredits`). Returns the writes to make; call before the caller's first write.
 */
export function useBankedPass(
  doc: PassDoc, fresh: boolean, purchasesData: Record<string, unknown> | undefined, nowMs: number,
): { doc: PassDoc; purchases?: PurchasesDoc } {
  if (!fresh || doc.premiumActive) return { doc };
  const purchases = readPurchases(purchasesData, nowMs);
  if (!purchases.passCredits) return { doc };
  return { doc: { ...doc, premiumActive: true }, purchases: { ...purchases, passCredits: purchases.passCredits - 1, updatedAt: nowMs } };
}

/** Pass XP and the highest open tier for an island XP. */
export function passTier(cat: CatalogueDoc, doc: PassDoc, islandXp: number): { passXp: number; tier: number } {
  const passXp = Math.max(0, Math.floor((islandXp - doc.baseXp) / cat.pass.islandXpPerPassXp));
  return { passXp, tier: Math.min(cat.pass.tiers.length, 1 + Math.floor(passXp / cat.pass.xpPerTier)) };
}

/** `pass-join`: make sure the player is in this season (idempotent); pass XP counts from their island XP now. */
export async function joinPass(env: Env, uid: string): Promise<{ seasonId: string; endsAt: number; baseXp: number }> {
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const season = passSeason(cat.pass, nowMs);
    const [passSnap, islandSnap, purchasesSnap] = await uow.readAll([paths.pass(uid), paths.island(uid), paths.purchases(uid)]);
    const xp = islandProgress(islandSnap?.data)?.xp ?? 0;
    const season0 = seasonDoc(passSnap?.data, season.id, xp, nowMs);
    const { doc, purchases } = useBankedPass(season0.doc, season0.fresh, purchasesSnap?.data, nowMs);
    if (season0.fresh) uow.set(paths.pass(uid), doc);
    if (purchases) uow.set(paths.purchases(uid), purchases);
    uow.commit();
    return { seasonId: season.id, endsAt: season.endsAt, baseXp: doc.baseXp };
  });
}

/** `pass-claim`: a tier's free (or, with the pass bought, premium) prize, once, when the island's pass XP reaches it. */
export async function claimPass(env: Env, uid: string, tier: number, premium: boolean, key: string): Promise<PrizePaid & { fresh: boolean }> {
  const cat = await catalogue(env);
  const def = cat.pass.tiers[tier - 1];
  if (!def) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'p_' + key);
    const entry = await uow.read(ledgerPath);
    if (entry.exists) {
      const r = entry.data?.['result'] as PrizePaid | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    const season = passSeason(cat.pass, nowMs);
    const [passSnap, islandSnap, purchasesSnap] = await uow.readAll([paths.pass(uid), paths.island(uid), paths.purchases(uid)]);
    const xp = islandProgress(islandSnap?.data)?.xp ?? 0;
    const season0 = seasonDoc(passSnap?.data, season.id, xp, nowMs);
    const { doc, purchases } = useBankedPass(season0.doc, season0.fresh, purchasesSnap?.data, nowMs);
    if (premium && !doc.premiumActive) throw new AppError('PASS_PREMIUM_REQUIRED');
    const claimed = premium ? doc.premium : doc.free;
    if (claimed.includes(tier)) throw new AppError('PASS_CLAIMED');
    const open = passTier(cat, doc, xp).tier;
    if (tier > open) throw new AppError('PASS_LOCKED', { have: open, need: tier });
    const pay = await preparePrize(uow, cat, uid, premium ? def.premium : def.free, `pass_${season.id}_${premium ? 'p' : 'f'}${tier}`, nowMs);
    claimed.push(tier);
    claimed.sort((a, b) => a - b);
    uow.set(paths.pass(uid), { ...doc, updatedAt: nowMs });
    if (purchases) uow.set(paths.purchases(uid), purchases);
    pay.apply();
    uow.set(ledgerPath, { kind: 'pass', ref: `${season.id}:${premium ? 'premium' : 'free'}:${tier}`, at: nowMs, result: pay.paid });
    uow.commit();
    return { ...pay.paid, fresh: true };
  });
}
