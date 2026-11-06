/**
 * The nightly reconcile (plan 13 §5.3, 15 §10): the game's records against the chain.
 *
 *  - `refundedButMinted`: vouchers the game refunded whose token exists on chain — items in two places.
 *    Must be 0; anything else is a bug to look at at once.
 *  - `mintedNotRecorded`: voucher tokens on chain with no mint record in the game (a voucher not ours?).
 *  - `redeemsUncredited`: redeems older than 10 minutes by players with a game account, not credited
 *    (the backstop should have caught them).
 *
 * The result goes to `ops/reconcile` (the admin dashboard reads it) and to the log, as a warning on drift.
 */
import type { Logger } from '../../../game/src/core/logger';
import type { Store } from '../../../game/src/infra/store';
import type { PrismaClient } from '../generated/prisma/client';

export interface Reconcile {
  at: number;
  vouchersIssued: number;
  vouchersMinted: number;
  vouchersRefunded: number;
  voucherTokensOnChain: number;
  refundedButMinted: number;
  mintedNotRecorded: number;
  redeemsOnChain: number;
  redeemsCredited: number;
  redeemsUncredited: number;
  ok: boolean;
}

const n = (rows: { n: bigint | number | string }[]) => Number(rows[0]?.n ?? 0);

export async function reconcile(prisma: PrismaClient, store: Store, itemsContract: string, nowMs: number, log: Logger): Promise<Reconcile> {
  const byStatus = await prisma.$queryRaw<{ status: string | null; n: bigint }[]>`
    SELECT data->>'status' AS status, count(*) AS n FROM documents WHERE collection LIKE 'players/%/mints' GROUP BY 1`;
  const status = (s: string) => Number(byStatus.find((r) => r.status === s)?.n ?? 0);
  const voucherTokensOnChain = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM chain.tokens WHERE contract_id = ${itemsContract} AND source = 'voucher'`);
  const refundedButMinted = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM documents d
    JOIN chain.tokens t ON t.contract_id = ${itemsContract} AND t.token_id = d.data->>'tokenId'
    WHERE d.collection LIKE 'players/%/mints' AND d.data->>'status' = 'refunded'`);
  const mintedNotRecorded = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM chain.tokens t
    WHERE t.contract_id = ${itemsContract} AND t.source = 'voucher'
      AND NOT EXISTS (SELECT 1 FROM documents d WHERE d.collection LIKE 'players/%/mints' AND d.data->>'tokenId' = t.token_id)`);
  const redeemsOnChain = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM chain.events WHERE contract_id = ${itemsContract} AND event = 'item_redeem'`);
  const redeemsCredited = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM documents WHERE collection = 'redemptions'`);
  const redeemsUncredited = n(await prisma.$queryRaw<{ n: bigint }[]>`
    SELECT count(*) AS n FROM chain.events e
    JOIN accounts a ON a.account_id = e.data->>'owner_id'
    WHERE e.contract_id = ${itemsContract} AND e.event = 'item_redeem' AND e.block_time < now() - interval '10 minutes'
      AND NOT EXISTS (SELECT 1 FROM documents d WHERE d.path = 'redemptions/' || (e.data->>'token_id'))`);

  const r: Reconcile = {
    at: nowMs,
    vouchersIssued: status('issued'),
    vouchersMinted: status('minted'),
    vouchersRefunded: status('refunded'),
    voucherTokensOnChain,
    refundedButMinted,
    mintedNotRecorded,
    redeemsOnChain,
    redeemsCredited,
    redeemsUncredited,
    ok: refundedButMinted === 0 && mintedNotRecorded === 0 && redeemsUncredited === 0,
  };
  await store.writeBatch([{ path: 'ops/reconcile', data: r as unknown as Record<string, unknown> }]);
  if (r.ok) log.info('chain reconcile', { ...r });
  else log.warn('chain reconcile found drift', { ...r });
  return r;
}
