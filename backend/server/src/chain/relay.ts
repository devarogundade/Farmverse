/**
 * Sponsored transactions (plan 15 §14): players don't need NEAR for gas.
 *
 * - **Relay (NEP-366 meta-transactions):** the game page signs a delegate action with the player's session key
 *   (a function-call key on their account, limited to the items contract's `mint_with_voucher` and `redeem`). The
 *   server checks it is exactly that — one deposit-free call, for the signed-in account, to our items contract —
 *   and sends it from the relayer account, which pays the gas. The relayer key can't move anyone's assets: it only
 *   wraps what the player's own key signed.
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

export interface Relayer {
  readonly accountId: string;
  blockHeight(): Promise<number>;
  relay(signed: SignedDelegate): Promise<string>;
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
  /** Per-account rate limit: true when allowed. */
  readonly allow: (accountId: string) => Promise<boolean>;
}

/** `chain-relay`: checks the policy, then sends the player's signed call with the relayer paying gas. */
export async function relay(deps: RelayDeps, accountId: string, w: DelegateWire, log: Logger): Promise<{ txHash: string }> {
  if (w.senderId !== accountId) fail('PERMISSION_DENIED');
  if (w.receiverId !== deps.itemsContract || !SPONSORED_METHODS.has(w.method)) fail('PERMISSION_DENIED');
  if (BigInt(w.deposit) !== 0n) fail('PERMISSION_DENIED');
  if (BigInt(w.gas) > MAX_RELAY_GAS) fail('INVALID_ARGUMENT');
  const height = await deps.relayer.blockHeight();
  const max = Number(w.maxBlockHeight);
  if (!(max > height && max <= height + MAX_TTL_BLOCKS)) fail('INVALID_ARGUMENT');
  if (!(await deps.allow(accountId))) fail('RATE_LIMITED');
  const signed = signedDelegateOf(w);
  try {
    const txHash = await deps.relayer.relay(signed);
    log.info('relayed', { accountId, method: w.method, txHash });
    return { txHash };
  } catch (e) {
    log.warn('relay failed', { accountId, method: w.method, error: String(e) });
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
        receipts_outcome?: { outcome?: { status?: unknown } }[];
      };
      // the outer transaction succeeds even when the player's call inside failed: look at every receipt
      const failed = (out.receipts_outcome ?? []).find((r) => typeof r.outcome?.status === 'object' && r.outcome?.status !== null && 'Failure' in (r.outcome.status as object));
      if (failed) throw new Error(`relayed call failed: ${JSON.stringify((failed.outcome?.status as { Failure: unknown }).Failure).slice(0, 300)}`);
      return out.transaction?.hash ?? '';
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
