import type { NftMetadata, NftType } from '@farmverse/shared/nft/metadata';

export type Sort = 'newest' | 'price_asc' | 'price_desc' | 'unit_price';
export type Specialty = 'fields' | 'animals' | 'machines' | 'freight';
export type Rarity = 'common' | 'rare' | 'epic' | 'legendary';

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
  /** The contract's kind (good, boost, building, land, tool, pack, worker). */
  kind?: string;
  owner_id: string;
  metadata: NftMetadata;
  /** Active listing price in FARM, if listed. */
  price?: number;
  /** Daily average sale price of the same model, oldest first (detail only). */
  priceHistory: number[];
  /** Newest first (detail only). */
  events: TokenEvent[];
}

/** An offer escrowed on the market contract: on one token, or on any token of an item (`token_id` "#wheat"). */
export interface Offer {
  id: string;
  token_id: string;
  item_id?: string;
  min_quantity?: number;
  from: string;
  /** FARM. */
  amount: number;
  expires_at: number;
  status: 'open' | 'accepted' | 'cancelled';
}

/** A studio drop: fixed price in FARM, maybe a cap (backend/server market read `DropWire`). */
export interface Drop {
  id: string;
  kind: string;
  item_id: string;
  quantity: number;
  title: string;
  /** FARM. */
  price: number;
  /** yocto-FARM, exactly as the contract wants it. */
  priceYocto: string;
  cap?: number;
  sold: number;
  per_wallet?: number;
  starts_at: number;
  ends_at?: number;
  paused: boolean;
}

/** A worker held by the market for rent. */
export interface Rental {
  token: Token;
  lender_id: string;
  /** FARM per day. */
  price_per_day: number;
  pricePerDayYocto: string;
  max_days: number;
  renter_id?: string;
  /** Epoch ms; 0 when not rented. */
  rented_until: number;
  /** FARM waiting for the lender. */
  earned: number;
}

export interface ItemStats {
  item_id: string;
  /** Cheapest listing per unit, FARM. */
  floor_per_unit?: number;
  listed: number;
  minted: number;
  burned: number;
  supply: number;
  sales_24h: number;
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
  /** Workers: by specialty / rarity. */
  specialty?: Specialty;
  rarity?: Rarity;
  /** Goods: one catalogue item (the game's "Buy more"). */
  item?: string;
  cursor?: string | null;
  limit?: number;
}

export interface TokenDetail {
  token: Token;
  /** Lowest active listing for the same model (workers: any worker). */
  floor?: number;
  related: Token[];
}

/** The marketplace read API (docs 13 §9, plan 15 §12). Every change is a wallet transaction (chain/market). */
export interface MarketApi {
  listings(q: ListingQuery, signal?: AbortSignal): Promise<Page<Token>>;
  token(id: string, signal?: AbortSignal): Promise<TokenDetail | null>;
  tokens(ids: string[], signal?: AbortSignal): Promise<Token[]>;
  /** Live offers the token's owner could accept (on the token, and on its item). */
  offers(tokenId: string, signal?: AbortSignal): Promise<Offer[]>;
  /** Offers an account made. */
  offersBy(account: string, signal?: AbortSignal): Promise<Offer[]>;
  notifications(account: string, signal?: AbortSignal): Promise<Note[]>;
  markNotificationsRead(account: string): Promise<void>;
  drops(signal?: AbortSignal): Promise<Drop[]>;
  rentals(signal?: AbortSignal): Promise<Rental[]>;
  myRentals(account: string, signal?: AbortSignal): Promise<{ lent: Rental[]; renting: Rental[] }>;
  itemStats(itemId: string, signal?: AbortSignal): Promise<ItemStats | null>;
}
