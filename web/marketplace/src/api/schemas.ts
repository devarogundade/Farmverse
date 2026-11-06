/**
 * Wire schemas for the read API (docs 13 §9). Prices travel as yocto-FARM strings (18 decimals) and
 * are turned into plain FARM numbers for the UI here, in one place.
 */
import { parseMetadata } from '@farmverse/shared/nft/metadata';
import { z } from 'zod';
import type { Token } from '../data/mock';
import { fromYocto } from '../lib/amount';
import type { Note, Offer, Page, TokenDetail } from './types';

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
      owner_id: t.owner_id,
      metadata: meta.value,
      price: t.price ?? undefined,
      priceHistory: t.price_history,
      events: t.events.map((e) => ({ ...e, price: e.price ?? undefined })),
    };
  });

export const PageWire = z
  .object({ items: z.array(TokenWire), next_cursor: z.string().nullable(), total: z.number() })
  .transform((p): Page<Token> => p);

export const TokenDetailWire = z
  .object({ token: TokenWire, floor: farm.nullish(), related: z.array(TokenWire).default([]) })
  .transform((d): TokenDetail => ({ token: d.token, floor: d.floor ?? undefined, related: d.related }));

export const OfferWire = z
  .object({
    id: z.string(),
    token_id: z.string(),
    from: z.string(),
    amount: farm,
    expires_at: z.number(),
    status: z.enum(['open', 'accepted', 'declined', 'cancelled']),
  })
  .transform((o): Offer => o);

export const NoteWire = z
  .object({ id: z.string(), icon: z.string(), text: z.string(), href: z.string().optional(), at: z.number(), read: z.boolean() })
  .transform((n): Note => n);
