/**
 * Contract calls for the marketplace, matching chain/contracts (items, market, farm_token) and
 * docs 14 §3. Change calls go through the connected wallet and are checked for failure; reads go
 * over RPC.
 */
import { config } from '../config';
import { fromYocto, nearToYocto, ONE_YOCTO, TGAS, toYocto } from '../lib/amount';
import { getConnector, view } from './near';

const { items, market, farm } = config.contracts;

export interface Listing {
  nft_contract_id: string;
  token_id: string;
  owner_id: string;
  approval_id: number;
  price: string;
  listed_at_ms: number;
}

export interface MarketConfig {
  fee_bps: number;
  paused: boolean;
  treasury_id: string;
  ft_token_id: string;
}

/** What one listing reserves from the seller's market storage deposit (STORAGE_PER_LISTING = 0.01 NEAR). */
const STORAGE_PER_LISTING = nearToYocto('0.01');
/** Approval storage on the items contract (NEP-178 nft_approve is payable). */
const APPROVAL_DEPOSIT = nearToYocto('0.002');

const call = (methodName: string, args: object, deposit = ONE_YOCTO, gas = TGAS(100)) => ({
  type: 'FunctionCall' as const,
  params: { methodName, args, gas, deposit },
});

async function wallet() {
  const c = await getConnector();
  return c.wallet();
}

/* ---------- outcomes ---------- */

interface Outcome {
  status?: unknown;
  transaction_outcome?: { outcome?: { status?: unknown } };
  receipts_outcome?: { outcome?: { logs?: string[]; status?: unknown } }[];
}

/** The contract's own words from a failed call ("Smart contract panicked: Not listed" → "Not listed"). */
function failureText(failure: unknown): string {
  const raw = JSON.stringify(failure) ?? '';
  const m = raw.match(/"ExecutionError":"([^"]+)"/) ?? raw.match(/"(?:message|error_message)":"([^"]+)"/);
  return (m?.[1] ?? 'The transaction failed.').replace(/^Smart contract panicked:\s*/, '');
}

const failureOf = (status: unknown) => (status && typeof status === 'object' && 'Failure' in status ? (status as { Failure: unknown }).Failure : undefined);

/** Throws if the transaction (or any receipt it set off) failed; returns its event logs. */
export function check(result: unknown): string[] {
  const outcomes = (Array.isArray(result) ? result : [result]) as (Outcome | undefined)[];
  const logs: string[] = [];
  for (const o of outcomes) {
    if (!o) continue;
    const top = failureOf(o.status) ?? failureOf(o.transaction_outcome?.outcome?.status);
    if (top !== undefined) throw new Error(failureText(top));
    for (const r of o.receipts_outcome ?? []) {
      logs.push(...(r.outcome?.logs ?? []));
      const f = failureOf(r.outcome?.status);
      if (f !== undefined) throw new Error(failureText(f));
    }
  }
  return logs;
}

const emitted = (logs: string[], event: string) => logs.some((l) => l.startsWith('EVENT_JSON:') && l.includes(`"event":"${event}"`));

/* ---------- reads ---------- */

/** FARM balance of an account, as a plain number. */
export async function farmBalance(accountId: string): Promise<number> {
  const raw = await view<string>(farm, 'ft_balance_of', { account_id: accountId });
  return fromYocto(raw ?? '0');
}

export const getListing = (tokenId: string) => view<Listing | null>(market, 'get_listing', { nft_contract_id: items, token_id: tokenId });

export const marketConfig = () => view<MarketConfig>(market, 'get_config');

/* ---------- changes ---------- */

/** Sell: top up the market storage deposit if this listing needs it, then approve the market with a price. */
export async function list(accountId: string, tokenId: string, price: number) {
  const [deposit, count] = await Promise.all([
    view<string>(market, 'storage_balance_of', { account_id: accountId }),
    view<number>(market, 'listing_count_of', { account_id: accountId }),
  ]);
  const needed = BigInt(STORAGE_PER_LISTING) * BigInt(count + 1);
  const transactions = [];
  if (BigInt(deposit ?? '0') < needed) {
    transactions.push({ receiverId: market, actions: [call('storage_deposit', { account_id: accountId }, (needed - BigInt(deposit ?? '0')).toString(), TGAS(30))] });
  }
  transactions.push({
    receiverId: items,
    actions: [call('nft_approve', { token_id: tokenId, account_id: market, msg: JSON.stringify({ price: toYocto(price) }) }, APPROVAL_DEPOSIT, TGAS(100))],
  });
  check(await (await wallet()).signAndSendTransactions({ transactions }));
}

export async function updatePrice(tokenId: string, price: number) {
  check(
    await (await wallet()).signAndSendTransaction({
      receiverId: market,
      actions: [call('update_price', { nft_contract_id: items, token_id: tokenId, price: toYocto(price) })],
    }),
  );
}

export async function delist(tokenId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: market, actions: [call('delist', { nft_contract_id: items, token_id: tokenId })] }));
}

/** Buy: re-check the listing first so a stale price never reaches the wallet, then pay with FARM. */
export async function buy(tokenId: string, expectedPrice: number) {
  const listing = await getListing(tokenId);
  if (!listing) throw new Error('This item is no longer for sale.');
  if (Math.abs(fromYocto(listing.price) - expectedPrice) > 1e-9) throw new Error(`The price changed to ${fromYocto(listing.price)} FARM. Please check and try again.`);
  const logs = check(
    await (await wallet()).signAndSendTransaction({
      receiverId: farm,
      actions: [call('ft_transfer_call', { receiver_id: market, amount: listing.price, msg: JSON.stringify({ nft_contract_id: items, token_id: tokenId }) }, ONE_YOCTO, TGAS(300))],
    }),
  );
  // The FARM transfer succeeds either way; the market refunds it if the item couldn't move.
  if (emitted(logs, 'market_sale_failed')) throw new Error('Someone else got this item first. Your FARM was refunded.');
}

/** Use in game: burns the item; the game picks it up from the redeem event. Not for workers. */
export async function redeem(tokenId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: items, actions: [call('redeem', { token_id: tokenId })] }));
}

export async function send(tokenId: string, receiverId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: items, actions: [call('nft_transfer', { receiver_id: receiverId, token_id: tokenId })] }));
}
