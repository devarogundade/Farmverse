/**
 * Contracts v2 from the marketplace (plan 15 §5, §12): buying drops, on-chain offers and worker
 * rentals. Like chain/market.ts, every change goes through the connected wallet and is checked for
 * failure; FARM always travels by `ft_transfer_call`, so a refused purchase refunds itself.
 */
import { config } from '../config';
import { nearToYocto, ONE_YOCTO, TGAS, toYocto } from '../lib/amount';
import { check, farmRegistration } from './market';
import { getConnector, view } from './near';

const { items, market, farm } = config.contracts;

/** Each open listing, offer or rental reserves this much of the account's market storage deposit. */
const STORAGE_PER_SLOT = BigInt(nearToYocto('0.01'));
/** Approval storage on the items contract (accepting an offer approves the market). */
const APPROVAL_DEPOSIT = nearToYocto('0.002');
const DAY_MS = 86_400_000;

const call = (methodName: string, args: object, deposit = ONE_YOCTO, gas = TGAS(100)) => ({
  type: 'FunctionCall' as const,
  params: { methodName, args, gas, deposit },
});

async function wallet() {
  return (await getConnector()).wallet();
}

const emitted = (logs: string[], event: string) => logs.some((l) => l.startsWith('EVENT_JSON:') && l.includes(`"event":"${event}"`));

/**
 * A market storage_deposit for one more open slot, or null if the deposit already covers it. The
 * market counts listings, offers and rentals alike; one slot of headroom covers an expired offer
 * nobody cleared yet.
 */
async function slotDeposit(accountId: string) {
  const [deposit, listings, offers, lent] = await Promise.all([
    view<string>(market, 'storage_balance_of', { account_id: accountId }),
    view<number>(market, 'listing_count_of', { account_id: accountId }),
    view<unknown[]>(market, 'offers_of', { account_id: accountId }).catch(() => []),
    view<unknown[]>(market, 'rentals_of_lender', { account_id: accountId }).catch(() => []),
  ]);
  const needed = STORAGE_PER_SLOT * BigInt(listings + offers.length + lent.length + 2);
  const have = BigInt(deposit ?? '0');
  if (have >= needed) return null;
  return { receiverId: market, actions: [call('storage_deposit', { account_id: accountId }, (needed - have).toString(), TGAS(30))] };
}

/* ---------- drops ---------- */

interface ChainDrop {
  id: string;
  price: string;
  cap: number | null;
  sold: number;
  paused: boolean;
  ends_at_ms: number | null;
}

/**
 * Buys one edition of a drop. The drop is read again first, so a price change or a sell-out shows
 * here instead of in the wallet. `use`: mint and redeem at once (the game does this; the market keeps it).
 */
export async function buyDrop(dropId: string, expectedPriceYocto: string, use = false) {
  const d = await view<ChainDrop | null>(items, 'get_drop', { id: dropId });
  if (!d) throw new Error('This isn’t for sale any more.');
  if (d.paused) throw new Error('This is paused for now. Please try again later.');
  if (d.cap != null && d.sold >= d.cap) throw new Error('Sold out!');
  if (d.ends_at_ms != null && Date.now() >= d.ends_at_ms) throw new Error('This sale has ended.');
  if (d.price !== expectedPriceYocto) throw new Error('The price just changed. Please check it and try again.');
  const logs = check(
    await (await wallet()).signAndSendTransaction({
      receiverId: farm,
      actions: [call('ft_transfer_call', { receiver_id: items, amount: d.price, msg: JSON.stringify({ drop: dropId, use }) }, ONE_YOCTO, TGAS(300))],
    }),
  );
  if (!emitted(logs, 'drop_sale')) throw new Error('It didn’t go through. Your FARM was returned.');
}

/* ---------- offers ---------- */

/** Escrows `amount` FARM as an offer on a token for `days` days (a new one replaces your old one there). */
export async function makeOffer(accountId: string, tokenId: string, amount: number, days: number) {
  const transactions = [];
  const slot = await slotDeposit(accountId);
  if (slot) transactions.push(slot);
  const msg = { offer: { nft_contract_id: items, token_id: tokenId, expires_at_ms: Date.now() + days * DAY_MS } };
  transactions.push({
    receiverId: farm,
    actions: [call('ft_transfer_call', { receiver_id: market, amount: toYocto(amount), msg: JSON.stringify(msg) }, ONE_YOCTO, TGAS(100))],
  });
  const logs = check(await (await wallet()).signAndSendTransactions({ transactions }));
  if (!emitted(logs, 'offer_make')) throw new Error('The offer didn’t go through. Your FARM was returned.');
}

/** Withdraws your offer; its FARM comes back. */
export async function cancelOffer(offerId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: market, actions: [call('offer_cancel', { offer_id: Number(offerId) }, ONE_YOCTO, TGAS(60))] }));
}

/** Sells a token to an offer: approves the market for it with the offer's id. */
export async function acceptOffer(accountId: string, tokenId: string, offerId: string) {
  const transactions = [];
  const register = await farmRegistration(accountId);
  if (register) transactions.push(register);
  transactions.push({
    receiverId: items,
    actions: [call('nft_approve', { token_id: tokenId, account_id: market, msg: JSON.stringify({ accept_offer: Number(offerId) }) }, APPROVAL_DEPOSIT, TGAS(300))],
  });
  const logs = check(await (await wallet()).signAndSendTransactions({ transactions }));
  if (emitted(logs, 'offer_accept_failed') || !emitted(logs, 'offer_accept')) throw new Error('That offer isn’t open any more.');
}

/* ---------- rentals ---------- */

/** Hands a worker to the market to rent out. It keeps working for you until someone rents it. */
export async function rentOut(accountId: string, tokenId: string, pricePerDay: number, maxDays: number) {
  const transactions = [];
  const register = await farmRegistration(accountId); // so earnings can be paid out
  if (register) transactions.push(register);
  const slot = await slotDeposit(accountId);
  if (slot) transactions.push(slot);
  const msg = { rent: { price_per_day: toYocto(pricePerDay), max_days: maxDays } };
  transactions.push({
    receiverId: items,
    actions: [call('nft_transfer_call', { receiver_id: market, token_id: tokenId, msg: JSON.stringify(msg) }, ONE_YOCTO, TGAS(300))],
  });
  const logs = check(await (await wallet()).signAndSendTransactions({ transactions }));
  if (!emitted(logs, 'rent_list')) throw new Error('Only workers can be rented out. It’s back in your wallet.');
}

/** Rents a worker for `days` days: it works on your island until then. */
export async function rent(tokenId: string, pricePerDayYocto: string, days: number) {
  const amount = (BigInt(pricePerDayYocto) * BigInt(days)).toString();
  const msg = { rent: { nft_contract_id: items, token_id: tokenId, days } };
  const logs = check(
    await (await wallet()).signAndSendTransaction({
      receiverId: farm,
      actions: [call('ft_transfer_call', { receiver_id: market, amount, msg: JSON.stringify(msg) }, ONE_YOCTO, TGAS(100))],
    }),
  );
  if (!emitted(logs, 'rent_start')) throw new Error('Someone hired this worker first. Your FARM was returned.');
}

/** Sends what the worker earned to its owner. */
export async function rentWithdraw(tokenId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: market, actions: [call('rent_withdraw', { nft_contract_id: items, token_id: tokenId }, ONE_YOCTO, TGAS(100))] }));
}

/** Takes the worker back (only when nobody is renting it right now). */
export async function rentEnd(tokenId: string) {
  check(await (await wallet()).signAndSendTransaction({ receiverId: market, actions: [call('rent_end', { nft_contract_id: items, token_id: tokenId }, ONE_YOCTO, TGAS(150))] }));
}
