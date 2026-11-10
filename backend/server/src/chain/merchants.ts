/**
 * Market Town merchants (plan 15 §13.1): NPCs that are agents with wallets. Each is its own NEAR account with its
 * own FARM. Every tick a merchant looks at its own stock (tokens it holds) and demand (its item's sales in the last
 * 24 h), sets a buy price and a sell price from them, keeps a standing offer to buy each of its goods in lots, and
 * lists what it holds. Its FARM is only what it started with plus what it earned; its key can move only its own
 * tokens and FARM. The economy moves without a patch: prices follow stock and demand.
 */
import { Account, JsonRpcProvider, type KeyPairString, teraToGas } from 'near-api-js';
import type { Logger } from '../../../game/src/core/logger';
import type { Store } from '../../../game/src/infra/store';
import type { PrismaClient } from '../generated/prisma/client';
import { needsNote, type MerchantVoice, type Voice } from './merchantVoice';

/** One FARM is worth this many island coins when a merchant prices goods (0.01 FARM a coin). */
export const COINS_PER_FARM = 100;
const E18 = 10n ** 18n;
const DAY_MS = 86_400_000;

export interface MerchantGood {
  /** Catalogue id (`golden_wheat` for golden goods). */
  readonly item: string;
  /** Island coin price (GameplayRules.PriceOf). */
  readonly base: number;
  /** Units per lot the merchant buys. */
  readonly lot: number;
  /** Units the merchant is happy to hold: below it pays more, above it pays less. */
  readonly target: number;
}

export interface MerchantDef {
  readonly name: string;
  readonly display: string;
  readonly role: string;
  readonly goods: readonly MerchantGood[];
}

const crop = (item: string, base: number, target = 300): MerchantGood => ({ item, base, lot: 100, target });
const golden = (c: string, base: number): MerchantGood => ({ item: `golden_${c}`, base: base * 25, lot: 1, target: 5 });

/** The cast (prices: GameplayRules.PriceOf). */
export const MERCHANTS: readonly MerchantDef[] = [
  { name: 'bea', display: 'Bea', role: 'Baker', goods: [crop('wheat', 5), crop('maize', 7), crop('flour', 16, 200), crop('croissant', 37, 100)] },
  { name: 'ruth', display: 'Ruth', role: 'Rancher', goods: [crop('egg', 10), crop('milk', 15), crop('meat', 30, 150), crop('yogurt', 43, 100)] },
  {
    name: 'tomas', display: 'Tomas', role: 'Trader',
    goods: [crop('beans', 6), crop('pumpkin', 11), crop('cabbage', 10), crop('tomato', 16), crop('sunflower', 18), crop('fish', 14), crop('feed', 20, 200)],
  },
  { name: 'ivy', display: 'Ivy', role: 'Collector', goods: [golden('wheat', 5), golden('maize', 7), golden('pumpkin', 11), golden('tomato', 16), golden('sunflower', 18)] },
];

export interface GoodState {
  readonly stock: number;
  /** The item's sales on the market in the last 24 h (anyone's). */
  readonly sales24h: number;
}

export interface Quote {
  /** yocto-FARM per unit the merchant pays / asks. */
  readonly buyPerUnit: bigint;
  readonly sellPerUnit: bigint;
  /** Why, in plain words (the Merchants page shows it). */
  readonly why: string;
}

/**
 * Pure pricing: base × sqrt(target / stock) × (1 + 10 % per recent sale, up to +50 %), kept within 0.5–2× base;
 * the merchant sells at 25 % above what it pays.
 */
export function quote(g: MerchantGood, s: GoodState): Quote {
  const scarcity = Math.sqrt(g.target / Math.max(s.stock, 1));
  const demand = 1 + 0.1 * Math.min(s.sales24h, 5);
  const factor = Math.min(2, Math.max(0.5, scarcity * demand));
  const buyMilliFarm = Math.max(1, Math.round((g.base * factor * 1000) / COINS_PER_FARM));
  const buyPerUnit = (BigInt(buyMilliFarm) * E18) / 1000n;
  const sellPerUnit = (buyPerUnit * 125n) / 100n;
  const why =
    s.stock < g.target * 0.5 ? (s.sales24h > 0 ? 'Low on stock and it sells: paying more' : 'Low on stock: paying more')
    : s.stock > g.target * 1.5 ? 'Plenty in stock: paying less'
    : s.sales24h >= 3 ? 'Selling well: paying a little more'
    : 'Steady';
  return { buyPerUnit, sellPerUnit, why };
}

/** What a merchant can do on chain (its own account and key). */
export interface MerchantWallet {
  readonly accountId: string;
  farmBalance(): Promise<bigint>;
  makeItemOffer(itemId: string, minQuantity: number, amount: bigint, expiresAtMs: number): Promise<void>;
  list(tokenId: string, price: bigint): Promise<void>;
  ensureMarketStorage(slots: number): Promise<void>;
}

export interface MerchantDeps {
  readonly prisma: PrismaClient;
  readonly store: Store;
  readonly itemsContract: string;
  readonly wallets: ReadonlyMap<string, MerchantWallet>;
  readonly nowMs: () => number;
  /** NEAR AI Cloud writes each price move up in the merchant's voice; unset (no key): the fixed reason labels. */
  readonly voice?: MerchantVoice;
  /** Notes asked for per tick across all merchants (credits are limited). Default 12. */
  readonly maxNotesPerTick?: number;
}

/** One tick's budget of model calls; a failed call stops the rest of the tick (the service is likely down). */
interface NoteBudget {
  left: number;
}

interface Held {
  token_id: string;
  item_id: string;
  quantity: string;
  listed_price: string | null;
}

/** `merchant-tick`: every merchant looks at its stock and the market, then updates its offers and listings. */
export async function merchantTick(deps: MerchantDeps, log: Logger): Promise<number> {
  let acted = 0;
  const budget: NoteBudget = { left: deps.maxNotesPerTick ?? 12 };
  for (const m of MERCHANTS) {
    const wallet = deps.wallets.get(m.name);
    if (!wallet) continue;
    try {
      acted += await tickOne(deps, m, wallet, log, budget);
    } catch (e) {
      log.warn('merchant tick failed', { merchant: m.name, error: String(e) });
    }
  }
  return acted;
}

interface MerchantLog {
  goods?: { item: string; note?: Voice }[];
  history?: Record<string, { at: number; buy: string }[]>;
}

async function tickOne(deps: MerchantDeps, m: MerchantDef, w: MerchantWallet, log: Logger, budget: NoteBudget): Promise<number> {
  const now = deps.nowMs();
  const path = `merchants/${m.name}`;
  const before = (await deps.store.read(path)).data as MerchantLog | undefined;
  const items = m.goods.map((g) => g.item);
  const held = await deps.prisma.$queryRaw<Held[]>`
    SELECT t.token_id, t.item_id, t.quantity::text, l.price::text AS listed_price
    FROM chain.tokens t
    LEFT JOIN chain.listings l ON l.contract_id = t.contract_id AND l.token_id = t.token_id AND l.seller_id = t.owner_id
    WHERE t.contract_id = ${deps.itemsContract} AND t.owner_id = ${w.accountId} AND t.burned_at IS NULL AND t.item_id = ANY(${items})`;
  const sales = await deps.prisma.$queryRaw<{ item_id: string; n: bigint }[]>`
    SELECT item_id, count(*) AS n FROM chain.sales
    WHERE contract_id = ${deps.itemsContract} AND item_id = ANY(${items}) AND sold_at > ${new Date(now - DAY_MS)}
    GROUP BY item_id`;
  const offers = await deps.prisma.$queryRaw<{ item_id: string; amount: string; min_quantity: string | null }[]>`
    SELECT item_id, amount::text, min_quantity::text FROM chain.offers
    WHERE contract_id = ${deps.itemsContract} AND buyer_id = ${w.accountId} AND status = 'open' AND expires_at > ${new Date(now + 6 * 3_600_000)}`;

  let farm = await w.farmBalance();
  const report: Record<string, unknown>[] = [];
  let acted = 0;
  // slots: one per open offer and listing the merchant will have
  await w.ensureMarketStorage(m.goods.length + held.length + 2);

  for (const g of m.goods) {
    const mine = held.filter((h) => h.item_id === g.item);
    const stock = mine.reduce((a, h) => a + Number(h.quantity), 0);
    const s = { stock, sales24h: Number(sales.find((x) => x.item_id === g.item)?.n ?? 0) };
    const q = quote(g, s);

    // buy side: one standing offer for a lot (a new offer replaces the old one on chain, refunding it)
    const want = q.buyPerUnit * BigInt(g.lot);
    const open = offers.find((o) => o.item_id === g.item);
    const drift = open ? Math.abs(Number((BigInt(open.amount) * 1000n) / (want === 0n ? 1n : want)) - 1000) : Infinity;
    const full = stock >= g.target * 2;
    if (!full && drift > 50) {
      if (farm + (open ? BigInt(open.amount) : 0n) >= want) {
        await w.makeItemOffer(g.item, g.lot, want, now + 3 * DAY_MS);
        farm -= want - (open ? BigInt(open.amount) : 0n);
        acted++;
      }
    }
    // sell side: everything held, listed at the sell price
    for (const h of mine) {
      const price = q.sellPerUnit * BigInt(h.quantity);
      const listed = h.listed_price ? BigInt(h.listed_price.split('.')[0]!) : null;
      if (listed === null || listed * 100n < price * 95n || listed * 100n > price * 105n) {
        await w.list(h.token_id, price);
        acted++;
      }
    }
    // the merchant's note on the move (words only: the price above is already set), kept until price or reason change
    let note = before?.goods?.find((x) => x.item === g.item)?.note;
    if (note && needsNote(note, q.buyPerUnit, q.why)) note = undefined;
    if (!note && deps.voice && budget.left > 0) {
      budget.left--;
      const last = before?.history?.[g.item]?.at(-1)?.buy;
      const fresh = await deps.voice.note({
        merchant: m.name, display: m.display, role: m.role, item: g.item, stock, target: g.target, sales24h: s.sales24h,
        ...(last ? { oldBuy: BigInt(last) } : {}), newBuy: q.buyPerUnit, sell: q.sellPerUnit, why: q.why, wantsMore: !full,
      });
      if (fresh) note = fresh;
      else budget.left = 0;
    }
    report.push({
      item: g.item, lot: g.lot, stock, sales24h: s.sales24h, buyPerUnit: q.buyPerUnit.toString(), sellPerUnit: q.sellPerUnit.toString(), why: q.why, wantsMore: !full,
      ...(note ? { note } : {}),
    });
  }

  // the merchant's log: what it saw and set, with a price history for the Merchants page
  const history: Record<string, { at: number; buy: string }[]> = { ...(before?.history ?? {}) };
  for (const r of report) {
    const item = r['item'] as string;
    history[item] = [...(history[item] ?? []), { at: now, buy: r['buyPerUnit'] as string }].slice(-72);
  }
  await deps.store.writeBatch([
    { path, data: { name: m.name, display: m.display, role: m.role, account: w.accountId, farm: farm.toString(), goods: report, history, at: now } },
  ]);
  log.info('merchant tick', { merchant: m.name, acted });
  return acted;
}

/** A merchant's wallet on chain (near-api-js), signing with its own key. */
export function nearMerchantWallet(rpcUrl: string, accountId: string, privateKey: string, farm: string, items: string, market: string): MerchantWallet {
  const provider = new JsonRpcProvider({ url: rpcUrl });
  const account = new Account(accountId, provider, privateKey as KeyPairString);
  const view = async <T>(contractId: string, method: string, args: Record<string, unknown>) => (await provider.callFunction({ contractId, method, args })) as T;
  return {
    accountId,
    async farmBalance() {
      return BigInt((await view<string>(farm, 'ft_balance_of', { account_id: accountId })) ?? '0');
    },
    async makeItemOffer(itemId, minQuantity, amount, expiresAtMs) {
      await account.callFunction({
        contractId: farm,
        methodName: 'ft_transfer_call',
        args: { receiver_id: market, amount: amount.toString(), msg: JSON.stringify({ offer: { nft_contract_id: items, item_id: itemId, min_quantity: minQuantity, expires_at_ms: expiresAtMs } }) },
        deposit: 1n,
        gas: teraToGas('100'),
      });
    },
    async list(tokenId, price) {
      await account.callFunction({
        contractId: items,
        methodName: 'nft_approve',
        args: { token_id: tokenId, account_id: market, msg: JSON.stringify({ price: price.toString() }) },
        deposit: 2_000_000_000_000_000_000_000n,
        gas: teraToGas('100'),
      });
    },
    async ensureMarketStorage(slots) {
      const have = BigInt((await view<string>(market, 'storage_balance_of', { account_id: accountId })) ?? '0');
      const need = 10n ** 22n * BigInt(slots);
      if (have >= need) return;
      await account.callFunction({ contractId: market, methodName: 'storage_deposit', args: {}, deposit: need - have, gas: teraToGas('30') });
    },
  };
}

/** The Merchants page: every merchant's last log (public). */
export async function merchantsPage(store: Store): Promise<unknown[]> {
  const rows = await store.query({ collection: 'merchants', where: [], limit: 20 });
  return rows.map((r) => r.data);
}
