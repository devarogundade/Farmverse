/**
 * Sending the market's calls. Our own in-page wallets (Email or Google, and the game's guest farm) sign each call as
 * a delegate action and the game server relays it, paying the gas and the small deposits a listing needs
 * (backend/server/src/chain/relay.ts): the player never needs NEAR. Calls the relay doesn't take, and every call
 * from a NEAR wallet app, go through the wallet as before.
 */
import { config } from '../config';
import { GameError, relayDelegate } from '../api/game';
import { TGAS } from '../lib/amount';
import { wireOf } from './delegate';
import { getConnector } from './near';
import { IN_PAGE_WALLETS } from './privyWallet';

export interface Call {
  type: 'FunctionCall';
  params: { methodName: string; args: object; gas: string; deposit: string };
}
export interface Tx {
  receiverId: string;
  actions: Call[];
}

const { items, market, farm } = config.contracts;
const MILLI_NEAR = 10n ** 21n;
const G100 = BigInt(TGAS(100));
const G300 = BigInt(TGAS(300));
/** The seller's own market calls, 1 yoctoNEAR each (relay.ts MARKET_OWN_CALLS). */
const MARKET_OWN_CALLS = new Set(['delist', 'update_price', 'offer_cancel', 'rent_withdraw', 'rent_end']);

/** The relay's policy, so calls it would refuse go straight to the wallet. Keep in step with relay.ts ruleFor. */
export function sponsorable(accountId: string, t: Tx): boolean {
  if (t.actions.length !== 1 || t.actions[0].type !== 'FunctionCall') return false;
  const { methodName: m, args, gas, deposit } = t.actions[0].params;
  const a = args as Record<string, unknown>;
  const d = BigInt(deposit);
  const g = BigInt(gas);
  const own = a.account_id === undefined || a.account_id === accountId;
  if (t.receiverId === items && (m === 'redeem' || m === 'mint_with_voucher')) return d === 0n && g <= G100;
  if (t.receiverId === items && m === 'nft_approve') return a.account_id === market && typeof a.token_id === 'string' && d > 0n && d <= 2n * MILLI_NEAR && g <= G300;
  if (t.receiverId === items && m === 'nft_transfer_call') return a.receiver_id === market && typeof a.token_id === 'string' && d === 1n && g <= G300;
  if (t.receiverId === market && m === 'storage_deposit') return own && d > 0n && d <= 50n * MILLI_NEAR && g <= G100;
  if (t.receiverId === market && MARKET_OWN_CALLS.has(m)) return d === 1n && g <= G300;
  if (t.receiverId === market && m === 'withdraw_owed') return d === 0n && g <= G100;
  if (t.receiverId === farm && m === 'ft_transfer_call') return (a.receiver_id === market || a.receiver_id === items) && d === 1n && g <= G300;
  if (t.receiverId === farm && m === 'storage_deposit') return own && d > 0n && d <= 10n * MILLI_NEAR && g <= G100;
  return false;
}

/** The relay said no before sending anything: the wallet can still send it. */
const REFUSED = new Set(['PERMISSION_DENIED', 'RATE_LIMITED', 'INVALID_ARGUMENT', 'UNAUTHENTICATED', 'MAINTENANCE', 'no_api']);

/** An outcome shaped like a wallet's, so market.ts `check` reads relayed calls the same way. */
const outcomeOf = (txHash: string, logs: string[]) => ({ transaction: { hash: txHash }, transaction_outcome: { id: txHash }, receipts_outcome: [{ outcome: { logs } }] });

/**
 * Sends the calls in order and returns their outcomes. With one of our in-page wallets, each call the relay takes is
 * relayed; anything else (or a call the relay turned down) is signed and sent by the wallet.
 */
export async function sendAll(transactions: Tx[]): Promise<unknown[]> {
  const wallet = await (await getConnector()).wallet();
  const accountId = IN_PAGE_WALLETS.has(wallet.manifest.id) && config.gameApiUrl ? (await wallet.getAccounts())[0]?.accountId : undefined;
  if (!accountId) {
    const out = await wallet.signAndSendTransactions({ transactions: transactions as never });
    return Array.isArray(out) ? out : [out];
  }
  const outcomes: unknown[] = [];
  for (const t of transactions) {
    if (sponsorable(accountId, t)) {
      try {
        const { signedDelegateActions } = await wallet.signDelegateActions({ delegateActions: [t as never] });
        const { txHash, logs } = await relayDelegate(accountId, wireOf(signedDelegateActions[0]));
        outcomes.push(outcomeOf(txHash, logs));
        continue;
      } catch (e) {
        if (!(e instanceof GameError)) throw e;
        if (e.code === 'CONFLICT') throw new Error(String(e.params.reason ?? 'The transaction failed.'));
        if (!REFUSED.has(e.code)) throw new Error('We couldn’t confirm that went through. Refresh in a minute to check before trying again.');
        console.warn(`[farmverse] not sponsored (${e.code}): sending it from your account`);
      }
    }
    outcomes.push(await wallet.signAndSendTransaction(t as never));
  }
  return outcomes;
}

/** One call; its outcome. */
export async function send(t: Tx): Promise<unknown> {
  const [outcome] = await sendAll([t]);
  return outcome;
}
