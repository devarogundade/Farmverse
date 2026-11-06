import { createHash } from 'node:crypto';
import { AppError, fail } from '../../../game/src/core/errors';
import type { Logger } from '../../../game/src/core/logger';
import { grantEntitlement, type Entitlement } from '../../../game/src/game/entitlements';
import { sync as syncIsland, type GrantsDoc, type IslandDeps, type IslandDoc } from '../../../game/src/game/islands';
import type { Env } from '../../../game/src/game/services';
import { EngineRefusal, type ExchangeOp, type IslandSave } from '../../../game/src/infra/islandEngine';
import { paths } from '../../../game/src/infra/paths';
import type { ChainItem, ChainReader } from './nearChain';
import type { MintVoucher, TokenKind, VoucherSigner } from './voucher';

export const VOUCHER_TTL_MS = 30 * 60_000;
/** The refund check runs this long after expiry, when the chain can no longer accept the voucher. */
export const EXPIRY_GRACE_MS = 2 * 60_000;
const ATTEMPTS = 3;
const BOOSTS = new Set(['shield', 'fert', 'rush', 'instant', 'basket', 'nightshift', 'lunch']);
/** The mint fee: this share of the value minted, in island coins (a coin sink; plan 15 §9 D10). */
export const MINT_FEE_BPS = 200;
export const MINT_FEE_MIN = 5;
/** Goods mint in lots of at least this many units; golden goods (rare) one at a time. */
export const MINT_LOT_MIN = 10;
export const isGolden = (item: string) => item.startsWith('golden_');

export interface MintLimits {
  /** Market value (coins) a player may mint per UTC day. */
  readonly dailyValueCap: number;
  /** Hours since the player started before they may mint. */
  readonly minAccountHours: number;
}

export interface MintDeps {
  readonly env: Env;
  readonly island: IslandDeps;
  readonly chain: ChainReader;
  readonly signer: VoucherSigner;
  readonly itemsContract: string;
  readonly limits: MintLimits;
  /** Books the refund check for a voucher. */
  readonly scheduleExpiry: (uid: string, tokenId: string, atMs: number) => Promise<void>;
}

export interface MintRequest {
  readonly kind: 'good' | 'boost' | 'building' | 'land';
  readonly item?: string;
  readonly quantity?: number;
  readonly buildingUid?: number;
  readonly key: string;
}

/** `players/{uid}/mints/{tokenId}`. */
export interface MintDoc {
  tokenId: string;
  status: 'issued' | 'minted' | 'refunded';
  voucher: MintVoucher;
  signer: string;
  signature: string;
  /** What gives the items back (and the fee) if the voucher is never used. */
  refund: { ops: ExchangeOp[]; boosts?: Record<string, number> };
  fee?: number;
  value: number;
  createdAt: number;
  updatedAt: number;
}

export interface MintResponse {
  tokenId: string;
  voucher: MintVoucher;
  signer: string;
  signature: string;
  expiresAtMs: number;
  state?: string;
  rulesVersion?: number;
  fee?: number;
}

/** A deterministic token id per player and request key: a retried request can never take twice. */
export function tokenIdFor(uid: string, key: string): string {
  return `g-${createHash('sha256').update(`${uid}:${key}`).digest('hex').slice(0, 30)}`;
}

/** `chicken_coop` → `Chicken coop`. */
export function titleOf(item: string, quantity?: number): string {
  const name = item.replace(/_/g, ' ');
  const title = name.charAt(0).toUpperCase() + name.slice(1);
  return (quantity && quantity > 1 ? `${title} ×${quantity}` : title).slice(0, 64);
}

const utcDay = (ms: number) => new Date(ms).toISOString().slice(0, 10);

function refusalToError(e: EngineRefusal): AppError {
  if (e.code === 'not_enough') return new AppError('MINT_NOT_ENOUGH', { have: Number(/only (\d+)/.exec(e.message)?.[1] ?? 0) });
  if (e.code === 'no_coins') return new AppError('MINT_NO_COINS', { fee: Number(/fee of (\d+)/.exec(e.message)?.[1] ?? 0) });
  if (['not_empty', 'busy', 'core_building', 'under_construction', 'ruined'].includes(e.code)) {
    return new AppError('MINT_BUILDING_BUSY', { reason: e.code });
  }
  if (e.code === 'no_building') return new AppError('NOT_FOUND');
  return new AppError('MINT_NOT_ALLOWED', {}, { cause: e });
}

function islandDocFrom(before: IslandDoc, state: IslandSave, nextWake: number, summary: IslandDoc['summary'], nowMs: number): IslandDoc {
  return {
    ...before,
    rev: before.rev + 1,
    rulesVersion: state.rulesVersion,
    simAtMs: Math.round(state.simAt * 1000),
    state: JSON.stringify(state),
    nextWakeMs: Math.round(nextWake * 1000),
    ...(summary ? { summary } : {}),
    updatedAt: nowMs,
  };
}

/**
 * `chain-mint`: take items off the island and sign a voucher for the wallet to mint them.
 * The island change and the mint record commit together; the client loads the returned island.
 */
export async function mint(deps: MintDeps, uid: string, accountId: string, req: MintRequest, log: Logger): Promise<MintResponse> {
  const { env } = deps;
  const tokenId = tokenIdFor(uid, req.key);

  for (let attempt = 0; attempt < ATTEMPTS; attempt++) {
    const nowMs = env.clock.nowMs();
    const existing = await env.store.read(paths.mint(uid, tokenId));
    const islandSnap = await env.store.read(paths.island(uid));
    const island = islandSnap.exists ? (islandSnap.data as unknown as IslandDoc) : undefined;
    if (existing.exists) {
      // The same request again (a lost reply): same voucher, and the island as it is now.
      const m = existing.data as unknown as MintDoc;
      return { tokenId, voucher: m.voucher, signer: m.signer, signature: m.signature, expiresAtMs: m.voucher.expires_at_ms, fee: m.fee ?? 0, ...(island ? { state: island.state, rulesVersion: island.rulesVersion } : {}) };
    }
    if (!island) fail('NOT_FOUND');
    if (island.audit) fail('MINT_LOCKED');

    const player = await env.store.read(paths.player(uid));
    const startedAt = (player.data?.['createdAt'] as number | undefined) ?? nowMs;
    const hours = (nowMs - startedAt) / 3_600_000;
    if (hours < deps.limits.minAccountHours) fail('MINT_TOO_SOON', { hours: Math.floor(hours) });

    // What leaves, and what it's worth.
    let kind: TokenKind;
    let itemId: string;
    let quantity: number;
    let props = '{}';
    let value: number;
    let refund: MintDoc['refund'];
    let next: { state: IslandSave; nextWake: number; summary: IslandDoc['summary'] } | undefined;
    let boostsAfter: Record<string, number> | undefined;
    let fee = 0;

    if (req.kind === 'boost') {
      if (!req.item || !BOOSTS.has(req.item) || !req.quantity) fail('MINT_NOT_ALLOWED');
      const inv = await env.store.read(paths.inventory(uid));
      const boosts = { ...((inv.data?.['boosts'] as Record<string, number> | undefined) ?? {}) };
      const have = boosts[req.item] ?? 0;
      if (have < req.quantity) fail('MINT_NOT_ENOUGH', { have });
      boosts[req.item] = have - req.quantity;
      boostsAfter = boosts;
      kind = 'boost';
      itemId = req.item;
      quantity = req.quantity;
      value = 0; // boosts have no market price; they count against nothing but the island's stock
      refund = { ops: [], boosts: { [req.item]: req.quantity } };
    } else {
      if (!deps.island.engine.exchange) throw new Error('island engine has no exchange');
      if (req.kind === 'good' && req.item && req.quantity) {
        const min = isGolden(req.item) ? 1 : MINT_LOT_MIN;
        if (req.quantity < min) fail('MINT_LOT_TOO_SMALL', { min });
      }
      const ops: ExchangeOp[] = [
        req.kind === 'good'
          ? req.item && req.quantity
            ? { op: 'take', item: req.item, n: req.quantity }
            : fail('INVALID_ARGUMENT')
          : req.buildingUid !== undefined
            ? { op: 'pickUp', uid: req.buildingUid }
            : fail('INVALID_ARGUMENT'),
        { op: 'fee', n: MINT_FEE_MIN, bps: MINT_FEE_BPS },
      ];
      let ex;
      try {
        ex = await deps.island.engine.exchange(JSON.parse(island.state) as IslandSave, Math.max(nowMs, island.simAtMs) / 1000, ops);
      } catch (e) {
        if (e instanceof EngineRefusal && e.status === 422) throw refusalToError(e);
        throw e;
      }
      const done = ex.done[0]!;
      fee = ex.done.find((d) => d.op === 'fee')?.n ?? 0;
      const giveFeeBack: ExchangeOp[] = fee > 0 ? [{ op: 'credit', n: fee }] : [];
      if (req.kind === 'good') {
        kind = 'good';
        itemId = done.item;
        quantity = done.n;
        value = done.n * done.unitValue;
        refund = { ops: [{ op: 'give', item: done.item, n: done.n }, ...giveFeeBack] };
      } else {
        kind = done.id === 'field' ? 'land' : 'building';
        if (kind !== req.kind) fail('MINT_NOT_ALLOWED');
        itemId = done.id;
        quantity = 1;
        props = JSON.stringify({ level: done.level, price: done.price });
        value = done.price;
        refund = { ops: [{ op: 'hold', id: done.id, level: done.level, price: done.price }, ...giveFeeBack] };
      }
      next = { state: ex.state, nextWake: ex.nextWake, summary: ex.summary };
    }

    const today = utcDay(nowMs);
    const voucher: MintVoucher = {
      contract_id: deps.itemsContract,
      token_id: tokenId,
      receiver_id: accountId,
      kind,
      item_id: itemId,
      quantity,
      props,
      title: titleOf(itemId, quantity),
      media: `items/${itemId}.png`,
      expires_at_ms: nowMs + VOUCHER_TTL_MS,
    };
    const signature = deps.signer.sign(voucher);
    const record: MintDoc = { tokenId, status: 'issued', voucher, signer: deps.signer.publicKey, signature, refund, value, fee, createdAt: nowMs, updatedAt: nowMs };
    const newIsland = next ? islandDocFrom(island, next.state, next.nextWake, next.summary, nowMs) : undefined;

    const outcome = await env.store.runTransaction(async (tx) => {
      const now = await tx.get(paths.island(uid));
      const dup = await tx.get(paths.mint(uid, tokenId));
      const daySnap = await tx.get(paths.mintDay(uid));
      const inv = boostsAfter ? await tx.get(paths.inventory(uid)) : undefined;
      if (dup.exists) return 'retry' as const;
      if (newIsland && Number((now.data as unknown as IslandDoc | undefined)?.rev ?? 0) !== island.rev) return 'retry' as const;
      const day = daySnap.data as { day?: string; value?: number } | undefined;
      const spent = day?.day === today ? (day.value ?? 0) : 0;
      if (spent + value > deps.limits.dailyValueCap) {
        return { limit: deps.limits.dailyValueCap, left: Math.max(0, deps.limits.dailyValueCap - spent) };
      }
      if (boostsAfter && inv) {
        const have = ((inv.data?.['boosts'] as Record<string, number> | undefined) ?? {})[itemId] ?? 0;
        if (have < quantity) return 'retry' as const;
        tx.set(paths.inventory(uid), { boosts: boostsAfter, updatedAt: nowMs }, { merge: true });
      }
      if (newIsland) tx.set(paths.island(uid), newIsland as unknown as Record<string, unknown>);
      tx.set(paths.mint(uid, tokenId), record as unknown as Record<string, unknown>);
      tx.set(paths.mintDay(uid), { day: today, value: spent + value, updatedAt: nowMs });
      return 'ok' as const;
    });
    if (outcome === 'retry') {
      log.info('mint raced; retrying', { uid, attempt });
      continue;
    }
    if (outcome !== 'ok') fail('MINT_LIMIT', outcome);

    await deps.scheduleExpiry(uid, tokenId, voucher.expires_at_ms + EXPIRY_GRACE_MS);
    log.info('mint voucher issued', { uid, tokenId, kind, itemId, quantity, value, fee });
    return {
      tokenId,
      voucher,
      signer: deps.signer.publicKey,
      signature,
      expiresAtMs: voucher.expires_at_ms,
      fee,
      ...(newIsland ? { state: newIsland.state, rulesVersion: newIsland.rulesVersion } : {}),
    };
  }
  fail('CONFLICT');
}

/**
 * `chain-pendingMints`: vouchers this player was given that the wallet can still sign ("Finish minting").
 * A voucher whose token is already on chain, or that expired, is not offered.
 */
export async function pendingMints(
  deps: MintDeps, uid: string,
): Promise<{ mints: Omit<MintResponse, 'state' | 'rulesVersion' | 'fee'>[]; spentToday: number; dailyCap: number; feeBps: number; feeMin: number }> {
  const nowMs = deps.env.clock.nowMs();
  const day = (await deps.env.store.read(paths.mintDay(uid))).data as { day?: string; value?: number } | undefined;
  const spentToday = day?.day === utcDay(nowMs) ? (day.value ?? 0) : 0;
  const rows = await deps.env.store.query({
    collection: `players/${uid}/mints`,
    where: [['status', '==', 'issued']],
    limit: 50,
  });
  const live = rows
    .map((r) => r.data as unknown as MintDoc)
    .filter((m) => m.voucher.expires_at_ms > nowMs + 60_000)
    .sort((a, b) => b.createdAt - a.createdAt)
    .slice(0, 20);
  const out = [];
  for (const m of live) {
    if (await deps.chain.wasMinted(m.tokenId)) continue; // signed already; the expiry job settles it
    out.push({ tokenId: m.tokenId, voucher: m.voucher, signer: m.signer, signature: m.signature, expiresAtMs: m.voucher.expires_at_ms });
  }
  return { mints: out, spentToday, dailyCap: deps.limits.dailyValueCap, feeBps: MINT_FEE_BPS, feeMin: MINT_FEE_MIN };
}

export type ExpiryOutcome = 'missing' | 'settled' | 'early' | 'minted' | 'refunded';

/**
 * The refund check (a delayed job after the voucher expired): if the token was never minted,
 * the items are owed back — goods and buildings as an island grant, boosts straight away.
 */
export async function expireMint(deps: MintDeps, uid: string, tokenId: string, log: Logger): Promise<ExpiryOutcome> {
  const { env } = deps;
  const snap = await env.store.read(paths.mint(uid, tokenId));
  if (!snap.exists) return 'missing';
  const m = snap.data as unknown as MintDoc;
  if (m.status !== 'issued') return 'settled';
  const nowMs = env.clock.nowMs();
  if (nowMs < m.voucher.expires_at_ms + EXPIRY_GRACE_MS) return 'early';

  const minted = await deps.chain.wasMinted(tokenId);
  return env.store.runTransaction(async (tx) => {
    const cur = await tx.get(paths.mint(uid, tokenId));
    if ((cur.data as unknown as MintDoc | undefined)?.status !== 'issued') return 'settled';
    if (minted) {
      tx.set(paths.mint(uid, tokenId), { status: 'minted', updatedAt: nowMs }, { merge: true });
      return 'minted';
    }
    const grants = await tx.get(paths.grants(uid));
    const inv = m.refund.boosts ? await tx.get(paths.inventory(uid)) : undefined;
    if (m.refund.ops.length > 0) {
      const items = (grants.data as unknown as GrantsDoc | undefined)?.items ?? [];
      tx.set(paths.grants(uid), { items: [...items, { id: `refund_${tokenId}`, ops: m.refund.ops, at: nowMs }], updatedAt: nowMs });
    }
    if (m.refund.boosts && inv) {
      const boosts = { ...((inv.data?.['boosts'] as Record<string, number> | undefined) ?? {}) };
      for (const [b, n] of Object.entries(m.refund.boosts)) boosts[b] = (boosts[b] ?? 0) + n;
      tx.set(paths.inventory(uid), { boosts, updatedAt: nowMs }, { merge: true });
    }
    tx.set(paths.mint(uid, tokenId), { status: 'refunded', updatedAt: nowMs }, { merge: true });
    log.info('mint refunded', { uid, tokenId });
    return 'refunded';
  });
}

/** Studio packs: what each redeems for. */
export function packEntitlement(itemId: string, quantity: number): Entitlement | undefined {
  const gems = /^gems_(\d{1,6})$/.exec(itemId);
  if (gems) return { kind: 'bundle', contents: { coins: 0, gems: Math.min(100_000, Number(gems[1]) * quantity), boosts: {} } };
  if (itemId === 'farm_pass') return { kind: 'pass' };
  if (itemId === 'builder_pass') return { kind: 'builder' };
  if (itemId === 'starter_pack') return { kind: 'starter' };
  return undefined;
}

/** What crediting a redeemed item does to the island (empty for boosts and packs). */
export function redeemOps(item: ChainItem): ExchangeOp[] {
  if (item.kind === 'good') return [{ op: 'give', item: item.item_id, n: item.quantity }];
  // a Genesis land deed clears the next wild plot (plan 15 §12.3)
  if (item.kind === 'land' && item.item_id === 'deed') return [{ op: 'openPlot' }];
  if (item.kind === 'building' || item.kind === 'land' || item.kind === 'tool') {
    let level = 1;
    let price = 0;
    try {
      const p = JSON.parse(item.props) as { level?: unknown; price?: unknown };
      if (Number.isInteger(p.level) && (p.level as number) >= 1) level = p.level as number;
      if (Number.isInteger(p.price) && (p.price as number) >= 0) price = p.price as number;
    } catch {
      // studio items carry no level: level 1
    }
    return [{ op: 'hold', id: item.item_id, level, price }];
  }
  return [];
}

export interface RedeemResponse {
  tokenId: string;
  kind: string;
  item: string;
  quantity: number;
  fresh: boolean;
  state?: string;
  rulesVersion?: number;
}

/**
 * Credits one redeemed token to a player, once (`redemptions/{tokenId}`): island items as a grant,
 * boosts to the inventory, packs as an entitlement. Used by `chain-redeem` (tx checked on RPC) and by
 * the backstop job (event from the indexer, for redeems nobody reported).
 */
export async function creditRedeem(
  env: Env, uid: string, accountId: string, tokenId: string, item: ChainItem, txHash: string | null, log: Logger,
): Promise<{ fresh: boolean; islandOps: boolean }> {
  const nowMs = env.clock.nowMs();
  const ops = redeemOps(item);
  const fresh = await env.store.runTransaction(async (tx) => {
    const done = await tx.get(paths.redemption(tokenId));
    if (done.exists) return false;
    const grants = ops.length > 0 ? await tx.get(paths.grants(uid)) : undefined;
    const inv = item.kind === 'boost' ? await tx.get(paths.inventory(uid)) : undefined;
    tx.set(paths.redemption(tokenId), { tokenId, uid, accountId, txHash, item: item as unknown as Record<string, unknown>, at: nowMs });
    if (grants) {
      const items = (grants.data as unknown as GrantsDoc | undefined)?.items ?? [];
      tx.set(paths.grants(uid), { items: [...items, { id: `redeem_${tokenId}`, ops, at: nowMs }], updatedAt: nowMs });
    }
    if (inv) {
      const boosts = { ...((inv.data?.['boosts'] as Record<string, number> | undefined) ?? {}) };
      boosts[item.item_id] = (boosts[item.item_id] ?? 0) + item.quantity;
      tx.set(paths.inventory(uid), { boosts, updatedAt: nowMs }, { merge: true });
    }
    return true;
  });
  if (item.kind === 'pack') {
    // Idempotent by ref, so it also repairs a crash between the record above and this grant.
    const grant = packEntitlement(item.item_id, item.quantity);
    if (grant) await grantEntitlement(env, uid, grant, `redeem_${tokenId}`.slice(0, 120));
    else log.error('redeemed pack has no entitlement', { uid, tokenId, item: item.item_id });
  }
  log.info('redeem credited', { uid, tokenId, fresh, kind: item.kind, item: item.item_id });
  return { fresh, islandOps: ops.length > 0 };
}

/** One redeem the indexer saw that no `chain-redeem` call credited. */
export interface UnclaimedRedeem {
  readonly tokenId: string;
  readonly ownerId: string;
  readonly uid: string;
  readonly item: ChainItem;
  readonly txHash: string | null;
}

/**
 * The redeem backstop (every minute): credits burns the indexer saw but nobody reported (the player
 * closed the tab). Owners who never signed in to the game have no island yet and are left for later.
 */
export async function backstopRedeems(env: Env, find: () => Promise<UnclaimedRedeem[]>, log: Logger): Promise<number> {
  let credited = 0;
  for (const r of await find()) {
    try {
      const { fresh } = await creditRedeem(env, r.uid, r.ownerId, r.tokenId, r.item, r.txHash, log);
      if (fresh) credited++;
    } catch (e) {
      log.error('backstop credit failed', { tokenId: r.tokenId, error: String(e) });
    }
  }
  if (credited) log.info('redeem backstop', { credited });
  return credited;
}

/**
 * `chain-redeem`: the wallet burned a token on chain; credit its items to this player once.
 * The transaction is checked on a NEAR RPC node: it succeeded, this wallet signed it, and the
 * items contract logged the redeem.
 */
export async function redeem(deps: MintDeps, uid: string, accountId: string, txHash: string, log: Logger): Promise<RedeemResponse> {
  const { env } = deps;
  const seen = await deps.chain.redeemsIn(txHash, accountId);
  if (seen.length === 0) fail('REDEEM_NOT_FOUND');

  let islandChanged = false;
  const results: Omit<RedeemResponse, 'state' | 'rulesVersion'>[] = [];
  for (const r of seen) {
    const { fresh, islandOps } = await creditRedeem(env, uid, accountId, r.tokenId, r.item, txHash, log);
    if (islandOps) islandChanged = true;
    results.push({ tokenId: r.tokenId, kind: r.item.kind, item: r.item.item_id, quantity: r.item.quantity, fresh });
  }

  // Apply the grants now, while the player is here to load the island.
  let island: { state?: string; rulesVersion?: number } = {};
  if (islandChanged) {
    try {
      const synced = await syncIsland(deps.island, uid);
      island = { state: synced.state, rulesVersion: synced.rulesVersion };
    } catch (e) {
      // No island on the server yet: the items stay owed and arrive with the first sync.
      log.warn('redeem grants left for the next sync', { uid, error: String(e) });
    }
  }
  return { ...results[0]!, ...island };
}
