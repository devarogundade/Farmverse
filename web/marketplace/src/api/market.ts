/**
 * Marketplace read API. Mocked for now; each function maps to a route in docs §9
 * (GET /listings, /tokens/{id}, /owners/{account}/tokens, /stats).
 */
import { TOKENS, type Token } from '../data/mock';
import type { NftType } from '@farmverse/shared/nft/metadata';

export type Sort = 'newest' | 'price_asc' | 'price_desc';

export interface ListingQuery {
  type?: NftType;
  q?: string;
  owner?: string;
  sort?: Sort;
}

export function listTokens({ type, q, owner, sort = 'newest' }: ListingQuery = {}): Token[] {
  const needle = q?.trim().toLowerCase();
  const rows = TOKENS.filter(
    (t) =>
      (!type || t.metadata.type === type) &&
      (!owner || t.owner_id === owner) &&
      (!needle || t.title.toLowerCase().includes(needle) || t.token_id.toLowerCase().includes(needle)),
  );
  const price = (t: Token) => t.price ?? Number.POSITIVE_INFINITY;
  if (sort === 'price_asc') rows.sort((a, b) => price(a) - price(b));
  if (sort === 'price_desc') rows.sort((a, b) => (b.price ?? -1) - (a.price ?? -1));
  if (sort === 'newest') rows.sort((a, b) => (b.events[0]?.at ?? 0) - (a.events[0]?.at ?? 0));
  return rows;
}

export const getToken = (id: string) => TOKENS.find((t) => t.token_id === id);

/** Lowest active listing among tokens of the same model (workers: all workers). */
export function floorPrice(token: Token): number | undefined {
  const same = TOKENS.filter(
    (t) => t.price != null && t.metadata.type === token.metadata.type && (token.metadata.type === 'worker' || t.metadata.item_id === token.metadata.item_id),
  );
  const prices = same.map((t) => t.price!);
  return prices.length ? Math.min(...prices) : undefined;
}

export function related(token: Token, n = 3): Token[] {
  const same = TOKENS.filter((t) => t.token_id !== token.token_id && t.metadata.type === token.metadata.type);
  const other = TOKENS.filter((t) => t.token_id !== token.token_id && t.metadata.type !== token.metadata.type);
  return [...same, ...other].slice(0, n);
}
