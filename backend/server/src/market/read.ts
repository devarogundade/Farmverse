import { farmNumber, formatFarm, NFT_TYPES, shortAccount, type NftType } from '../chain/items';
import { Prisma, type PrismaClient } from '../generated/prisma/client';
import { levelFor } from '../../../game/src/game/crew';

/**
 * The marketplace's reads over the `chain` schema (docs 13 §9). Shapes match the wire schemas in
 * web/marketplace/src/api/schemas.ts: prices are yocto-FARM strings, times are epoch ms.
 */

export type Sort = 'newest' | 'price_asc' | 'price_desc' | 'unit_price';

export interface TokenWire {
  token_id: string;
  title: string;
  /** The contract's ItemKind (good, boost, building, land, tool, pack, worker). */
  kind: string;
  owner_id: string;
  metadata: Record<string, unknown>;
  price: string | null;
  price_history: number[];
  events: EventWire[];
}
export interface EventWire {
  kind: string;
  account: string;
  price: string | null;
  at: number;
}
export interface Page<T> {
  items: T[];
  next_cursor: string | null;
  total: number;
}
export interface NoteWire {
  id: string;
  icon: string;
  text: string;
  href: string;
  at: number;
  read: boolean;
}

export interface BrowseQuery {
  type?: NftType;
  q?: string;
  sort?: Sort;
  /** Only this account's tokens, listed or not. Without it: active listings only. */
  owner?: string;
  /** Workers: only this specialty / rarity (props). */
  specialty?: string;
  rarity?: string;
  /** Goods: only this catalogue item. */
  item?: string;
  /** Owner pages: every kind, packs and boosts too (the game's NFT tab); `metadata.type` is null for those. */
  allKinds?: boolean;
  cursor?: string | null;
  limit?: number;
}

interface Row {
  token_id: string;
  title: string;
  kind: string;
  owner_id: string;
  nft_type: NftType;
  item_id: string;
  quantity: string;
  props: Record<string, unknown> | null;
  minted_from: string | null;
  minted_at: string;
  price: string | null;
  /** Farmhands: experience the game counted (workers/{token}). */
  worker_xp?: number | null;
}

export const MAX_LIMIT = 60;
const HISTORY_DAYS = 24;

const isPrimitive = (v: unknown): v is string | number | boolean => ['string', 'number', 'boolean'].includes(typeof v);

/** The metadata the marketplace renders (web/shared/src/nft/metadata.ts `NftMetadata`). */
export function metadataOf(r: Pick<Row, 'nft_type' | 'item_id' | 'quantity' | 'props' | 'minted_from' | 'minted_at' | 'worker_xp'>): Record<string, unknown> {
  const { outfit, traits: nested, ...rest } = r.props ?? {};
  // Farmhands keep speed/carry/stamina under `traits`: shown alongside the flat ones.
  const flat = { ...rest, ...(nested && typeof nested === 'object' && !Array.isArray(nested) ? (nested as Record<string, unknown>) : {}) };
  if (r.nft_type === 'worker' && typeof r.worker_xp === 'number') {
    flat['level'] = levelFor(r.worker_xp);
    flat['xp'] = r.worker_xp;
  }
  const traits = Object.fromEntries(Object.entries(flat).filter(([, v]) => isPrimitive(v)));
  const quantity = Number(r.quantity);
  const m: Record<string, unknown> = { type: r.nft_type, minted_at: Number(r.minted_at) };
  if (r.nft_type === 'worker') m['outfit'] = outfit && typeof outfit === 'object' && !Array.isArray(outfit) ? outfit : {};
  else m['item_id'] = r.item_id;
  if (r.nft_type === 'good' || quantity > 1) m['quantity'] = quantity;
  if (Object.keys(traits).length) m['props'] = traits;
  if (r.minted_from) m['minted_from'] = r.minted_from;
  return m;
}

const wire = (r: Row): TokenWire => ({
  token_id: r.token_id,
  title: r.title,
  kind: r.kind,
  owner_id: r.owner_id,
  metadata: metadataOf(r),
  price: r.price,
  price_history: [],
  events: [],
});

const SELECT = Prisma.sql`
  SELECT t.token_id, t.title, t.kind, t.owner_id, t.nft_type, t.item_id, t.quantity::text AS quantity, t.props, t.minted_from,
         (extract(epoch FROM t.minted_at) * 1000)::bigint::text AS minted_at, l.price::text AS price,
         (SELECT (d.data->>'xp')::int FROM documents d WHERE d.path = 'workers/' || t.token_id) AS worker_xp`;

/** A listing only counts while its seller still owns the token (a transfer kills the approval). */
const LISTING_JOIN = Prisma.sql`l.contract_id = t.contract_id AND l.token_id = t.token_id AND l.seller_id = t.owner_id`;

const escapeLike = (s: string) => s.replace(/[\\%_]/g, (c) => `\\${c}`);

export const parseCursor = (c: string | null | undefined): number => (c && /^\d{1,7}$/.test(c) ? Number(c) : 0);

export class MarketReader {
  constructor(
    private readonly prisma: PrismaClient,
    private readonly items: string,
  ) {}

  /** Active listings, or one account's tokens. Offset-paged: the cursor is the next offset. */
  async browse(q: BrowseQuery): Promise<Page<TokenWire>> {
    const limit = Math.min(Math.max(q.limit ?? 24, 1), MAX_LIMIT);
    const offset = parseCursor(q.cursor);
    const where: Prisma.Sql[] = [Prisma.sql`t.contract_id = ${this.items}`, Prisma.sql`t.burned_at IS NULL`];
    if (!(q.allKinds && q.owner)) where.push(Prisma.sql`t.nft_type IS NOT NULL`);
    if (q.type && NFT_TYPES.includes(q.type)) where.push(Prisma.sql`t.nft_type = ${q.type}`);
    if (q.owner) where.push(Prisma.sql`t.owner_id = ${q.owner}`);
    if (q.specialty) where.push(Prisma.sql`t.props->>'specialty' = ${q.specialty}`);
    if (q.rarity) where.push(Prisma.sql`t.props->>'rarity' = ${q.rarity}`);
    if (q.item) where.push(Prisma.sql`t.item_id = ${q.item}`);
    const needle = q.q?.trim();
    if (needle) {
      const like = `%${escapeLike(needle.slice(0, 64))}%`;
      where.push(Prisma.sql`(t.title ILIKE ${like} OR t.item_id ILIKE ${like} OR t.token_id ILIKE ${like})`);
    }
    const join = q.owner ? Prisma.sql`LEFT JOIN` : Prisma.sql`JOIN`;
    const from = Prisma.sql`FROM chain.tokens t ${join} chain.listings l ON ${LISTING_JOIN} WHERE ${Prisma.join(where, ' AND ')}`;
    const sort = q.sort ?? 'newest';
    const order =
      sort === 'unit_price'
        ? Prisma.sql`(l.price / GREATEST(t.quantity, 1)) ASC NULLS LAST, t.token_id`
        : sort === 'price_asc'
        ? Prisma.sql`l.price ASC NULLS LAST, t.token_id`
        : sort === 'price_desc'
          ? Prisma.sql`l.price DESC NULLS LAST, t.token_id`
          : q.owner
            ? Prisma.sql`t.updated_at DESC, t.token_id`
            : Prisma.sql`l.listed_at DESC, t.token_id`;

    const [rows, count] = await Promise.all([
      this.prisma.$queryRaw<Row[]>`${SELECT} ${from} ORDER BY ${order} LIMIT ${limit} OFFSET ${offset}`,
      this.prisma.$queryRaw<{ n: string }[]>`SELECT count(*)::text AS n ${from}`,
    ]);
    const total = Number(count[0]?.n ?? 0);
    return { items: rows.map(wire), next_cursor: offset + rows.length < total && rows.length > 0 ? String(offset + rows.length) : null, total };
  }

  /** Several tokens by id (saved items); burned ones are left out. */
  async tokens(ids: readonly string[]): Promise<TokenWire[]> {
    if (!ids.length) return [];
    const rows = await this.prisma.$queryRaw<Row[]>`
      ${SELECT} FROM chain.tokens t LEFT JOIN chain.listings l ON ${LISTING_JOIN}
      WHERE t.contract_id = ${this.items} AND t.burned_at IS NULL AND t.nft_type IS NOT NULL AND t.token_id IN (${Prisma.join(ids)})`;
    return rows.map(wire);
  }

  /** One token with its history, the model's floor and price trend, and a few other listings. */
  async token(id: string): Promise<{ token: TokenWire; floor: string | null; related: TokenWire[] } | null> {
    const rows = await this.prisma.$queryRaw<Row[]>`
      ${SELECT} FROM chain.tokens t LEFT JOIN chain.listings l ON ${LISTING_JOIN}
      WHERE t.contract_id = ${this.items} AND t.token_id = ${id} AND t.burned_at IS NULL AND t.nft_type IS NOT NULL`;
    const row = rows[0];
    if (!row) return null;

    // "Same model": the same item, or any worker for workers.
    const sameModel =
      row.nft_type === 'worker' ? Prisma.sql`t.nft_type = 'worker'` : Prisma.sql`t.item_id = ${row.item_id} AND t.nft_type = ${row.nft_type}`;

    const [events, floor, daily, related] = await Promise.all([
      this.prisma.$queryRaw<{ kind: string; account_id: string; price: string | null; at: string }[]>`
        SELECT kind, account_id, price::text AS price, (extract(epoch FROM at) * 1000)::bigint::text AS at
        FROM chain.activity WHERE contract_id = ${this.items} AND token_id = ${id}
        ORDER BY at DESC, block_height DESC, id DESC LIMIT 50`,
      this.prisma.$queryRaw<{ floor: string | null }[]>`
        SELECT min(l.price)::text AS floor FROM chain.tokens t JOIN chain.listings l ON ${LISTING_JOIN}
        WHERE t.contract_id = ${this.items} AND t.burned_at IS NULL AND ${sameModel}`,
      this.prisma.$queryRaw<{ day: number; avg: string }[]>`
        SELECT (CURRENT_DATE - s.sold_at::date)::int AS day, round(avg(s.price))::text AS avg
        FROM chain.sales s JOIN chain.tokens t ON t.contract_id = s.contract_id AND t.token_id = s.token_id
        WHERE s.contract_id = ${this.items} AND s.sold_at >= CURRENT_DATE - ${HISTORY_DAYS - 1}::int AND ${sameModel}
        GROUP BY 1`,
      this.prisma.$queryRaw<Row[]>`
        ${SELECT} FROM chain.tokens t JOIN chain.listings l ON ${LISTING_JOIN}
        WHERE t.contract_id = ${this.items} AND t.burned_at IS NULL AND t.nft_type IS NOT NULL AND t.token_id <> ${id}
        ORDER BY (t.nft_type = ${row.nft_type}) DESC, l.listed_at DESC LIMIT 3`,
    ]);

    return {
      token: {
        ...wire(row),
        events: events.map((e) => ({ kind: e.kind, account: e.account_id, price: e.price, at: Number(e.at) })),
        price_history: dailySeries(daily, HISTORY_DAYS),
      },
      floor: floor[0]?.floor ?? null,
      related: related.map(wire),
    };
  }

  /**
   * The last block the indexer fully applied (null before its first run). Clients that just sent a
   * transaction wait for this to pass the transaction's block before reading again. The newest
   * cursor is the running one; an old cursor from an earlier contract set stays behind.
   */
  async head(): Promise<{ block_height: number | null }> {
    const rows = await this.prisma.$queryRaw<{ h: string }[]>`SELECT block_height::text AS h FROM chain.cursors ORDER BY updated_at DESC LIMIT 1`;
    return { block_height: rows[0] ? Number(rows[0].h) : null };
  }

  /** Chain happenings addressed to an account: its items sold, items sent to it. */
  async notes(account: string, readUpToMs: number): Promise<NoteWire[]> {
    const rows = await this.prisma.$queryRaw<
      { id: string; kind: string; account_id: string; price: string | null; at: string; token_id: string; title: string | null }[]
    >`
      SELECT a.id, a.kind, a.account_id, a.price::text AS price, (extract(epoch FROM a.at) * 1000)::bigint::text AS at, a.token_id, t.title
      FROM chain.activity a LEFT JOIN chain.tokens t ON t.contract_id = a.contract_id AND t.token_id = a.token_id
      WHERE a.counterparty = ${account} AND a.kind IN ('bought', 'sent', 'rented') AND a.contract_id = ${this.items}
      ORDER BY a.at DESC LIMIT 30`;
    return rows.map((r) => {
      const title = r.title ?? r.token_id;
      const at = Number(r.at);
      const who = shortAccount(r.account_id);
      return {
        id: r.id,
        icon: r.kind === 'sent' ? 'Icon_Gift' : 'ResourceBar_Icon_Gold',
        text:
          r.kind === 'bought'
            ? `${who} bought your ${title} for ${formatFarm(r.price ?? '0')} FARM`
            : r.kind === 'rented'
              ? `${who} hired ${title} for ${formatFarm(r.price ?? '0')} FARM`
              : `${who} sent you ${title}`,
        href: `#/token/${encodeURIComponent(r.token_id)}`,
        at,
        read: at <= readUpToMs,
      };
    });
  }
}

export interface DropWire {
  id: string;
  kind: string;
  item_id: string;
  quantity: number;
  title: string;
  media: string;
  props: Record<string, unknown>;
  /** yocto-FARM */
  price: string;
  cap: number | null;
  sold: number;
  per_wallet: number | null;
  starts_at: number;
  ends_at: number | null;
  paused: boolean;
}

/** As web/marketplace/src/api/schemas.ts `OfferWire` (`token_id` is `#<item>` for an offer on any token of an item). */
export interface OfferWire {
  id: string;
  token_id: string;
  item_id: string | null;
  min_quantity: number | null;
  from: string;
  /** yocto-FARM */
  amount: string;
  expires_at: number;
  status: 'open' | 'accepted' | 'cancelled';
}

export interface RentalWire {
  token: TokenWire;
  lender_id: string;
  /** yocto-FARM per day */
  price_per_day: string;
  max_days: number;
  renter_id: string | null;
  /** epoch ms; 0 when not rented */
  rented_until: number;
  /** yocto-FARM waiting for the lender */
  earned: string;
}

export interface ItemStatsWire {
  item_id: string;
  /** Cheapest listing per unit (yocto-FARM), null with no listing. */
  floor_per_unit: string | null;
  listed: number;
  minted: number;
  burned: number;
  /** Units in live tokens. */
  supply: number;
  sales_24h: number;
}

interface OfferRow {
  offer_id: string;
  token_id: string | null;
  item_id: string | null;
  min_quantity: string | null;
  buyer_id: string;
  amount: string;
  expires_at: string;
  status: OfferWire['status'];
}
const offerWire = (r: OfferRow): OfferWire => ({
  id: r.offer_id,
  token_id: r.token_id ?? `#${r.item_id ?? ''}`,
  item_id: r.item_id,
  min_quantity: r.min_quantity === null ? null : Number(r.min_quantity),
  from: r.buyer_id,
  amount: r.amount,
  expires_at: Number(r.expires_at),
  status: r.status,
});
const OFFER_SELECT = Prisma.sql`
  SELECT o.offer_id::text, o.token_id, o.item_id, o.min_quantity::text, o.buyer_id, o.amount::text,
         (extract(epoch FROM o.expires_at) * 1000)::bigint::text AS expires_at, o.status`;

type DropRow = {
  drop_id: string; kind: string; item_id: string; quantity: string; title: string; media: string; props: Record<string, unknown>;
  price: string; cap: string | null; sold: string; per_wallet: number | null; starts_at: string; ends_at: string | null; paused: boolean;
};
type RentalRow = Row & { lender_id: string; price_per_day: string; max_days: number; renter_id: string | null; rented_until: string | null; earned: string };

/** Drops, offers, rentals and per-item stats (plan 15 §5.4, §6.5, §12). */
export class MarketReaderV2 {
  constructor(
    private readonly prisma: PrismaClient,
    private readonly items: string,
  ) {}

  async drops(): Promise<DropWire[]> {
    const rows = await this.prisma.$queryRaw<DropRow[]>`
      SELECT drop_id, kind, item_id, quantity::text, title, media, props, price::text, cap::text, sold::text, per_wallet,
             (extract(epoch FROM starts_at) * 1000)::bigint::text AS starts_at, (extract(epoch FROM ends_at) * 1000)::bigint::text AS ends_at, paused
      FROM chain.drops WHERE contract_id = ${this.items} ORDER BY price ASC, drop_id`;
    return rows.map((r) => ({
      id: r.drop_id,
      kind: r.kind,
      item_id: r.item_id,
      quantity: Number(r.quantity),
      title: r.title,
      media: r.media,
      props: r.props ?? {},
      price: r.price,
      cap: r.cap === null ? null : Number(r.cap),
      sold: Number(r.sold),
      per_wallet: r.per_wallet,
      starts_at: Number(r.starts_at),
      ends_at: r.ends_at === null ? null : Number(r.ends_at),
      paused: r.paused,
    }));
  }

  /** Live offers a token's owner could accept: on the token, and on its item (when it holds enough). */
  async offersForToken(tokenId: string, nowMs: number): Promise<OfferWire[]> {
    const rows = await this.prisma.$queryRaw<OfferRow[]>`
      ${OFFER_SELECT} FROM chain.offers o
      JOIN chain.tokens t ON t.contract_id = o.contract_id AND t.token_id = ${tokenId}
      WHERE o.contract_id = ${this.items} AND o.status = 'open' AND o.expires_at > ${new Date(nowMs)}
        AND (o.token_id = ${tokenId} OR (o.item_id = t.item_id AND (o.min_quantity IS NULL OR t.quantity >= o.min_quantity)))
      ORDER BY o.amount DESC LIMIT 50`;
    return rows.map(offerWire);
  }

  /** Live offers on any token of an item (merchants' buy side, the game's market stall). */
  async offersForItem(itemId: string, nowMs: number): Promise<OfferWire[]> {
    const rows = await this.prisma.$queryRaw<OfferRow[]>`
      ${OFFER_SELECT} FROM chain.offers o
      WHERE o.contract_id = ${this.items} AND o.status = 'open' AND o.expires_at > ${new Date(nowMs)} AND o.item_id = ${itemId}
      ORDER BY o.amount DESC LIMIT 50`;
    return rows.map(offerWire);
  }

  /** Offers an account made (any status, newest first). */
  async offersBy(account: string): Promise<OfferWire[]> {
    const rows = await this.prisma.$queryRaw<OfferRow[]>`
      ${OFFER_SELECT} FROM chain.offers o WHERE o.contract_id = ${this.items} AND o.buyer_id = ${account}
      ORDER BY o.made_at DESC LIMIT 50`;
    return rows.map(offerWire);
  }

  /** Workers for rent (free first, cheapest first), or one lender's / renter's. */
  async rentals(q: { lender?: string; renter?: string; nowMs: number; limit?: number }): Promise<RentalWire[]> {
    const now = new Date(q.nowMs);
    const where: Prisma.Sql[] = [Prisma.sql`r.contract_id = ${this.items}`];
    if (q.lender) where.push(Prisma.sql`r.lender_id = ${q.lender}`);
    if (q.renter) where.push(Prisma.sql`r.renter_id = ${q.renter} AND r.rented_until > ${now}`);
    const rows = await this.prisma.$queryRaw<RentalRow[]>`
      SELECT t.token_id, t.title, t.kind, t.owner_id, t.nft_type, t.item_id, t.quantity::text AS quantity, t.props, t.minted_from,
             (extract(epoch FROM t.minted_at) * 1000)::bigint::text AS minted_at, NULL::text AS price,
             (SELECT (d.data->>'xp')::int FROM documents d WHERE d.path = 'workers/' || t.token_id) AS worker_xp,
             r.lender_id, r.price_per_day::text, r.max_days, r.renter_id,
             (extract(epoch FROM r.rented_until) * 1000)::bigint::text AS rented_until, r.earned::text
      FROM chain.rentals r JOIN chain.tokens t ON t.contract_id = r.contract_id AND t.token_id = r.token_id
      WHERE ${Prisma.join(where, ' AND ')}
      ORDER BY (r.rented_until IS NOT NULL AND r.rented_until > ${now}), r.price_per_day ASC, t.token_id
      LIMIT ${Math.min(q.limit ?? 60, 100)}`;
    return rows.map((r) => ({
      token: wire(r),
      lender_id: r.lender_id,
      price_per_day: r.price_per_day,
      max_days: r.max_days,
      renter_id: r.renter_id,
      rented_until: r.rented_until === null ? 0 : Number(r.rented_until),
      earned: r.earned,
    }));
  }

  async itemStats(itemId: string, nowMs: number): Promise<ItemStatsWire> {
    const [s] = await this.prisma.$queryRaw<{ floor: string | null; listed: string; minted: string; burned: string; supply: string; sales: string }[]>`
      SELECT
        (SELECT min(l.price / GREATEST(t.quantity, 1))::text FROM chain.tokens t JOIN chain.listings l ON ${LISTING_JOIN}
          WHERE t.contract_id = ${this.items} AND t.item_id = ${itemId} AND t.burned_at IS NULL) AS floor,
        (SELECT count(*)::text FROM chain.tokens t JOIN chain.listings l ON ${LISTING_JOIN}
          WHERE t.contract_id = ${this.items} AND t.item_id = ${itemId} AND t.burned_at IS NULL) AS listed,
        (SELECT count(*)::text FROM chain.tokens WHERE contract_id = ${this.items} AND item_id = ${itemId}) AS minted,
        (SELECT count(*)::text FROM chain.tokens WHERE contract_id = ${this.items} AND item_id = ${itemId} AND burned_at IS NOT NULL) AS burned,
        (SELECT COALESCE(sum(quantity), 0)::text FROM chain.tokens WHERE contract_id = ${this.items} AND item_id = ${itemId} AND burned_at IS NULL) AS supply,
        (SELECT count(*)::text FROM chain.sales WHERE contract_id = ${this.items} AND item_id = ${itemId} AND sold_at > ${new Date(nowMs - 86_400_000)}) AS sales`;
    return {
      item_id: itemId,
      floor_per_unit: s?.floor ? s.floor.split('.')[0]! : null,
      listed: Number(s?.listed ?? 0),
      minted: Number(s?.minted ?? 0),
      burned: Number(s?.burned ?? 0),
      supply: Number(s?.supply ?? 0),
      sales_24h: Number(s?.sales ?? 0),
    };
  }
}

/**
 * Daily average sale prices, oldest first, in FARM. Days without a sale repeat the day before;
 * days before the first sale are left out.
 */
export function dailySeries(rows: readonly { day: number; avg: string }[], days: number): number[] {
  const byDay = new Map(rows.map((r) => [r.day, farmNumber(r.avg)]));
  const out: number[] = [];
  let last: number | undefined;
  for (let d = days - 1; d >= 0; d--) {
    last = byDay.get(d) ?? last;
    if (last !== undefined) out.push(last);
  }
  return out;
}
