import { z } from 'zod';
import { toYocto } from '../lib/amount';
import { http } from './http';
import { NoteWire, OfferWire, PageWire, TokenDetailWire, TokenWire } from './schemas';
import type { MarketApi } from './types';

/** MarketApi over the backend (docs 13 §9 read API + off-chain offers and notifications). */
export const httpApi: MarketApi = {
  listings: (q, signal) =>
    http(q.owner ? `/owners/${encodeURIComponent(q.owner)}/tokens` : '/listings', {
      query: { kind: q.type, q: q.q, sort: q.sort, cursor: q.cursor, limit: q.limit },
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
    ids.length ? http('/tokens', { query: { ids: ids.join(',') }, schema: z.array(TokenWire), signal }) : Promise.resolve([]),
  offers: (tokenId, signal) => http(`/tokens/${encodeURIComponent(tokenId)}/offers`, { schema: z.array(OfferWire), signal }),
  notifications: (account, signal) => http(`/accounts/${encodeURIComponent(account)}/notifications`, { schema: z.array(NoteWire), signal }),
  markNotificationsRead: async (account) => {
    await http(`/accounts/${encodeURIComponent(account)}/notifications/read`, { method: 'POST' });
  },
  createOffer: async (tokenId, amount, days) => {
    await http(`/tokens/${encodeURIComponent(tokenId)}/offers`, { method: 'POST', body: { amount: toYocto(amount), days } });
  },
  cancelOffer: async (id) => {
    await http(`/offers/${encodeURIComponent(id)}`, { method: 'DELETE' });
  },
  declineOffer: async (id) => {
    await http(`/offers/${encodeURIComponent(id)}/decline`, { method: 'POST' });
  },
  acceptOffer: async (id) => {
    await http(`/offers/${encodeURIComponent(id)}/accept`, { method: 'POST' });
  },
};
