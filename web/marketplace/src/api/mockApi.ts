/**
 * MarketApi over the in-browser mock (state/store). Same shapes and cursor pagination as the real
 * API, with a little latency so loading states show up during development.
 */
import { config } from '../config';
import type { Token } from '../data/mock';
import { actions, getState } from '../state/store';
import type { ListingQuery, MarketApi, Page, TokenDetail } from './types';

const latency = (signal?: AbortSignal, ms = 250 + Math.random() * 350) =>
  new Promise<void>((resolve, reject) => {
    const t = setTimeout(resolve, ms);
    signal?.addEventListener('abort', () => {
      clearTimeout(t);
      reject(new DOMException('Aborted', 'AbortError'));
    });
  });

const tokens = () => getState().tokens;

function query({ type, q, owner, sort = 'newest' }: ListingQuery): Token[] {
  const needle = q?.trim().toLowerCase();
  const rows = tokens().filter(
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

function floorOf(token: Token): number | undefined {
  const prices = tokens()
    .filter((t) => t.price != null && t.metadata.type === token.metadata.type && (token.metadata.type === 'worker' || t.metadata.item_id === token.metadata.item_id))
    .map((t) => t.price!);
  return prices.length ? Math.min(...prices) : undefined;
}

function relatedTo(token: Token, n = 3): Token[] {
  const others = tokens().filter((t) => t.token_id !== token.token_id);
  return [...others.filter((t) => t.metadata.type === token.metadata.type), ...others.filter((t) => t.metadata.type !== token.metadata.type)].slice(0, n);
}

export const mockApi: MarketApi = {
  async listings(q, signal) {
    await latency(signal);
    const rows = query(q);
    const start = Number(q.cursor ?? 0);
    const limit = q.limit ?? config.pageSize;
    const items = rows.slice(start, start + limit);
    const next = start + limit < rows.length ? String(start + limit) : null;
    return { items, next_cursor: next, total: rows.length } satisfies Page<Token>;
  },
  async token(id, signal) {
    await latency(signal);
    const token = tokens().find((t) => t.token_id === id);
    return token ? ({ token, floor: floorOf(token), related: relatedTo(token) } satisfies TokenDetail) : null;
  },
  async tokens(ids, signal) {
    await latency(signal);
    return tokens().filter((t) => ids.includes(t.token_id));
  },
  async offers(tokenId, signal) {
    await latency(signal, 200);
    return getState().offers.filter((o) => o.token_id === tokenId && o.status === 'open');
  },
  async notifications(account, signal) {
    await latency(signal, 200);
    return account === getState().account ? getState().notes : [];
  },
  async markNotificationsRead() {
    actions.readNotes();
  },
  createOffer: (tokenId, amount, days) => actions.offer(tokenId, amount, days),
  cancelOffer: (id) => actions.cancelOffer(id),
  declineOffer: (id) => actions.declineOffer(id),
  acceptOffer: (id) => actions.acceptOffer(id),
};
