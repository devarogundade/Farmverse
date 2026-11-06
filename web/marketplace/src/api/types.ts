import type { NftType } from '@farmverse/shared/nft/metadata';
import type { Token } from '../data/mock';
import type { Note, Offer } from '../state/store';

export type { Note, Offer, Token };
export type Sort = 'newest' | 'price_asc' | 'price_desc';

export interface Page<T> {
  items: T[];
  /** Pass back as `cursor` for the next page; null on the last page. */
  next_cursor: string | null;
  total: number;
}

export interface ListingQuery {
  type?: NftType;
  q?: string;
  sort?: Sort;
  /** Only this account's tokens (My items); listed or not. */
  owner?: string;
  cursor?: string | null;
  limit?: number;
}

export interface TokenDetail {
  token: Token;
  /** Lowest active listing for the same model (workers: any worker). */
  floor?: number;
  related: Token[];
}

/** The marketplace read API (docs 13 §9) plus off-chain offers and notifications (docs 14 §3). */
export interface MarketApi {
  listings(q: ListingQuery, signal?: AbortSignal): Promise<Page<Token>>;
  token(id: string, signal?: AbortSignal): Promise<TokenDetail | null>;
  tokens(ids: string[], signal?: AbortSignal): Promise<Token[]>;
  offers(tokenId: string, signal?: AbortSignal): Promise<Offer[]>;
  notifications(account: string, signal?: AbortSignal): Promise<Note[]>;
  markNotificationsRead(account: string): Promise<void>;
  createOffer(tokenId: string, amount: number, days: number): Promise<void>;
  cancelOffer(offerId: string): Promise<void>;
  declineOffer(offerId: string): Promise<void>;
  acceptOffer(offerId: string): Promise<void>;
}
