/**
 * NEP-366 delegate actions for our own in-page wallets (Email or Google, and a guest farm through the key bridge):
 * the player's key signs a call, our game server relays it and pays the gas (backend/server/src/chain/relay.ts).
 *
 * Wallets hand signed delegate actions over the NEAR Connect way (`signDelegateActions` → base64 borsh). The relay
 * takes them as plain fields, so `wireOf` reads one back: one FunctionCall, which is all the relay accepts.
 */
import { buildDelegateAction, encodeSignedDelegate, type Account, type Signer } from 'near-api-js';

/** How many blocks a signed delegate action stays valid (the relay takes at most 600). */
const TTL_BLOCKS = 300n;

export type DelegateList<A> = { receiverId: string; actions: A[] }[];

/**
 * Signs each call as a delegate action with the account's key. Nonces count on from the key's on-chain nonce and from
 * what this page signed before, so calls signed back to back don't collide while the chain catches up.
 */
export async function signDelegates<A>(
  account: Account,
  signer: Signer,
  list: DelegateList<A>,
  toAction: (a: A) => unknown,
  lastNonce: Map<string, bigint>,
): Promise<string[]> {
  const publicKey = await signer.getPublicKey();
  const [ak, block] = await Promise.all([account.getAccessKey(publicKey), account.provider.viewBlock({ finality: 'final' })]);
  const slot = `${account.accountId}:${publicKey.toString()}`;
  let nonce = BigInt(ak.nonce);
  const signedBefore = lastNonce.get(slot) ?? 0n;
  if (signedBefore > nonce) nonce = signedBefore;
  const maxBlockHeight = BigInt(block.header.height) + TTL_BLOCKS;
  const out: string[] = [];
  for (const d of list) {
    nonce += 1n;
    const delegateAction = buildDelegateAction({
      senderId: account.accountId,
      receiverId: d.receiverId,
      actions: d.actions.map(toAction) as never,
      nonce,
      maxBlockHeight,
      publicKey,
    });
    const { signedDelegate } = await signer.signDelegateAction(delegateAction);
    out.push(toBase64(encodeSignedDelegate(signedDelegate)));
  }
  lastNonce.set(slot, nonce);
  return out;
}

/** What the relay takes (backend/server/src/chain/relay.ts DelegateWire). */
export interface DelegateWire {
  senderId: string;
  receiverId: string;
  method: string;
  args: string;
  gas: string;
  deposit: string;
  nonce: string;
  maxBlockHeight: string;
  publicKey: string;
  signature: string;
}

/** Reads a signed delegate action (base64 borsh) holding one FunctionCall back into the relay's fields. */
export function wireOf(signedDelegateBase64: string): DelegateWire {
  const r = reader(fromBase64(signedDelegateBase64));
  const senderId = r.str();
  const receiverId = r.str();
  if (r.u32() !== 1) throw new Error('Only one call at a time can be sponsored.');
  if (r.u8() !== 2) throw new Error('Only contract calls can be sponsored.'); // Action::FunctionCall
  const method = r.str();
  const args = r.bytes(r.u32());
  const gas = r.uint(8);
  const deposit = r.uint(16);
  const nonce = r.uint(8);
  const maxBlockHeight = r.uint(8);
  if (r.u8() !== 0) throw new Error('Only ed25519 keys can sign here.');
  const publicKey = `ed25519:${base58(r.bytes(32))}`;
  if (r.u8() !== 0) throw new Error('Only ed25519 keys can sign here.');
  const signature = r.bytes(64);
  r.end();
  return {
    senderId,
    receiverId,
    method,
    args: toBase64(args),
    gas: gas.toString(),
    deposit: deposit.toString(),
    nonce: nonce.toString(),
    maxBlockHeight: maxBlockHeight.toString(),
    publicKey,
    signature: toBase64(signature),
  };
}

function reader(b: Uint8Array) {
  let at = 0;
  const take = (n: number) => {
    if (at + n > b.length) throw new Error('The signed call is cut short.');
    const out = b.subarray(at, at + n);
    at += n;
    return out;
  };
  const uint = (n: number) => take(n).reduceRight((acc, byte) => (acc << 8n) | BigInt(byte), 0n);
  const u32 = () => Number(uint(4));
  return {
    u8: () => take(1)[0],
    u32,
    uint,
    bytes: (n: number) => take(n),
    str: () => new TextDecoder().decode(take(u32())),
    end: () => {
      if (at !== b.length) throw new Error('The signed call has extra bytes.');
    },
  };
}

/** An implicit account's key (its id is the key in hex). */
export function implicitPublicKey(accountId: string): string {
  const bytes = Uint8Array.from(accountId.match(/../g) ?? [], (h) => parseInt(h, 16));
  return `ed25519:${base58(bytes)}`;
}

const B58 = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

function base58(bytes: Uint8Array): string {
  let n = 0n;
  for (const b of bytes) n = (n << 8n) | BigInt(b);
  let out = '';
  while (n > 0n) {
    out = B58[Number(n % 58n)] + out;
    n /= 58n;
  }
  for (const b of bytes) {
    if (b !== 0) break;
    out = '1' + out;
  }
  return out;
}

export const toBase64 = (bytes: Uint8Array) => {
  let s = '';
  for (const b of bytes) s += String.fromCharCode(b);
  return btoa(s);
};

export const fromBase64 = (b64: string) => Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
