import { randomBytes } from 'node:crypto';

const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';

/** n notification · r reward */
export type IdPrefix = 'n' | 'r';

/**
 * A new server id: `<prefix>_` + 20 random base62 characters (≈119 bits), the
 * format the contract's id schemas accept. Rejection sampling keeps it unbiased.
 */
export function newId(prefix: IdPrefix, length = 20): string {
  let out = '';
  while (out.length < length) {
    for (const byte of randomBytes(length * 2)) {
      if (byte < 248) out += ALPHABET[byte % 62];
      if (out.length === length) break;
    }
  }
  return `${prefix}_${out}`;
}
