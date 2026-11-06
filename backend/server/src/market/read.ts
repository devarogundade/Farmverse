import { farmNumber, formatFarm, NFT_TYPES, type NftType } from '../chain/items';
import { Prisma, type PrismaClient } from '../generated/prisma/client';

/**
 * The marketplace's reads over the `chain` schema (docs 13 §9). Shapes match the wire schemas in
 * web/marketplace/src/api/schemas.ts: prices are yocto-FARM strings, times are epoch ms.
 */

export type Sort = 'newest' | 'price_asc' | 'price_desc';

export interface TokenWire {
  token_id: string;
  title: string;
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
  cursor?: string | null;
  limit?: number;
}

interface Row {
  token_id: string;
  title: string;
  owner_id: string;
  nft_type: NftType;
  item_id: string;
  quantity: string;
  props: Record<string, unknown> | null;
  minted_from: string | null;
  minted_at: string;
  price: string | null;
}

export const MAX_LIMIT = 60;
const HISTORY_DAYS = 24;

const isPrimitive = (v: unknown): v is string | number | boolean => ['string', 'number', 'boolean'].includes(typeof v);

/** The metadata the marketplace renders (web/shared/src/nft/metadata.ts `NftMetadata`). */
export function metadataOf(r: Pick<Row, 'nft_type' | 'item_id' | 'quantity' | 'props' | 'minted_from' | 'minted_at'>): Record<string, unknown> {
  const { outfit, ...rest } = r.props ?? {};
  const traits = Object.fromEntries(Object.entries(rest).filter(([, v]) => isPrimitive(v)));
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
  owner_id: r.owner_id,
  metadata: metadataOf(r),
  price: r.price,
  price_history: [],
  events: [],
});

const SELECT = Prisma.sql`
  SELECT t.token_id, t.title, t.owner_id, t.nft_type, t.item_id, t.quantity::text AS quantity, t.props, t.minted_from,
         (extract(epoch FROM t.minted_at) * 1000)::bigint::text AS minted_at, l.price::text AS price`;

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
    const where: Prisma.Sql[] = [Prisma.sql`t.contract_id = ${this.items}`, Prisma.sql`t.burned_at IS NULL`, Prisma.sql`t.nft_type IS NOT NULL`];
    if (q.type && NFT_TYPES.includes(q.type)) where.push(Prisma.sql`t.nft_type = ${q.type}`);
    if (q.owner) where.push(Prisma.sql`t.owner_id = ${q.owner}`);
    const needle = q.q?.trim();
    if (needle) {
      const like = `%${escapeLike(needle.slice(0, 64))}%`;
      where.push(Prisma.sql`(t.title ILIKE ${like} OR t.item_id ILIKE ${like} OR t.token_id ILIKE ${like})`);
    }
    const join = q.owner ? Prisma.sql`LEFT JOIN` : Prisma.sql`JOIN`;
    const from = Prisma.sql`FROM chain.tokens t ${join} chain.listings l ON ${LISTING_JOIN} WHERE ${Prisma.join(where, ' AND ')}`;
    const sort = q.sort ?? 'newest';
    const order =
      sort === 'price_asc'
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

  /** Chain happenings addressed to an account: its items sold, items sent to it. */
  async notes(account: string, readUpToMs: number): Promise<NoteWire[]> {
    const rows = await this.prisma.$queryRaw<
      { id: string; kind: string; account_id: string; price: string | null; at: string; token_id: string; title: string | null }[]
    >`
      SELECT a.id, a.kind, a.account_id, a.price::text AS price, (extract(epoch FROM a.at) * 1000)::bigint::text AS at, a.token_id, t.title
      FROM chain.activity a LEFT JOIN chain.tokens t ON t.contract_id = a.contract_id AND t.token_id = a.token_id
      WHERE a.counterparty = ${account} AND a.kind IN ('bought', 'sent') AND a.contract_id = ${this.items}
      ORDER BY a.at DESC LIMIT 30`;
    return rows.map((r) => {
      const title = r.title ?? r.token_id;
      const at = Number(r.at);
      return {
        id: r.id,
        icon: r.kind === 'bought' ? 'ResourceBar_Icon_Gold' : 'Icon_Gift',
        text: r.kind === 'bought' ? `${r.account_id} bought your ${title} for ${formatFarm(r.price ?? '0')} FARM` : `${r.account_id} sent you ${title}`,
        href: `#/token/${encodeURIComponent(r.token_id)}`,
        at,
        read: at <= readUpToMs,
      };
    });
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
