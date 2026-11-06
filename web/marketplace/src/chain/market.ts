/**
 * Contract calls for the marketplace, matching chain/contracts (items, market, farm_token) and
 * docs 14 §3. Change calls go through the connected wallet; reads go over RPC.
 */
import { config } from '../config';
import { nearToYocto, ONE_YOCTO, TGAS, fromYocto, toYocto } from '../lib/amount';
import { getConnector, view } from './near';

const { items, market, farm } = config.contracts;

interface Listing {
  nft_contract_id: string;
  token_id: string;
  owner_id: string;
  approval_id: number;
  price: string;
  listed_at_ms: number;
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

/** FARM balance of an account, as a plain number. */
export async function farmBalance(accountId: string): Promise<number> {
  const raw = await view<string>(farm, 'ft_balance_of', { account_id: accountId });
  return fromYocto(raw ?? '0');
}

export const getListing = (tokenId: string) => view<Listing | null>(market, 'get_listing', { nft_contract_id: items, token_id: tokenId });

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
  return (await wallet()).signAndSendTransactions({ transactions });
}

export async function updatePrice(tokenId: string, price: number) {
  return (await wallet()).signAndSendTransaction({
    receiverId: market,
    actions: [call('update_price', { nft_contract_id: items, token_id: tokenId, price: toYocto(price) })],
  });
}

export async function delist(tokenId: string) {
  return (await wallet()).signAndSendTransaction({ receiverId: market, actions: [call('delist', { nft_contract_id: items, token_id: tokenId })] });
}

/** Buy: re-check the listing first so a stale price never reaches the wallet, then pay with FARM. */
export async function buy(tokenId: string, expectedPrice: number) {
  const listing = await getListing(tokenId);
  if (!listing) throw new Error('This item is no longer for sale.');
  if (Math.abs(fromYocto(listing.price) - expectedPrice) > 1e-9) throw new Error(`The price changed to ${fromYocto(listing.price)} FARM. Please check and try again.`);
  return (await wallet()).signAndSendTransaction({
    receiverId: farm,
    actions: [call('ft_transfer_call', { receiver_id: market, amount: listing.price, msg: JSON.stringify({ nft_contract_id: items, token_id: tokenId }) }, ONE_YOCTO, TGAS(300))],
  });
}

/** Use in game: burns the item; the game picks it up from the redeem event. Not for workers. */
export async function redeem(tokenId: string) {
  return (await wallet()).signAndSendTransaction({ receiverId: items, actions: [call('redeem', { token_id: tokenId })] });
}

export async function send(tokenId: string, receiverId: string) {
  return (await wallet()).signAndSendTransaction({ receiverId: items, actions: [call('nft_transfer', { receiver_id: receiverId, token_id: tokenId })] });
}
