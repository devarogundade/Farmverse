/**
 * Sponsored transactions (plan 15 §14): players don't need NEAR for gas.
 *
 * - **Relay (NEP-366 meta-transactions):** the game page signs a delegate action with the player's session key
 *   (a function-call key on their account, limited to the items contract's `mint_with_voucher` and `redeem`). The
 *   server checks it is exactly that — one deposit-free call, for the signed-in account, to our items contract —
 *   and sends it from the relayer account, which pays the gas. The relayer key can't move anyone's assets: it only
 *   wraps what the player's own key signed.
 * - **Market actions** (guest farms and Email or Google accounts, whose keys are full-access and so can sign calls
 *   with a deposit): paying with FARM (`ft_transfer_call` to the market or, for a drop, the items contract; exactly
 *   1 yoctoNEAR), approving the market to sell a token (`nft_approve`), handing a worker to the market to rent out,
 *   the seller's own 1 yoctoNEAR market calls (delist, price, cancel an offer, end a rental, collect), and the
 *   storage deposits a listing needs (market slots, FARM registration). In a
 *   relayed call the relayer pays the attached deposit as well as the gas, and a failed call refunds that deposit to
 *   the player, not the relayer (docs.near.org/protocol/transactions/meta-tx). So each deposit has a cap per call
 *   and counts against a daily NEAR budget per account, on top of the per-account rate limit.
 * - **Welcome top-up:** a new account gets a little NEAR once, so the one-time session-key approval and FARM
 *   payments (which need 1 yoctoNEAR by the token standard) work without the player ever buying NEAR.
 */
import { createHash } from 'node:crypto';
import { Account, actions, JsonRpcProvider, type KeyPairString, PublicKey, Signature, type SignedDelegate, buildDelegateAction, encodeDelegateAction, encodeSignedDelegate } from 'near-api-js';
import { AppError, fail } from '../../../game/src/core/errors';
import type { Logger } from '../../../game/src/core/logger';
import type { Env } from '../../../game/src/game/services';

/** The calls a session key may make, and through the relayer. */
export const SPONSORED_METHODS = new Set(['mint_with_voucher', 'redeem']);
export const MAX_RELAY_GAS = 100_000_000_000_000n; // 100 TGas
/** Market calls go further (a purchase runs the market, the items contract and their callbacks). */
export const MAX_MARKET_GAS = 300_000_000_000_000n; // 300 TGas
const MILLI_NEAR = 10n ** 21n;
/** Approval storage the items contract takes for the market (the marketplace attaches the exact cost, ~0.0004 NEAR). */
export const MAX_APPROVE_DEPOSIT = 2n * MILLI_NEAR;
/** One market storage top-up: a few listing slots (0.01 NEAR each). */
export const MAX_MARKET_STORAGE_DEPOSIT = 50n * MILLI_NEAR;
/** FARM registration (NEP-145; the token's minimum is 0.00125 NEAR). */
export const MAX_FARM_STORAGE_DEPOSIT = 10n * MILLI_NEAR;
/** A seller's own market calls (1 yoctoNEAR each): a guest's welcome NEAR can't cover their gas otherwise. */
export const MARKET_OWN_CALLS = new Set(['delist', 'update_price', 'offer_cancel', 'rent_withdraw', 'rent_end']);
/** How far ahead a delegate action may expire (blocks): short, so a leaked one dies quickly. */
export const MAX_TTL_BLOCKS = 600;

/** A delegate action as the page sends it (plain JSON; args as base64 bytes so both sides encode the same). */
export interface DelegateWire {
  senderId: string;
  receiverId: string;
  method: string;
  /** base64 of the exact args bytes the key signed */
  args: string;
  gas: string;
  deposit: string;
  nonce: string;
  maxBlockHeight: string;
  publicKey: string;
  /** base64 ed25519 signature over sha256(encoded delegate action) */
  signature: string;
}

/** The relayed call ran and the contract refused it (its own words in `reason`). */
export class RelayedCallFailed extends Error {
  constructor(
    readonly reason: string,
    readonly txHash = '',
  ) {
    super(`relayed call failed${txHash ? ` (${txHash})` : ''}: ${reason}`);
  }
}

export interface Relayer {
  readonly accountId: string;
  blockHeight(): Promise<number>;
  /** Sends it and waits for every receipt; throws RelayedCallFailed when one failed. */
  relay(signed: SignedDelegate): Promise<{ txHash: string; logs: string[] }>;
  balanceOf(accountId: string): Promise<bigint | null>;
  send(accountId: string, yocto: bigint): Promise<void>;
}

/** Rebuilds the delegate action and checks the signature locally (the chain checks again). */
export function signedDelegateOf(w: DelegateWire): SignedDelegate {
  const delegateAction = buildDelegateAction({
    senderId: w.senderId,
    receiverId: w.receiverId,
    actions: [actions.functionCall(w.method, Buffer.from(w.args, 'base64'), BigInt(w.gas), BigInt(w.deposit))],
    nonce: BigInt(w.nonce),
    maxBlockHeight: BigInt(w.maxBlockHeight),
    publicKey: PublicKey.fromString(w.publicKey),
  });
  const sig = Buffer.from(w.signature, 'base64');
  if (sig.length !== 64) fail('INVALID_ARGUMENT');
  const hash = createHash('sha256').update(encodeDelegateAction(delegateAction)).digest();
  if (!PublicKey.fromString(w.publicKey).verify(hash, sig)) fail('UNAUTHENTICATED');
  // the class isn't exported at runtime; the relayer only reads these two fields
  const signed = { delegateAction, signature: new Signature({ keyType: 0, data: sig }) } as unknown as SignedDelegate;
  encodeSignedDelegate(signed); // throws now, not on chain, if the shape is wrong
  return signed;
}

export interface RelayDeps {
  readonly env: Env;
  readonly relayer: Relayer;
  readonly itemsContract: string;
  /** The market and FARM: market calls are sponsored too when both are set. */
  readonly marketContract?: string;
  readonly farmContract?: string;
  /** Deposits (yoctoNEAR) the relayer pays for one account per UTC day; 0 or unset = none. */
  readonly depositBudget?: bigint;
  /** Per-account rate limit: true when allowed. */
  readonly allow: (accountId: string) => Promise<boolean>;
}

/** What one sponsored call may be: its gas, its deposit and what its arguments must say. */
export interface RelayRule {
  readonly maxGas: bigint;
  readonly deposit: (yocto: bigint) => boolean;
  readonly args?: (a: Record<string, unknown>) => boolean;
}

/** The rule for this call, or null: nothing else is relayed. */
export function ruleFor(deps: Pick<RelayDeps, 'itemsContract' | 'marketContract' | 'farmContract'>, w: DelegateWire): RelayRule | null {
  const { itemsContract: items, marketContract: market, farmContract: farm } = deps;
  if (w.receiverId === items && SPONSORED_METHODS.has(w.method)) return { maxGas: MAX_RELAY_GAS, deposit: (d) => d === 0n };
  if (!market || !farm) return null;
  // storage for the signer only (an unset account_id means the caller)
  const own = (a: Record<string, unknown>) => a.account_id === undefined || a.account_id === w.senderId;
  // approve the market, and only the market, to sell the token or hand it to an offer
  if (w.receiverId === items && w.method === 'nft_approve') {
    return { maxGas: MAX_MARKET_GAS, deposit: (d) => d > 0n && d <= MAX_APPROVE_DEPOSIT, args: (a) => a.account_id === market && typeof a.token_id === 'string' };
  }
  // hand a worker to the market to rent out (the market holds it; it comes back on rent_end)
  if (w.receiverId === items && w.method === 'nft_transfer_call') {
    return { maxGas: MAX_MARKET_GAS, deposit: (d) => d === 1n, args: (a) => a.receiver_id === market && typeof a.token_id === 'string' };
  }
  // the seller's listing slots on the market
  if (w.receiverId === market && w.method === 'storage_deposit') {
    return { maxGas: MAX_RELAY_GAS, deposit: (d) => d > 0n && d <= MAX_MARKET_STORAGE_DEPOSIT, args: own };
  }
  // the seller's own housekeeping (the market checks it's theirs; nothing goes anywhere but back to them)
  if (w.receiverId === market && MARKET_OWN_CALLS.has(w.method)) return { maxGas: MAX_MARKET_GAS, deposit: (d) => d === 1n };
  if (w.receiverId === market && w.method === 'withdraw_owed') return { maxGas: MAX_RELAY_GAS, deposit: (d) => d === 0n };
  if (w.receiverId !== farm) return null;
  // pay with FARM: the market (a purchase, an offer, a rental) or the items contract (a drop); 1 yoctoNEAR, as the
  // token standard wants
  if (w.method === 'ft_transfer_call') {
    return { maxGas: MAX_MARKET_GAS, deposit: (d) => d === 1n, args: (a) => a.receiver_id === market || a.receiver_id === items };
  }
  // FARM registration, so a sale's FARM reaches the seller
  if (w.method === 'storage_deposit') return { maxGas: MAX_RELAY_GAS, deposit: (d) => d > 0n && d <= MAX_FARM_STORAGE_DEPOSIT, args: own };
  return null;
}

function argsOf(w: DelegateWire): Record<string, unknown> {
  try {
    const a: unknown = JSON.parse(Buffer.from(w.args, 'base64').toString('utf8'));
    if (a && typeof a === 'object' && !Array.isArray(a)) return a as Record<string, unknown>;
  } catch {
    // not JSON
  }
  return fail('PERMISSION_DENIED');
}

const DAY_MS = 86_400_000;

/** Books `yocto` against the account's deposit budget for today (UTC); false when it would go over. */
async function bookDeposit(env: Env, accountId: string, yocto: bigint, budget: bigint): Promise<boolean> {
  const day = Math.floor(env.clock.nowMs() / DAY_MS);
  const mark = `ops/relaydeposit_${createHash('sha256').update(accountId).digest('hex').slice(0, 40)}_${day}`;
  return env.store.runTransaction(async (tx) => {
    const doc = await tx.get(mark);
    const spent = doc.exists ? BigInt(String((doc.data as { spent?: unknown } | undefined)?.spent ?? '0')) : 0n;
    if (spent + yocto > budget) return false;
    tx.set(mark, { accountId, day, spent: (spent + yocto).toString() });
    return true;
  });
}

/**
 * `chain-relay`: checks the policy, then sends the player's signed call with the relayer paying the gas (and, for a
 * market call, its deposit). The call's logs come back so the marketplace can tell a sale from a refund.
 */
export async function relay(deps: RelayDeps, accountId: string, w: DelegateWire, log: Logger): Promise<{ txHash: string; logs: string[] }> {
  if (w.senderId !== accountId) fail('PERMISSION_DENIED');
  const rule = ruleFor(deps, w);
  if (!rule) fail('PERMISSION_DENIED');
  const deposit = BigInt(w.deposit);
  if (!rule.deposit(deposit)) fail('PERMISSION_DENIED');
  if (rule.args && !rule.args(argsOf(w))) fail('PERMISSION_DENIED');
  if (BigInt(w.gas) > rule.maxGas) fail('INVALID_ARGUMENT');
  const height = await deps.relayer.blockHeight();
  const max = Number(w.maxBlockHeight);
  if (!(max > height && max <= height + MAX_TTL_BLOCKS)) fail('INVALID_ARGUMENT');
  const signed = signedDelegateOf(w);
  if (!(await deps.allow(accountId))) fail('RATE_LIMITED');
  // 1 yoctoNEAR (a FARM payment) doesn't count; storage and approvals do, even when the call then fails
  if (deposit > 1n && !(await bookDeposit(deps.env, accountId, deposit, deps.depositBudget ?? 0n))) fail('RATE_LIMITED');
  try {
    const { txHash, logs } = await deps.relayer.relay(signed);
    log.info('relayed', { accountId, receiverId: w.receiverId, method: w.method, txHash });
    return { txHash, logs };
  } catch (e) {
    log.warn('relay failed', { accountId, receiverId: w.receiverId, method: w.method, error: String(e) });
    // the call ran and the contract said no: its words, for the market to show. Anything else: we can't tell.
    if (e instanceof RelayedCallFailed) throw new AppError('CONFLICT', { reason: e.reason.slice(0, 200) }, { cause: e });
    throw new AppError('INTERNAL', {}, { cause: e });
  }
}

/** Gives a new account a little NEAR once (`ops/welcome_<account>` marks it), if it holds less than `below`. */
export async function welcome(env: Env, relayer: Relayer, accountId: string, amount: bigint, below: bigint, log: Logger): Promise<boolean> {
  const mark = `ops/welcome_${createHash('sha256').update(accountId).digest('hex').slice(0, 40)}`;
  const booked = await env.store.runTransaction(async (tx) => {
    if ((await tx.get(mark)).exists) return false;
    tx.set(mark, { accountId, at: env.clock.nowMs() });
    return true;
  });
  if (!booked) return false;
  try {
    const balance = await relayer.balanceOf(accountId);
    if (balance !== null && balance >= below) return false;
    await relayer.send(accountId, amount);
    log.info('welcome NEAR sent', { accountId });
    return true;
  } catch (e) {
    log.warn('welcome NEAR not sent', { accountId, error: String(e) });
    await env.store.runTransaction(async (tx) => tx.delete(mark));
    return false;
  }
}

/** The contract's own words from a failed receipt ("Smart contract panicked: Not listed" → "Not listed"). */
export function failureText(failure: unknown): string {
  const raw = JSON.stringify(failure) ?? '';
  const m = raw.match(/"ExecutionError":"([^"]+)"/) ?? raw.match(/"(?:message|error_message)":"([^"]+)"/);
  return (m?.[1] ?? raw.slice(0, 200)).replace(/^Smart contract panicked:\s*/, '');
}

/** The relayer account (near-api-js); pays gas for relayed calls and the welcome top-ups. */
export function nearRelayer(rpcUrl: string, accountId: string, privateKey: string): Relayer {
  const provider = new JsonRpcProvider({ url: rpcUrl });
  const account = new Account(accountId, provider, privateKey as KeyPairString);
  return {
    accountId,
    async blockHeight() {
      const b = await provider.viewBlock({ finality: 'final' });
      return Number(b.header.height);
    },
    async relay(signed) {
      const out = (await account.relayMetaTransaction(signed)) as {
        transaction?: { hash?: string };
        receipts_outcome?: { outcome?: { status?: unknown; logs?: string[] } }[];
      };
      // the outer transaction succeeds even when the player's call inside failed: look at every receipt
      const failed = (out.receipts_outcome ?? []).find((r) => typeof r.outcome?.status === 'object' && r.outcome?.status !== null && 'Failure' in (r.outcome.status as object));
      if (failed) throw new RelayedCallFailed(failureText((failed.outcome?.status as { Failure: unknown }).Failure), out.transaction?.hash ?? '');
      return { txHash: out.transaction?.hash ?? '', logs: (out.receipts_outcome ?? []).flatMap((r) => r.outcome?.logs ?? []) };
    },
    async balanceOf(target) {
      try {
        const a = await provider.viewAccount({ accountId: target });
        return BigInt((a as { amount: string | bigint }).amount);
      } catch {
        return null; // doesn't exist yet (an implicit account before its first NEAR)
      }
    },
    async send(target, yocto) {
      await account.transfer({ receiverId: target, amount: yocto });
    },
  };
}
