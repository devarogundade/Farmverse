/**
 * Marketplace actions: on chain (NEAR Connect wallet + chain/market) or on the mock, then refresh
 * the queries they affect. Dialogs call these and show pending / done / error states.
 */
import { useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { keys } from '../api/queries';
import type { Offer, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { DATA_MODE } from '../config';
import { actions as mock } from '../state/store';

const onChain = DATA_MODE === 'api';
/** near-api-js and the contract layer load only when a real wallet is in use. */
const chainCall = <T,>(fn: (c: typeof import('../chain/market')) => Promise<T>) => import('../chain/market').then(fn);

/** Wallet errors arrive in many shapes; give people one sentence. */
function friendly(e: unknown): Error {
  const msg = (e as Error)?.message ?? String(e);
  if (/reject|cancel|denied|closed/i.test(msg)) return new Error('You cancelled in your wallet.');
  if (/not enough|insufficient|balance/i.test(msg)) return new Error('Not enough funds for this. Check your FARM and NEAR.');
  return e instanceof Error ? e : new Error(msg);
}

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
    } catch (e) {
      throw friendly(e);
    } finally {
      refresh(tokenId);
    }
  };

  return {
    buy: (t: Token) => run(t.token_id, () => (onChain ? chainCall((c) => c.buy(t.token_id, t.price!)) : mock.buy(t.token_id))),
    list: (t: Token, price: number) =>
      run(t.token_id, () => (onChain ? chainCall((c) => (t.price != null ? c.updatePrice(t.token_id, price) : c.list(accountId!, t.token_id, price))) : mock.list(t.token_id, price))),
    delist: (t: Token) => run(t.token_id, () => (onChain ? chainCall((c) => c.delist(t.token_id)) : mock.delist(t.token_id))),
    redeem: (t: Token) => {
      if (t.metadata.type === 'worker') return Promise.reject(new Error('Workers stay in your wallet and work on your island — they can’t be used up.'));
      return run(t.token_id, () => (onChain ? chainCall((c) => c.redeem(t.token_id)) : mock.redeem(t.token_id)));
    },
    send: (t: Token, to: string) => run(t.token_id, () => (onChain ? chainCall((c) => c.send(t.token_id, to)) : mock.send(t.token_id, to))),
    // Offers are off-chain (docs 14 §3): the API in both modes.
    offer: (t: Token, amount: number, days: number) => run(t.token_id, () => api.createOffer(t.token_id, amount, days)),
    cancelOffer: (o: Offer) => run(o.token_id, () => api.cancelOffer(o.id)),
    declineOffer: (o: Offer) => run(o.token_id, () => api.declineOffer(o.id)),
    acceptOffer: (o: Offer) => run(o.token_id, () => api.acceptOffer(o.id)),
  };
}
