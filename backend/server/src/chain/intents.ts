/**
 * Buy FARM with any token (plan 15 §13.3), through NEAR Intents' 1Click API. FARM has no DEX liquidity at launch,
 * so the studio sells FARM at a fixed rate: the player swaps any supported token into NEAR paid to the treasury, and
 * once 1Click reports the swap done, a `sales` account sends the FARM (once per deposit address).
 *
 * Intents run on mainnet only (the verifier has no testnet deployment): on testnet, and until INTENTS_LIVE is on,
 * the API answers dry quotes (a price, no deposit address) so players can see what FARM would cost.
 */
import { AppError, fail } from '../../../game/src/core/errors';
import type { Logger } from '../../../game/src/core/logger';
import type { Store } from '../../../game/src/infra/store';

const ONECLICK = 'https://1click.chaindefuser.com/v0';
/** NEAR inside Intents is wrapped NEAR. */
export const WNEAR_ASSET = 'nep141:wrap.near';
const E24 = 10n ** 24n;
const E18 = 10n ** 18n;

export interface IntentsConfig {
  /** Live orders (mainnet): deposit addresses and FARM delivery. Off: dry quotes only. */
  readonly live: boolean;
  readonly apiKey?: string;
  /** Who receives the NEAR (the treasury, on mainnet). */
  readonly treasury: string;
  /** Whole FARM per whole NEAR. */
  readonly farmPerNear: number;
  readonly fetchImpl?: typeof fetch;
}

export interface FarmQuote {
  readonly live: boolean;
  /** NEAR (yocto) the swap delivers, and FARM (yocto-FARM) the player gets for it. */
  readonly nearOut: string;
  readonly farmOut: string;
  readonly depositAddress?: string;
  readonly deadline?: string;
}

/**
 * A refund address that's valid on the token's own chain, for previews only (a dry quote moves nothing, but 1Click
 * still checks the format). Live orders use the player's own refund address.
 */
export function previewRefundTo(originAsset: string): string | null {
  if (/-0x[0-9a-f]{40}\.omft\.near$/i.test(originAsset) || /^nep141:(eth|arb|base|gnosis|bsc|pol|avax|op)\.omft\.near$/.test(originAsset)) return '0x0000000000000000000000000000000000000001';
  if (originAsset.startsWith('nep141:sol')) return '11111111111111111111111111111111';
  if (/^nep141:[a-z0-9._-]+$/.test(originAsset) && !originAsset.includes('.omft.near')) return 'near';
  return null;
}

/** FARM (yocto-FARM) for an amount of NEAR (yocto) at the fixed rate. */
export function farmFor(nearYocto: bigint, farmPerNear: number): bigint {
  return (nearYocto * BigInt(Math.round(farmPerNear * 1000)) * E18) / (E24 * 1000n);
}

async function call<T>(cfg: IntentsConfig, path: string, init?: RequestInit): Promise<T> {
  const f = cfg.fetchImpl ?? fetch;
  const res = await f(`${ONECLICK}${path}`, {
    ...init,
    headers: { 'content-type': 'application/json', ...(cfg.apiKey ? { 'x-api-key': cfg.apiKey } : {}), ...(init?.headers ?? {}) },
    signal: AbortSignal.timeout(15_000),
  });
  if (!res.ok) throw new Error(`1Click answered ${res.status}: ${(await res.text()).slice(0, 200)}`);
  return (await res.json()) as T;
}

/** The tokens 1Click can swap from (cached by the caller). */
export function intentsTokens(cfg: IntentsConfig) {
  return call<{ assetId: string; symbol: string; blockchain: string; decimals: number; price?: number }[]>(cfg, '/tokens');
}

/**
 * A quote: `amount` of `originAsset` (base units) → NEAR to the treasury → FARM. Live mode returns a deposit address
 * and records the order for `account`.
 */
export async function farmQuote(
  cfg: IntentsConfig, store: Store, account: string | null, originAsset: string, amount: string, refundTo: string, nowMs: number,
): Promise<FarmQuote> {
  if (!/^\d{1,40}$/.test(amount) || !originAsset) fail('INVALID_ARGUMENT');
  const live = cfg.live && Boolean(account);
  // previews go to an existing stand-in account (1Click wants a real one); live orders pay the treasury
  const refund = live ? refundTo : (refundTo || previewRefundTo(originAsset));
  if (!refund) fail('INVALID_ARGUMENT');
  const q = await call<{ quote: { amountOut: string; depositAddress?: string; deadline?: string } }>(cfg, '/quote', {
    method: 'POST',
    body: JSON.stringify({
      dry: !live,
      swapType: 'EXACT_INPUT',
      slippageTolerance: 100,
      originAsset,
      depositType: 'ORIGIN_CHAIN',
      destinationAsset: WNEAR_ASSET,
      amount,
      refundTo: refund,
      refundType: 'ORIGIN_CHAIN',
      recipient: live ? cfg.treasury : 'near',
      recipientType: 'DESTINATION_CHAIN',
      deadline: new Date(nowMs + 30 * 60_000).toISOString(),
      referral: 'farmverse',
    }),
  });
  const nearOut = BigInt(q.quote.amountOut);
  const farmOut = farmFor(nearOut, cfg.farmPerNear);
  if (live && q.quote.depositAddress) {
    await store.writeBatch([
      {
        path: `intents/${q.quote.depositAddress}`,
        data: { account, originAsset, amount, nearOut: nearOut.toString(), farmOut: farmOut.toString(), status: 'waiting', at: nowMs },
      },
    ]);
  }
  return { live, nearOut: nearOut.toString(), farmOut: farmOut.toString(), ...(q.quote.depositAddress ? { depositAddress: q.quote.depositAddress, deadline: q.quote.deadline } : {}) };
}

export interface FarmSender {
  sendFarm(accountId: string, units: bigint): Promise<void>;
  registered(accountId: string): Promise<boolean>;
  register(accountId: string): Promise<void>;
}

/**
 * `intents-settle` (every minute, live only): asks 1Click about each waiting order; a finished swap gets its FARM
 * once (the order is marked before sending, so a crash can't send twice; a failed send is logged for a person).
 */
export async function settleIntents(cfg: IntentsConfig, store: Store, sender: FarmSender, log: Logger, nowMs: number): Promise<number> {
  if (!cfg.live) return 0;
  const rows = await store.query({ collection: 'intents', where: [['status', '==', 'waiting']], limit: 50 });
  let paid = 0;
  for (const r of rows) {
    const depositAddress = r.path.split('/')[1]!;
    const o = r.data as { account: string; farmOut: string; at: number };
    let status = '';
    try {
      status = (await call<{ status: string }>(cfg, `/status?depositAddress=${encodeURIComponent(depositAddress)}`)).status;
    } catch (e) {
      log.warn('intents status failed', { depositAddress, error: String(e) });
      continue;
    }
    if (status === 'SUCCESS') {
      const claimed = await store.runTransaction(async (tx) => {
        const cur = await tx.get(r.path);
        if ((cur.data as { status?: string } | undefined)?.status !== 'waiting') return false;
        tx.set(r.path, { status: 'paying', updatedAt: nowMs }, { merge: true });
        return true;
      });
      if (!claimed) continue;
      try {
        if (!(await sender.registered(o.account))) await sender.register(o.account);
        await sender.sendFarm(o.account, BigInt(o.farmOut));
        await store.writeBatch([{ path: r.path, data: { status: 'paid', updatedAt: nowMs }, merge: true }]);
        paid++;
        log.info('intents FARM sent', { depositAddress, account: o.account, farm: o.farmOut });
      } catch (e) {
        log.error('intents FARM not sent: needs a person', { depositAddress, account: o.account, error: String(e) });
      }
    } else if (status === 'REFUNDED' || status === 'FAILED' || (status === 'PENDING_DEPOSIT' && nowMs - o.at > 2 * 3_600_000)) {
      await store.writeBatch([{ path: r.path, data: { status: status === 'PENDING_DEPOSIT' ? 'expired' : status.toLowerCase(), updatedAt: nowMs }, merge: true }]);
    }
  }
  return paid;
}

export function assertIntents(cfg: IntentsConfig | undefined): IntentsConfig {
  if (!cfg) throw new AppError('UNSUPPORTED');
  return cfg;
}
