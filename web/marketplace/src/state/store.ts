/**
 * Marketplace state for the mock: who is signed in, their FARM, every token, offers and notifications.
 *
 * Each action waits briefly as if the wallet were confirming, then updates state. When the NEAR
 * wallet selector and contracts are wired in, the bodies of these actions become contract calls
 * (docs §6.3: nft_approve to list, ft_transfer_call to buy, items.redeem, nft_transfer); the UI
 * that calls them stays the same.
 */
import { useSyncExternalStore } from 'react';
import { ME, START_BALANCE, TOKENS, type EventKind, type Token } from '../data/mock';

/** Proposed in the plan (§11 Q2): 5 % market fee taken from the seller. Not final. */
export const MARKET_FEE = 0.05;
const WALLET_DELAY = 900;

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
  icon: string; // game sprite
  text: string;
  href?: string;
  at: number;
  read: boolean;
}

export interface State {
  account: string | null;
  balance: number;
  tokens: Token[];
  offers: Offer[];
  notes: Note[];
  saved: string[];
}

const now = () => Date.now();
const id = () => Math.random().toString(36).slice(2, 10);

let state: State = {
  account: ME,
  balance: START_BALANCE,
  tokens: structuredClone(TOKENS),
  offers: [
    { id: 'o1', token_id: 'w-0981', from: 'floyd.near', amount: 12.5, expires_at: now() + 3 * 86_400_000, status: 'open' },
    { id: 'o2', token_id: 'g-01JD7Q3K8M', from: 'bram.near', amount: 5.4, expires_at: now() + 86_400_000, status: 'open' },
    { id: 'o3', token_id: 'w-0402', from: ME, amount: 28, expires_at: now() + 2 * 86_400_000, status: 'open' },
  ],
  notes: [
    { id: 'n1', icon: 'Icon_Mail', text: 'floyd.near offered 12.5 FARM for Tilly Furrow', href: '#/token/w-0981', at: now() - 3_600_000, read: false },
    { id: 'n2', icon: 'Icon_Mail', text: 'bram.near offered 5.4 FARM for Wheat ×250', href: '#/token/g-01JD7Q3K8M', at: now() - 5 * 3_600_000, read: false },
    { id: 'n3', icon: 'Icon_Gift', text: 'Welcome to the market! Your island items show up under My items.', href: '#/mine', at: now() - 2 * 86_400_000, read: true },
  ],
  saved: [],
};

const listeners = new Set<() => void>();
const set = (fn: (s: State) => State) => {
  state = fn(state);
  listeners.forEach((l) => l());
};
const subscribe = (l: () => void) => {
  listeners.add(l);
  return () => listeners.delete(l);
};

export const getState = () => state;
export function useStore<T>(select: (s: State) => T): T {
  return useSyncExternalStore(subscribe, () => select(state));
}

const wallet = () => new Promise<void>((r) => setTimeout(r, WALLET_DELAY));
const round = (n: number) => Math.round(n * 100) / 100;

function patchToken(s: State, tokenId: string, fn: (t: Token) => Token): State {
  return { ...s, tokens: s.tokens.map((t) => (t.token_id === tokenId ? fn(t) : t)) };
}
function withEvent(t: Token, kind: EventKind, account: string, price?: number): Token {
  return { ...t, events: [{ kind, account, price, at: now() }, ...t.events] };
}
function note(s: State, icon: string, text: string, href?: string): State {
  return { ...s, notes: [{ id: id(), icon, text, href, at: now(), read: false }, ...s.notes] };
}
function need(cond: unknown, msg: string): asserts cond {
  if (!cond) throw new Error(msg);
}
const token = (tokenId: string) => state.tokens.find((t) => t.token_id === tokenId);
const me = () => {
  need(state.account, 'Connect your wallet first.');
  return state.account;
};

/** NEAR account names: name.near, sub.name.testnet, or a 64-character implicit account. */
export const isAccount = (v: string) => /^(([a-z\d]+[-_])*[a-z\d]+\.)+(near|testnet)$/.test(v) || /^[a-f\d]{64}$/.test(v);

export const actions = {
  async connect(account = ME) {
    await wallet();
    set((s) => ({ ...s, account }));
  },
  disconnect() {
    set((s) => ({ ...s, account: null }));
  },

  async buy(tokenId: string) {
    const who = me();
    const t = token(tokenId);
    need(t && t.price != null, 'This item is no longer for sale.');
    need(t.owner_id !== who, 'You already own this item.');
    need(state.balance >= t.price, 'Not enough FARM.');
    const price = t.price;
    await wallet();
    set((s) => {
      s = patchToken(s, tokenId, (x) => withEvent({ ...x, owner_id: who, price: undefined }, 'bought', who, price));
      s = { ...s, balance: round(s.balance - price), offers: s.offers.map((o) => (o.token_id === tokenId && o.status === 'open' ? { ...o, status: 'cancelled' } : o)) };
      return note(s, 'Icon_Shop', `You bought ${t.title} for ${price} FARM`, `#/token/${tokenId}`);
    });
  },

  async list(tokenId: string, price: number) {
    const who = me();
    const t = token(tokenId);
    need(t && t.owner_id === who, 'Only the owner can sell this item.');
    need(price > 0, 'Enter a price above 0.');
    await wallet();
    set((s) => patchToken(s, tokenId, (x) => withEvent({ ...x, price: round(price) }, 'listed', who, round(price))));
  },

  async delist(tokenId: string) {
    const who = me();
    const t = token(tokenId);
    need(t && t.owner_id === who && t.price != null, 'This item is not for sale.');
    await wallet();
    set((s) => patchToken(s, tokenId, (x) => withEvent({ ...x, price: undefined }, 'delisted', who)));
  },

  async offer(tokenId: string, amount: number, days: number) {
    const who = me();
    const t = token(tokenId);
    need(t && t.owner_id !== who, 'You can’t make an offer on your own item.');
    need(amount > 0, 'Enter an amount above 0.');
    need(amount <= state.balance, 'Not enough FARM for this offer.');
    await wallet();
    set((s) => {
      const open = s.offers.filter((o) => !(o.token_id === tokenId && o.from === who && o.status === 'open'));
      const o: Offer = { id: id(), token_id: tokenId, from: who, amount: round(amount), expires_at: now() + days * 86_400_000, status: 'open' };
      return patchToken({ ...s, offers: [o, ...open] }, tokenId, (x) => withEvent(x, 'offer', who, o.amount));
    });
  },

  async cancelOffer(offerId: string) {
    me();
    await wallet();
    set((s) => ({ ...s, offers: s.offers.map((o) => (o.id === offerId ? { ...o, status: 'cancelled' } : o)) }));
  },

  async declineOffer(offerId: string) {
    me();
    await wallet();
    set((s) => ({ ...s, offers: s.offers.map((o) => (o.id === offerId ? { ...o, status: 'declined' } : o)) }));
  },

  async acceptOffer(offerId: string) {
    const who = me();
    const o = state.offers.find((x) => x.id === offerId);
    need(o && o.status === 'open', 'This offer is no longer open.');
    const t = token(o.token_id);
    need(t && t.owner_id === who, 'Only the owner can accept offers.');
    await wallet();
    set((s) => {
      s = patchToken(s, o.token_id, (x) => withEvent({ ...x, owner_id: o.from, price: undefined }, 'bought', o.from, o.amount));
      s = {
        ...s,
        balance: round(s.balance + o.amount * (1 - MARKET_FEE)),
        offers: s.offers.map((x) => (x.id === offerId ? { ...x, status: 'accepted' } : x.token_id === o.token_id && x.status === 'open' ? { ...x, status: 'cancelled' } : x)),
      };
      return note(s, 'Icon_Golds', `You sold ${t.title} to ${o.from} for ${o.amount} FARM`, '#/mine');
    });
  },

  async send(tokenId: string, to: string) {
    const who = me();
    const t = token(tokenId);
    need(t && t.owner_id === who, 'Only the owner can send this item.');
    need(t.price == null, 'Take it off the market before sending it.');
    need(isAccount(to), 'That doesn’t look like a NEAR account (e.g. friend.near).');
    need(to !== who, 'That’s your own account.');
    await wallet();
    set((s) => note(patchToken(s, tokenId, (x) => withEvent({ ...x, owner_id: to }, 'sent', to)), 'Icon_Gift', `You sent ${t.title} to ${to}`, `#/token/${tokenId}`));
  },

  /** Redeem: the item is used up and lands on the holder's island (items.redeem burns the NFT). */
  async redeem(tokenId: string) {
    const who = me();
    const t = token(tokenId);
    need(t && t.owner_id === who, 'Only the owner can use this item.');
    need(t.price == null, 'Take it off the market before using it.');
    await wallet();
    set((s) => {
      s = { ...s, tokens: s.tokens.filter((x) => x.token_id !== tokenId), offers: s.offers.map((o) => (o.token_id === tokenId && o.status === 'open' ? { ...o, status: 'cancelled' } : o)) };
      return note(s, 'Paint_fac_farmhouse_l1', `${t.title} is on its way to your island`);
    });
  },

  toggleSave(tokenId: string) {
    set((s) => ({ ...s, saved: s.saved.includes(tokenId) ? s.saved.filter((x) => x !== tokenId) : [...s.saved, tokenId] }));
  },
  readNotes() {
    set((s) => ({ ...s, notes: s.notes.map((n) => ({ ...n, read: true })) }));
  },
};

export type MarketMessage =
  | { type: 'listing' | 'delist' | 'sale' | 'redeem'; token_id: string }
  | { type: 'offer'; token_id: string; to: string }
  | { type: 'note'; to: string; text: string; icon?: string };

const BIDDERS = ['jenny.near', 'kofi.near', 'oak-and-acre.near', 'ines.near', 'markfisher.near'];
const pick = <T,>(a: T[]) => a[Math.floor(Math.random() * a.length)];

/** Mock realtime: another farmer changes a price, or bids on one of your items. Returns what the server would push. */
export function simulateMarketEvent(): MarketMessage[] {
  const who = state.account;
  const mine = who ? state.tokens.filter((t) => t.owner_id === who) : [];
  if (mine.length && Math.random() < 0.45) {
    const t = pick(mine);
    const from = pick(BIDDERS);
    const amount = round((t.price ?? t.priceHistory[t.priceHistory.length - 1] ?? 10) * (0.75 + Math.random() * 0.2));
    const text = `${from} offered ${amount} FARM for ${t.title}`;
    set((s) =>
      note(
        { ...s, offers: [{ id: id(), token_id: t.token_id, from, amount, expires_at: now() + 3 * 86_400_000, status: 'open' }, ...s.offers] },
        'Icon_Mail',
        text,
        `#/token/${t.token_id}`,
      ),
    );
    return [
      { type: 'offer', token_id: t.token_id, to: who! },
      { type: 'note', to: who!, text, icon: 'Icon_Mail' },
    ];
  }
  const listed = state.tokens.filter((t) => t.price != null && t.owner_id !== who);
  if (!listed.length) return [];
  const t = pick(listed);
  const price = round(t.price! * (0.9 + Math.random() * 0.2));
  set((s) => patchToken(s, t.token_id, (x) => withEvent({ ...x, price }, 'listed', x.owner_id, price)));
  return [{ type: 'listing', token_id: t.token_id }];
}
