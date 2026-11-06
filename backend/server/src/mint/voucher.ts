import { createPrivateKey, createPublicKey, sign, type KeyObject } from 'node:crypto';
import { base58Encode } from '../auth/nep413';

/** Token kinds in the items contract's borsh order (`ItemKind`): append only. */
export const TOKEN_KINDS = ['good', 'boost', 'building', 'land', 'tool', 'pack', 'worker'] as const;
export type TokenKind = (typeof TOKEN_KINDS)[number];

/** The voucher `items.mint_with_voucher` verifies; field order is its borsh layout. */
export interface MintVoucher {
  readonly contract_id: string;
  readonly token_id: string;
  readonly receiver_id: string;
  readonly kind: TokenKind;
  readonly item_id: string;
  readonly quantity: number;
  readonly props: string;
  readonly title: string;
  readonly media: string;
  readonly expires_at_ms: number;
}

/** Must equal the contract's MINT_DOMAIN. */
export const MINT_DOMAIN = Buffer.from('farmverse:items:mint:v1', 'utf8');

function str(s: string): Buffer {
  const bytes = Buffer.from(s, 'utf8');
  const len = Buffer.alloc(4);
  len.writeUInt32LE(bytes.length);
  return Buffer.concat([len, bytes]);
}

function u64(n: number): Buffer {
  if (!Number.isSafeInteger(n) || n < 0) throw new Error(`not a u64: ${n}`);
  const b = Buffer.alloc(8);
  b.writeBigUInt64LE(BigInt(n));
  return b;
}

/** `MINT_DOMAIN ‖ borsh(voucher)` — what the signer signs and the contract checks. */
export function voucherBytes(v: MintVoucher): Buffer {
  const kind = TOKEN_KINDS.indexOf(v.kind);
  if (kind < 0) throw new Error(`unknown kind ${v.kind}`);
  return Buffer.concat([
    MINT_DOMAIN,
    str(v.contract_id),
    str(v.token_id),
    str(v.receiver_id),
    Buffer.from([kind]),
    str(v.item_id),
    u64(v.quantity),
    str(v.props),
    str(v.title),
    str(v.media),
    u64(v.expires_at_ms),
  ]);
}

const PKCS8_ED25519 = Buffer.from('302e020100300506032b657004220420', 'hex');

/** The game server's voucher key. Its public half is registered on the contract with `add_signer`. */
export class VoucherSigner {
  private readonly privateKey: KeyObject;
  /** `ed25519:<base58>`, as the contract's `signer` argument. */
  readonly publicKey: string;

  constructor(seed: Buffer) {
    if (seed.length !== 32) throw new Error('voucher signer seed must be 32 bytes');
    this.privateKey = createPrivateKey({ key: Buffer.concat([PKCS8_ED25519, seed]), format: 'der', type: 'pkcs8' });
    const raw = createPublicKey(this.privateKey).export({ format: 'der', type: 'spki' }).subarray(-32);
    this.publicKey = `ed25519:${base58Encode(raw)}`;
  }

  /** Base64 signature, as the contract's `signature` argument. */
  sign(v: MintVoucher): string {
    return sign(null, voucherBytes(v), this.privateKey).toString('base64');
  }
}
