import type { NftMetadata, NftType } from '@farmverse/shared/nft/metadata';

export type Sort = 'newest' | 'price_asc' | 'price_desc';

export type EventKind = 'minted' | 'listed' | 'bought' | 'delisted' | 'redeemed' | 'sent' | 'offer';

export interface TokenEvent {
  kind: EventKind;
  account: string;
  /** FARM. */
  price?: number;
  /** Epoch ms. */
  at: number;
}

/** An item NFT as the read API serves it (backend/server/src/market/read.ts). */
export interface Token {
  token_id: string;
  title: string;
  owner_id: string;
  metadata: NftMetadata;
  /** Active listing price in FARM, if listed. */
  price?: number;
  /** Daily average sale price of the same model, oldest first (detail only). */
  priceHistory: number[];
  /** Newest first (detail only). */
  events: TokenEvent[];
}

export interface Offer {
  id: string;
  token_id: string;
  from: string;
  amount: number;
  expires_at: number;
  status: 'open' | 'accepted' | 'declined' | 'cancelled';
}

export interface Note {
  id: string;
  /** A game sprite name. */
  icon: string;
  text: string;
  href?: string;
  at: number;
  read: boolean;
}

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
