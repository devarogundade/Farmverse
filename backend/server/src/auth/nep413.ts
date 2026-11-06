import { createHash, createPublicKey, verify } from 'node:crypto';

/**
 * NEP-413 (signed messages, "Sign in with NEAR"). The wallet signs
 * `sha256(u32le(2^31 + 413) ‖ borsh({ message, nonce[32], recipient, callbackUrl? }))` with one of the
 * account's keys. https://github.com/near/NEPs/blob/master/neps/nep-0413.md
 */
export const NEP413_TAG = 2 ** 31 + 413;

export interface SignedMessage {
  readonly accountId: string;
  /** `ed25519:<base58>` */
  readonly publicKey: string;
  /** Base64 signature. */
  readonly signature: string;
}

export interface Nep413Payload {
  readonly message: string;
  readonly nonce: Uint8Array;
  readonly recipient: string;
  readonly callbackUrl?: string | null;
}

function borshString(s: string): Buffer {
  const bytes = Buffer.from(s, 'utf8');
  const len = Buffer.alloc(4);
  len.writeUInt32LE(bytes.length);
  return Buffer.concat([len, bytes]);
}

/** The exact bytes a wallet hashes and signs for `payload`. */
export function nep413Bytes(payload: Nep413Payload): Buffer {
  if (payload.nonce.length !== 32) throw new Error('nonce must be 32 bytes');
  const tag = Buffer.alloc(4);
  tag.writeUInt32LE(NEP413_TAG);
  const callback =
    payload.callbackUrl == null ? Buffer.from([0]) : Buffer.concat([Buffer.from([1]), borshString(payload.callbackUrl)]);
  return Buffer.concat([tag, borshString(payload.message), Buffer.from(payload.nonce), borshString(payload.recipient), callback]);
}

export function nep413Hash(payload: Nep413Payload): Buffer {
  return createHash('sha256').update(nep413Bytes(payload)).digest();
}

const ALPHABET = '123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz';

export function base58Decode(text: string): Buffer {
  let n = 0n;
  for (const ch of text) {
    const i = ALPHABET.indexOf(ch);
    if (i < 0) throw new Error('not base58');
    n = n * 58n + BigInt(i);
  }
  const bytes: number[] = [];
  while (n > 0n) {
    bytes.unshift(Number(n & 0xffn));
    n >>= 8n;
  }
  for (const ch of text) {
    if (ch !== '1') break;
    bytes.unshift(0);
  }
  return Buffer.from(bytes);
}

export function base58Encode(data: Uint8Array): string {
  let n = 0n;
  for (const b of data) n = (n << 8n) | BigInt(b);
  let out = '';
  while (n > 0n) {
    out = ALPHABET[Number(n % 58n)] + out;
    n /= 58n;
  }
  for (const b of data) {
    if (b !== 0) break;
    out = '1' + out;
  }
  return out;
}

/** The raw 32 bytes of an `ed25519:<base58>` key, or null if it isn't one. */
export function ed25519KeyBytes(publicKey: string): Buffer | null {
  if (!publicKey.startsWith('ed25519:')) return null;
  try {
    const raw = base58Decode(publicKey.slice('ed25519:'.length));
    return raw.length === 32 ? raw : null;
  } catch {
    return null;
  }
}

const SPKI_ED25519 = Buffer.from('302a300506032b6570032100', 'hex');

/** Checks the signature only; whether the key belongs to the account is checked on-chain. */
export function verifyNep413(signed: SignedMessage, payload: Nep413Payload): boolean {
  const raw = ed25519KeyBytes(signed.publicKey);
  if (!raw) return false;
  const signature = Buffer.from(signed.signature, 'base64');
  if (signature.length !== 64) return false;
  const key = createPublicKey({ key: Buffer.concat([SPKI_ED25519, raw]), format: 'der', type: 'spki' });
  return verify(null, nep413Hash(payload), key, signature);
}

/** NEAR account ids: 2-64 chars, lowercase, `[a-z0-9_-]` segments joined by dots (or 64-hex implicit). */
export const ACCOUNT_ID = /^(?=.{2,64}$)(([a-z\d]+[-_])*[a-z\d]+\.)*([a-z\d]+[-_])*[a-z\d]+$/;
