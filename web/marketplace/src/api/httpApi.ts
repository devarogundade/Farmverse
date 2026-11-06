import { z } from 'zod';
import { http } from './http';
import { DropWire, ItemStatsWire, NoteWire, OfferWire, PageWire, RentalListWire, TokenDetailWire, TokenListWire } from './schemas';
import type { MarketApi } from './types';

/** MarketApi over the backend read API (docs 13 §9, plan 15 §12). Changes go through the wallet. */
export const httpApi: MarketApi = {
  listings: (q, signal) =>
    http(q.owner ? `/owners/${encodeURIComponent(q.owner)}/tokens` : '/listings', {
      query: { kind: q.type, q: q.q, sort: q.sort, cursor: q.cursor, limit: q.limit, specialty: q.specialty, rarity: q.rarity, item: q.item },
      schema: PageWire,
      signal,
    }),
  token: async (id, signal) => {
    try {
      return await http(`/tokens/${encodeURIComponent(id)}`, { schema: TokenDetailWire, signal });
    } catch (e) {
      if ((e as { status?: number }).status === 404) return null;
      throw e;
    }
  },
  tokens: (ids, signal) =>
    ids.length ? http('/tokens', { query: { ids: ids.join(',') }, schema: TokenListWire, signal }) : Promise.resolve([]),
  offers: (tokenId, signal) => http(`/tokens/${encodeURIComponent(tokenId)}/offers`, { schema: z.array(OfferWire), signal }),
  offersBy: (account, signal) => http(`/accounts/${encodeURIComponent(account)}/offers`, { schema: z.array(OfferWire), signal }),
  notifications: (account, signal) => http(`/accounts/${encodeURIComponent(account)}/notifications`, { schema: z.array(NoteWire), signal }),
  markNotificationsRead: async (account) => {
    await http(`/accounts/${encodeURIComponent(account)}/notifications/read`, { method: 'POST' });
  },
  drops: (signal) => http('/drops', { schema: z.array(DropWire), signal }),
  rentals: (signal) => http('/rentals', { schema: RentalListWire, signal }),
  myRentals: (account, signal) =>
    http(`/accounts/${encodeURIComponent(account)}/rentals`, { schema: z.object({ lent: RentalListWire, renting: RentalListWire }), signal }),
  itemStats: async (itemId, signal) => {
    try {
      return await http(`/items/${encodeURIComponent(itemId)}/stats`, { schema: ItemStatsWire.nullable(), signal });
    } catch (e) {
      if ((e as { status?: number }).status === 404) return null;
      throw e;
    }
  },
};
