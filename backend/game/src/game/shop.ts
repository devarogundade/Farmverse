import { InventoryDoc, ShopDoc, type BoostId, type CatalogueDoc, type ChestId } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import type { Env } from './services';
import { checkChest, islandProgress } from './goals';
import { creditInTransaction, utcDay } from './wallet';

/**
 * The shop beyond gems (money module 2): boosts held and used, the free coin gift, the daily chests
 * and the starter pack's window. All server-written:
 *
 *   players/{uid}/state/inventory  InventoryDoc — boosts held, and until when each one runs
 *   players/{uid}/state/shop       ShopDoc — next gift time, starter pack end, chest days
 *
 * Coins are still the island's (the client adds them); gems and boosts land here.
 */

const MINUTE_MS = 60_000;
const HOUR_MS = 60 * MINUTE_MS;

export function readInventory(data: Record<string, unknown> | undefined, nowMs: number): InventoryDoc {
  if (!data || legacyDoc('inventory', data)) return { boosts: {}, activeUntil: {}, updatedAt: nowMs };
  const parsed = InventoryDoc.safeParse(data);
  if (!parsed.success) throw corrupt('inventory', parsed.error);
  return { boosts: { ...parsed.data.boosts }, activeUntil: { ...parsed.data.activeUntil }, updatedAt: parsed.data.updatedAt };
}

/** Add boosts to an inventory (a purchase, a chest). */
export function addBoosts(inventory: InventoryDoc, boosts: Readonly<Record<string, number>>, nowMs: number): InventoryDoc {
  const next = { ...inventory, boosts: { ...inventory.boosts }, updatedAt: nowMs };
  for (const [id, n] of Object.entries(boosts)) if (n > 0) next.boosts[id] = (next.boosts[id] ?? 0) + n;
  return next;
}

/** The shop doc, made up for a player who has none yet (starter window from when they joined). */
export function readShop(data: Record<string, unknown> | undefined, playerCreatedAt: number, cat: CatalogueDoc, nowMs: number): ShopDoc {
  if (!data || legacyDoc('shop', data)) return newShop(cat, playerCreatedAt || nowMs, nowMs);
  const parsed = ShopDoc.safeParse(data);
  if (!parsed.success) throw corrupt('shop', parsed.error);
  return { ...parsed.data, chestDay: { ...parsed.data.chestDay } };
}

export function newShop(cat: CatalogueDoc, joinedAt: number, nowMs: number): ShopDoc {
  return { nextGiftAt: 0, starterEndsAt: joinedAt + cat.starterPack.windowHours * HOUR_MS, chestDay: {}, updatedAt: nowMs };
}

/** A repeated key returns the first answer (the ledger keeps it). */
async function replay<T>(uow: UnitOfWork, path: string): Promise<T | undefined> {
  const entry = await uow.read(path);
  if (!entry.exists) return undefined;
  const r = entry.data?.['result'] as T | undefined;
  if (!r) throw new AppError('CONFLICT');
  return { ...r, fresh: false };
}

/** `boost-use`: spend one boost; a timed one runs from now (or from when the running one ends). */
export async function useBoost(env: Env, uid: string, boostId: BoostId, key: string): Promise<{ left: number; activeUntil: number; fresh: boolean }> {
  const cat = await catalogue(env);
  const def = cat.boosts[boostId];
  if (!def) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'b_' + key);
    const seen = await replay<{ left: number; activeUntil: number; fresh: boolean }>(uow, ledgerPath);
    if (seen) return seen;
    const inventory = readInventory((await uow.read(paths.inventory(uid))).data, nowMs);
    const held = inventory.boosts[boostId] ?? 0;
    if (held <= 0) throw new AppError('BOOST_NONE');
    inventory.boosts[boostId] = held - 1;
    let activeUntil = 0;
    if (def.minutes > 0) {
      activeUntil = Math.max(nowMs, inventory.activeUntil[boostId] ?? 0) + def.minutes * MINUTE_MS;
      inventory.activeUntil[boostId] = activeUntil;
    }
    inventory.updatedAt = nowMs;
    const result = { left: held - 1, activeUntil, fresh: true };
    uow.set(paths.inventory(uid), inventory);
    uow.set(ledgerPath, { kind: 'boost', ref: boostId, delta: -1, at: nowMs, result });
    uow.commit();
    return result;
  });
}

/** `shop-claimGift`: the free coins, once every `freeGift.everyMinutes`. */
export async function claimGift(env: Env, uid: string, key: string): Promise<{ coins: number; nextGiftAt: number; fresh: boolean }> {
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'f_' + key);
    const seen = await replay<{ coins: number; nextGiftAt: number; fresh: boolean }>(uow, ledgerPath);
    if (seen) return seen;
    const [shopSnap, playerSnap] = await uow.readAll([paths.shop(uid), paths.player(uid)]);
    const shop = readShop(shopSnap?.data, Number(playerSnap?.data?.['createdAt'] ?? 0), cat, nowMs);
    if (nowMs < shop.nextGiftAt) throw new AppError('GIFT_NOT_READY', { readyAt: shop.nextGiftAt });
    const result = { coins: cat.freeGift.coins, nextGiftAt: nowMs + cat.freeGift.everyMinutes * MINUTE_MS, fresh: true };
    uow.set(paths.shop(uid), { ...shop, nextGiftAt: result.nextGiftAt, updatedAt: nowMs });
    uow.set(ledgerPath, { kind: 'gift', ref: 'free_gift', coins: result.coins, at: nowMs, result });
    uow.commit();
    return result;
  });
}

export interface ChestResult {
  coins: number;
  gems: number;
  boosts: Record<string, number>;
  fresh: boolean;
}

/**
 * `shop-claimChest`: today's chest, once per UTC day. Its gems go to the wallet and its boosts to
 * the inventory here; the coins are for the island. The day's orders / daily goals are checked on
 * the server's copy of the island (mirrored by the game just before it asks).
 */
export async function claimChest(env: Env, uid: string, chest: ChestId, key: string): Promise<ChestResult> {
  const cat = await catalogue(env);
  const contents = cat.chests[chest];
  if (!contents) throw new AppError('INVALID_ARGUMENT');
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'c_' + key);
    const seen = await replay<ChestResult>(uow, ledgerPath);
    if (seen) return seen;
    const [shopSnap, playerSnap, invSnap, islandSnap] = await uow.readAll([paths.shop(uid), paths.player(uid), paths.inventory(uid), paths.island(uid)]);
    const today = utcDay(nowMs);
    const shop = readShop(shopSnap?.data, Number(playerSnap?.data?.['createdAt'] ?? 0), cat, nowMs);
    if (shop.chestDay[chest] === today) throw new AppError('CHEST_CLAIMED');
    checkChest(islandProgress(islandSnap?.data), chest, cat.orderChestOrders, nowMs);
    const credit = await creditInTransaction(uow, cat, uid, contents.gems, 'chest_' + chest + '_' + key, nowMs);
    const inventory = addBoosts(readInventory(invSnap?.data, nowMs), contents.boosts, nowMs);
    const result: ChestResult = { coins: contents.coins, gems: contents.gems, boosts: { ...contents.boosts }, fresh: true };
    shop.chestDay[chest] = today;
    uow.set(paths.shop(uid), { ...shop, updatedAt: nowMs });
    if (Object.keys(contents.boosts).length > 0) uow.set(paths.inventory(uid), inventory);
    credit.apply();
    uow.set(ledgerPath, { kind: 'chest', ref: chest, at: nowMs, result });
    uow.commit();
    return result;
  });
}
