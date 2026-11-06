import { WalletDoc, type CatalogueDoc, type GemSource } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import type { Env } from './services';
import { addBoosts, readInventory } from './shop';

/**
 * Gems, owned by the server (money module 1).
 *
 *   players/{uid}/state/wallet     WalletDoc — the balance the game shows (client-readable)
 *   players/{uid}/state/walletDay  today's earnings per source and purchases per offer (server only)
 *   players/{uid}/ledger/{s_|e_|g_}{key}  every movement, keyed by the client's operation key, so a
 *                                  retried call repeats the first answer instead of paying twice
 *
 * A missing wallet holds the catalogue's starting gems.
 */

/** The UTC day of a timestamp, `2026-11-01`: daily limits reset at UTC midnight. */
export function utcDay(ms: number): string {
  return new Date(ms).toISOString().slice(0, 10);
}

interface DayDoc {
  day: string;
  earned: Record<string, number>;
  bought: Record<string, number>;
}

function readDay(data: Record<string, unknown> | undefined, today: string): DayDoc {
  if (!data || data['day'] !== today) return { day: today, earned: {}, bought: {} };
  const counts = (v: unknown) => (v && typeof v === 'object' ? { ...(v as Record<string, number>) } : {});
  return { day: today, earned: counts(data['earned']), bought: counts(data['bought']) };
}

function balanceOf(data: Record<string, unknown> | undefined, cat: CatalogueDoc): number {
  if (!data || legacyDoc('wallet', data)) return cat.startingGems;
  const parsed = WalletDoc.safeParse(data);
  if (!parsed.success) throw corrupt('wallet', parsed.error);
  return parsed.data.gems;
}

export interface SpendResult {
  gems: number;
  coins: number;
  boosts: Record<string, number>;
  fresh: boolean;
}

/** `wallet-spend`: buy a catalogue offer with gems. */
export async function spendGems(env: Env, uid: string, offerId: string, key: string): Promise<SpendResult> {
  const cat = await catalogue(env);
  const offer = cat.gemOffers[offerId];
  if (!offer) throw new AppError('OFFER_UNKNOWN');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 's_' + key);
    const [entry, wallet, daySnap, invSnap] = await uow.readAll([ledgerPath, paths.wallet(uid), paths.walletDay(uid), paths.inventory(uid)]);
    if (entry?.exists) {
      const r = entry.data?.['result'] as SpendResult | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    const day = readDay(daySnap?.data, utcDay(nowMs));
    const boughtToday = day.bought[offerId] ?? 0;
    if (offer.perDay > 0 && boughtToday >= offer.perDay) throw new AppError('OFFER_SOLD_OUT');
    const before = balanceOf(wallet?.data, cat);
    if (before < offer.gems) throw new AppError('NOT_ENOUGH_GEMS', { need: String(offer.gems - before) });

    const result: SpendResult = { gems: before - offer.gems, coins: offer.coins, boosts: { ...offer.boosts }, fresh: true };
    day.bought[offerId] = boughtToday + 1;
    // the client shows "Bought" on daily-limited offers from these
    uow.set(paths.wallet(uid), { gems: result.gems, boughtOn: day.day, bought: { ...day.bought }, updatedAt: nowMs });
    uow.set(paths.walletDay(uid), { ...day });
    // boosts go straight into the server's inventory (Storage reads it)
    if (Object.keys(offer.boosts).length > 0) uow.set(paths.inventory(uid), addBoosts(readInventory(invSnap?.data, nowMs), offer.boosts, nowMs));
    uow.set(ledgerPath, { kind: 'spend', ref: offerId, delta: -offer.gems, balance: result.gems, at: nowMs, result });
    uow.commit();
    return result;
  });
}

export interface EarnResult {
  gems: number;
  granted: number;
  fresh: boolean;
}

/** `wallet-earn`: gems the game says were earned, within the source's daily cap. */
export async function earnGems(env: Env, uid: string, source: GemSource, amount: number, key: string): Promise<EarnResult> {
  const cat = await catalogue(env);
  const cap = cat.earnCaps[source] ?? 0;
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'e_' + key);
    const [entry, wallet, daySnap] = await uow.readAll([ledgerPath, paths.wallet(uid), paths.walletDay(uid)]);
    if (entry?.exists) {
      const r = entry.data?.['result'] as EarnResult | undefined;
      if (!r) throw new AppError('CONFLICT');
      return { ...r, fresh: false };
    }
    const day = readDay(daySnap?.data, utcDay(nowMs));
    const earnedToday = day.earned[source] ?? 0;
    const granted = Math.max(0, Math.min(amount, cap - earnedToday));
    const before = balanceOf(wallet?.data, cat);
    const result: EarnResult = { gems: before + granted, granted, fresh: true };
    if (granted > 0) {
      day.earned[source] = earnedToday + granted;
      uow.set(paths.wallet(uid), { gems: result.gems, updatedAt: nowMs }, { merge: true });
      uow.set(paths.walletDay(uid), { ...day });
    } else if (amount > 0) {
      env.log.warn('gem earning over the daily cap', { uid, source, amount, cap });
    }
    uow.set(ledgerPath, { kind: 'earn', ref: source, delta: granted, asked: amount, balance: result.gems, at: nowMs, result });
    uow.commit();
    return result;
  });
}

/**
 * Credit gems inside another transaction (a server-made reward, e.g. rewards-claim). The caller has
 * already read nothing of the wallet; this reads it, so call it before the caller's first write.
 */
export async function creditInTransaction(
  uow: UnitOfWork, cat: CatalogueDoc, uid: string, gems: number, ref: string, nowMs: number,
): Promise<{ balance: number; apply: () => void }> {
  const wallet = await uow.read(paths.wallet(uid));
  const balance = balanceOf(wallet.data, cat) + gems;
  return {
    balance,
    apply: () => {
      if (gems <= 0) return;
      uow.set(paths.wallet(uid), { gems: balance, updatedAt: nowMs }, { merge: true });   // keeps what was bought today
      uow.set(paths.ledger(uid, 'g_' + ref), { kind: 'reward', ref, delta: gems, balance, at: nowMs });
    },
  };
}

/** A new player's wallet (player-create). */
export function startingWallet(cat: CatalogueDoc, nowMs: number): WalletDoc {
  return { gems: cat.startingGems, updatedAt: nowMs };
}
