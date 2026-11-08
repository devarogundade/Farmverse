import type { Logger } from '../../../game/src/core/logger';
import { holdCouponPayout, noteCouponPayoutTx, releaseCouponPayout, settleCouponPayout } from '../../../game/src/game/coupons';
import type { Env } from '../../../game/src/game/services';

export interface PayoutRequest {
  readonly code: string;
  readonly receiver: string;
  /** Whole tokens. */
  readonly amount: number;
  readonly task: string;
}

export type PayoutResult =
  | { sent: true; tx: string; confirmed: boolean }
  /** Nothing was paid: before sending, or the transaction reverted (`tx`). */
  | { sent: false; why: 'paused' | 'no_funds' | 'error' | 'reverted'; tx?: string };

/** Pays one coupon's unit to a wallet. `onSent` runs once the transaction is out, before its receipt. */
export interface Payer {
  readonly unit: string;
  pay(req: PayoutRequest, onSent: (tx: string) => Promise<void>): Promise<PayoutResult>;
}

export interface RedeemDeps {
  readonly env: Env;
  /** The units that can be paid now (B3TR); a code in another unit is "coming soon". */
  readonly payers: readonly Payer[];
  readonly log: Logger;
}

/** What the redeem page shows. `status` is the HTTP status. */
export type RedeemOutcome =
  | { status: 200; ok: true; amount: number; unit: string; tx: string; confirmed: boolean }
  | { status: number; ok: false; code: RedeemRefusal; unit?: string; tx?: string };

export type RedeemRefusal =
  | 'bad_code'
  | 'bad_wallet'
  | 'unknown'
  | 'used'
  | 'expired'
  | 'busy'
  | 'coming_soon'
  | 'closed'
  | 'paused'
  | 'no_funds'
  | 'failed';

/** A coupon code as the game writes it: PREFIX-XXXX-XXXX. */
export const CODE = /^[A-Z0-9]{2,8}-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/;
/** A VeChain (EVM-style) address. */
export const WALLET = /^0x[0-9a-fA-F]{40}$/;

const REFUSED: Record<'unknown' | 'used' | 'expired' | 'busy', number> = { unknown: 404, used: 409, expired: 410, busy: 409 };

export async function redeemForPayout(d: RedeemDeps, rawCode: string, rawWallet: string): Promise<RedeemOutcome> {
  const code = rawCode.replace(/\s+/g, '').toUpperCase();
  const wallet = rawWallet.trim();
  if (!CODE.test(code)) return { status: 400, ok: false, code: 'bad_code' };
  if (!WALLET.test(wallet)) return { status: 400, ok: false, code: 'bad_wallet' };

  // Hold it for every unit the game knows, then see whether this server can pay it
  const units = d.payers.map((p) => p.unit);
  const hold = await holdCouponPayout(d.env, code, wallet.toLowerCase(), units);
  if (!hold.ok) {
    if (hold.why === 'unsupported') {
      // a B3TR code while the payout isn't set up here is "closed"; any other unit is still to come
      return hold.unit === 'B3TR'
        ? { status: 503, ok: false, code: 'closed', unit: hold.unit }
        : { status: 409, ok: false, code: 'coming_soon', ...(hold.unit ? { unit: hold.unit } : {}) };
    }
    return { status: REFUSED[hold.why], ok: false, code: hold.why, ...(hold.tx ? { tx: hold.tx } : {}) };
  }

  const payer = d.payers.find((p) => p.unit === hold.unit)!;
  let result: PayoutResult;
  try {
    result = await payer.pay({ code, receiver: wallet, amount: hold.amount, task: hold.task }, (tx) => noteCouponPayoutTx(d.env, code, tx));
  } catch (error) {
    // the payer itself broke: if a transaction went out, its hash is on the code and the hold stays
    d.log.error('coupon payout threw', { code, error: String(error) });
    return { status: 502, ok: false, code: 'failed' };
  }

  if (!result.sent) {
    await releaseCouponPayout(d.env, code, result.tx ? `${result.why} ${result.tx}` : result.why);
    const status = result.why === 'paused' || result.why === 'no_funds' ? 503 : 502;
    return { status, ok: false, code: result.why === 'reverted' || result.why === 'error' ? 'failed' : result.why };
  }
  if (result.confirmed) await settleCouponPayout(d.env, code, result.tx);
  // unconfirmed: the hold keeps the transaction; the code stays held (never paid twice)
  d.log.info('coupon paid', { code, unit: hold.unit, amount: hold.amount, tx: result.tx, confirmed: result.confirmed });
  return { status: 200, ok: true, amount: hold.amount, unit: hold.unit, tx: result.tx, confirmed: result.confirmed };
}
