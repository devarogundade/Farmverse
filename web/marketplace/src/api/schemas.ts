/**
 * Wire schemas for the read API (docs 13 §9). Prices travel as yocto-FARM strings (18 decimals) and
 * are turned into plain FARM numbers for the UI here, in one place.
 */
import { parseMetadata } from '@farmverse/shared/nft/metadata';
import { z } from 'zod';
import { fromYocto } from '../lib/amount';
import type { Drop, ItemStats, Note, Offer, Page, Rental, Token, TokenDetail } from './types';

const yocto = z.string().regex(/^\d+$/);
const farm = yocto.transform((v) => fromYocto(v));

const EventWire = z.object({
  kind: z.enum(['minted', 'listed', 'bought', 'delisted', 'redeemed', 'sent', 'offer']),
  account: z.string(),
  price: farm.nullish(),
  at: z.number(),
});

export const TokenWire = z
  .object({
    token_id: z.string(),
    title: z.string(),
    kind: z.string().optional(),
    owner_id: z.string(),
    metadata: z.unknown(),
    price: farm.nullish(),
    price_history: z.array(z.number()).default([]),
    events: z.array(EventWire).default([]),
  })
  .transform((t, ctx): Token => {
    const meta = parseMetadata(t.metadata);
    if (!meta.ok) {
      ctx.addIssue({ code: 'custom', message: meta.error });
      return z.NEVER;
    }
    return {
      token_id: t.token_id,
      title: t.title,
      kind: t.kind,
      owner_id: t.owner_id,
      metadata: meta.value,
      price: t.price ?? undefined,
      priceHistory: t.price_history,
      events: t.events.map((e) => ({ ...e, price: e.price ?? undefined })),
    };
  });

/**
 * A list of tokens where one this build can't render (a model added after it shipped) is left out
 * instead of failing the whole page.
 */
export const TokenListWire = z.array(z.unknown()).transform((rows) =>
  rows.flatMap((row) => {
    const t = TokenWire.safeParse(row);
    return t.success ? [t.data] : [];
  }),
);

export const PageWire = z
  .object({ items: TokenListWire, next_cursor: z.string().nullable(), total: z.number() })
  .transform((p): Page<Token> => p);

export const TokenDetailWire = z
  .object({ token: TokenWire, floor: farm.nullish(), related: TokenListWire.default([]) })
  .transform((d): TokenDetail => ({ token: d.token, floor: d.floor ?? undefined, related: d.related }));

export const OfferWire = z
  .object({
    id: z.string(),
    token_id: z.string(),
    item_id: z.string().nullish(),
    min_quantity: z.number().nullish(),
    from: z.string(),
    amount: farm,
    expires_at: z.number(),
    status: z.enum(['open', 'accepted', 'cancelled']),
  })
  .transform((o): Offer => ({ ...o, item_id: o.item_id ?? undefined, min_quantity: o.min_quantity ?? undefined }));

export const DropWire = z
  .object({
    id: z.string(),
    kind: z.string(),
    item_id: z.string(),
    quantity: z.number(),
    title: z.string(),
    price: yocto,
    cap: z.number().nullable(),
    sold: z.number(),
    per_wallet: z.number().nullable(),
    starts_at: z.number(),
    ends_at: z.number().nullable(),
    paused: z.boolean(),
  })
  .transform(
    (d): Drop => ({
      id: d.id,
      kind: d.kind,
      item_id: d.item_id,
      quantity: d.quantity,
      title: d.title,
      price: fromYocto(d.price),
      priceYocto: d.price,
      cap: d.cap ?? undefined,
      sold: d.sold,
      per_wallet: d.per_wallet ?? undefined,
      starts_at: d.starts_at,
      ends_at: d.ends_at ?? undefined,
      paused: d.paused,
    }),
  );

const RentalRow = z.object({
  token: z.unknown(),
  lender_id: z.string(),
  price_per_day: yocto,
  max_days: z.number(),
  renter_id: z.string().nullable(),
  rented_until: z.number(),
  earned: yocto,
});

/** Rentals; one whose worker this build can't render is left out. */
export const RentalListWire = z.array(z.unknown()).transform((rows) =>
  rows.flatMap((row): Rental[] => {
    const r = RentalRow.safeParse(row);
    if (!r.success) return [];
    const token = TokenWire.safeParse(r.data.token);
    if (!token.success) return [];
    return [
      {
        token: token.data,
        lender_id: r.data.lender_id,
        price_per_day: fromYocto(r.data.price_per_day),
        pricePerDayYocto: r.data.price_per_day,
        max_days: r.data.max_days,
        renter_id: r.data.renter_id ?? undefined,
        rented_until: r.data.rented_until,
        earned: fromYocto(r.data.earned),
      },
    ];
  }),
);

export const ItemStatsWire = z
  .object({
    item_id: z.string(),
    floor_per_unit: yocto.nullable(),
    listed: z.number(),
    minted: z.number(),
    burned: z.number(),
    supply: z.number(),
    sales_24h: z.number(),
  })
  .transform((s): ItemStats => ({ ...s, floor_per_unit: s.floor_per_unit == null ? undefined : fromYocto(s.floor_per_unit) }));

export const NoteWire = z
  .object({ id: z.string(), icon: z.string(), text: z.string(), href: z.string().optional(), at: z.number(), read: z.boolean() })
  .transform((n): Note => n);
