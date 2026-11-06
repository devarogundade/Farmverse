/**
 * Marketplace actions: contract calls through the connected wallet (chain/market), then refresh
 * the queries they affect. Dialogs call these and show pending / done / error states.
 *
 * The read API follows the chain through the indexer, so a change shows up there a few seconds
 * after the wallet confirms. The live socket refreshes pages when it lands; the delayed refreshes
 * here cover a page whose socket is down.
 */
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { keys } from '../api/queries';
import type { Offer, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { features } from '../config';

/** near-api-js and the contract layer load on first use. */
const chain = <T,>(fn: (c: typeof import('../chain/market')) => Promise<T>) => import('../chain/market').then(fn);

/** Wallet errors arrive in many shapes; give people one sentence. */
function friendly(e: unknown): Error {
  const msg = (e as Error)?.message ?? String(e);
  if (/reject|cancel|denied|closed/i.test(msg)) return new Error('You cancelled in your wallet.');
  if (/not enough|insufficient|balance|NotEnoughBalance/i.test(msg)) return new Error('Not enough funds for this. Check your FARM and NEAR.');
  if (/Paused/.test(msg)) return new Error('The market is paused for maintenance. Please try again later.');
  if (/Deposit storage/.test(msg)) return new Error('Your wallet needs a little NEAR for the listing deposit.');
  return e instanceof Error ? e : new Error(msg);
}

/** Seconds after a confirmed transaction when the indexer has usually caught up. */
const SETTLE_MS = [3_000, 8_000];

export function useMarket() {
  const qc = useQueryClient();
  const { accountId } = useWallet();

  const refresh = (tokenId?: string) => {
    qc.invalidateQueries({ queryKey: keys.market });
    qc.invalidateQueries({ queryKey: keys.balance(accountId) });
    qc.invalidateQueries({ queryKey: keys.notes(accountId) });
    if (tokenId) qc.invalidateQueries({ queryKey: keys.offers(tokenId) });
  };
  const run = async (tokenId: string | undefined, fn: () => Promise<unknown>) => {
    try {
      await fn();
      for (const ms of SETTLE_MS) setTimeout(() => refresh(tokenId), ms);
    } catch (e) {
      throw friendly(e);
    } finally {
      refresh(tokenId);
    }
  };
  const me = () => {
    if (!accountId) throw new Error('Connect your wallet first.');
    return accountId;
  };
  const offersClosed = () => Promise.reject(new Error('Offers aren’t open yet. You can buy listed items now.'));

  return {
    buy: (t: Token) => run(t.token_id, () => chain((c) => c.buy(t.token_id, t.price!))),
    list: (t: Token, price: number) =>
      run(t.token_id, () => chain((c) => (t.price != null ? c.updatePrice(t.token_id, price) : c.list(me(), t.token_id, price)))),
    delist: (t: Token) => run(t.token_id, () => chain((c) => c.delist(t.token_id))),
    redeem: (t: Token) => {
      if (t.metadata.type === 'worker') return Promise.reject(new Error('Workers stay in your wallet and work on your island — they can’t be used up.'));
      return run(t.token_id, () => chain((c) => c.redeem(t.token_id)));
    },
    send: (t: Token, to: string) => run(t.token_id, () => chain((c) => c.send(t.token_id, to))),
    // Offers are off-chain (docs 14 §3) and stay closed until the backend serves them.
    offer: (t: Token, amount: number, days: number) => (features.offers ? run(t.token_id, () => api.createOffer(t.token_id, amount, days)) : offersClosed()),
    cancelOffer: (o: Offer) => (features.offers ? run(o.token_id, () => api.cancelOffer(o.id)) : offersClosed()),
    declineOffer: (o: Offer) => (features.offers ? run(o.token_id, () => api.declineOffer(o.id)) : offersClosed()),
    acceptOffer: (o: Offer) => (features.offers ? run(o.token_id, () => api.acceptOffer(o.id)) : offersClosed()),
  };
}
