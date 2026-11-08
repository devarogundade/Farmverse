/**
 * Marketplace actions: contract calls through the connected wallet (chain/market), then refresh
 * the queries they affect. Dialogs call these and show pending / done / error states.
 *
 * The read API follows the chain through the indexer, so a change shows up there a few seconds
 * after the wallet confirms. The live socket refreshes pages when it lands; the delayed refreshes
 * here cover a page whose socket is down.
 */
import { useQueryClient } from '@tanstack/react-query';
import { keys } from '../api/queries';
import type { Drop, Offer, Rental, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { toast } from '../components/Dialog';

/** near-api-js and the contract layer load on first use. */
const chain = <T,>(fn: (c: typeof import('../chain/market')) => Promise<T>) => import('../chain/market').then(fn);
const trade = <T,>(fn: (c: typeof import('../chain/trade')) => Promise<T>) => import('../chain/trade').then(fn);

/** Wallet errors arrive in many shapes; give people one sentence. */
function friendly(e: unknown): Error {
  const msg = (e as Error)?.message ?? String(e);
  if (/reject|cancel|denied|closed/i.test(msg)) return new Error('You cancelled in your wallet.');
  if (/not enough|insufficient|balance|NotEnoughBalance/i.test(msg)) return new Error('Not enough funds for this. Check your FARM and NEAR.');
  if (/Paused/.test(msg)) return new Error('The market is paused for maintenance. Please try again later.');
  if (/Deposit storage/.test(msg)) return new Error('Your wallet needs a little NEAR for the deposit.');
  if (/Sold out/.test(msg)) return new Error('Sold out!');
  if (/restocking/.test(msg)) return new Error('The shop is restocking. Please try again later.');
  if (/most this drop allows/.test(msg)) return new Error('You already have as many of these as one wallet can buy.');
  if (/Someone is renting/.test(msg)) return new Error('Someone hired this worker first. Your FARM was returned.');
  if (/rental is running/.test(msg)) return new Error('Someone is renting this worker. You can take it back when their days are up.');
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
    qc.invalidateQueries({ queryKey: keys.owed(accountId) });
    qc.invalidateQueries({ queryKey: keys.notes(accountId) });
    if (tokenId) qc.invalidateQueries({ queryKey: keys.offers(tokenId) });
    qc.invalidateQueries({ queryKey: keys.drops });
    qc.invalidateQueries({ queryKey: keys.rentals });
    qc.invalidateQueries({ queryKey: keys.myRentals(accountId) });
    qc.invalidateQueries({ queryKey: keys.offersBy(accountId) });
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
    if (!accountId) throw new Error('Sign in first.');
    return accountId;
  };

  return {
    buy: (t: Token) => run(t.token_id, () => chain((c) => c.buy(t.token_id, t.price!))),
    list: (t: Token, price: number) =>
      run(t.token_id, () => chain((c) => (t.price != null ? c.updatePrice(t.token_id, price) : c.list(me(), t.token_id, price)))),
    delist: (t: Token) => run(t.token_id, () => chain((c) => c.delist(t.token_id))),
    redeem: (t: Token) => {
      if (t.metadata.type === 'worker') return Promise.reject(new Error('Workers stay in your wallet and work on your island — they can’t be used up.'));
      return run(t.token_id, async () => {
        const who = me();
        const txHash = await chain((c) => c.redeem(t.token_id));
        // The burn is done; now the game credits the island. If that fails, the hash is kept and
        // retried on the next visit (api/game.ts), so the item isn't lost either way.
        try {
          const { creditRedeem } = await import('../api/game');
          await creditRedeem(who, txHash);
        } catch {
          toast('Used! It will reach your island the next time you open the market.', 'Icon_ImageIcon_Info');
        }
      });
    },
    /** Withdraws FARM the market is holding for you (registering you with FARM if needed). */
    collect: () => run(undefined, () => chain((c) => c.collectOwed(me()))),
    send: (t: Token, to: string) => run(t.token_id, () => chain((c) => c.send(t.token_id, to))),
    // Offers escrow FARM on the market contract (plan 15 §12.2).
    offer: (t: Token, amount: number, days: number) => run(t.token_id, () => trade((c) => c.makeOffer(me(), t.token_id, amount, days))),
    cancelOffer: (o: Offer) => run(o.token_id, () => trade((c) => c.cancelOffer(o.id))),
    /** Sells `t` to the offer (an item offer is accepted with the token being sold). */
    acceptOffer: (o: Offer, t: Token) => run(t.token_id, () => trade((c) => c.acceptOffer(me(), t.token_id, o.id))),
    /** One edition of a studio drop (Farm Pass, gem packs, land deeds …). */
    buyDrop: (d: Drop) => run(undefined, () => trade((c) => c.buyDrop(d.id, d.priceYocto))),
    // Renting workers (plan 15 §12.1).
    rentOut: (t: Token, pricePerDay: number, maxDays: number) => run(t.token_id, () => trade((c) => c.rentOut(me(), t.token_id, pricePerDay, maxDays))),
    rent: (r: Rental, days: number) => run(r.token.token_id, () => trade((c) => c.rent(r.token.token_id, r.pricePerDayYocto, days))),
    rentWithdraw: (r: Rental) => run(r.token.token_id, () => trade((c) => c.rentWithdraw(r.token.token_id))),
    rentEnd: (r: Rental) => run(r.token.token_id, () => trade((c) => c.rentEnd(r.token.token_id))),
    /** Testnet: today's free FARM from the game's faucet. Returns what arrived. */
    faucet: async () => {
      const who = me();
      try {
        const { claimFaucet } = await import('../api/game');
        const got = await claimFaucet(who);
        for (const ms of [0, ...SETTLE_MS]) setTimeout(() => qc.invalidateQueries({ queryKey: keys.balance(accountId) }), ms);
        return got;
      } catch (e) {
        const g = e as { code?: string; params?: Record<string, unknown> };
        if (g.code === 'FAUCET_WAIT') {
          const at = Number(g.params?.['nextAt'] ?? 0);
          const hours = at ? Math.max(1, Math.ceil((at - Date.now()) / 3_600_000)) : 0;
          throw new Error(hours ? `You’ve had today’s FARM. Come back in about ${hours} hour${hours > 1 ? 's' : ''}.` : 'You’ve had today’s FARM. Come back tomorrow.');
        }
        if (g.code === 'FAUCET_CLOSED') throw new Error('The free FARM tap is closed right now. Please try again later.');
        throw friendly(e);
      }
    },
  };
}
