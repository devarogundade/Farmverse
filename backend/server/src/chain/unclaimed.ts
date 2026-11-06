/**
 * Redeems the indexer saw (`item_redeem` in chain.events) that have no `redemptions/{tokenId}` record yet,
 * for owners who have a game account. The backstop job credits them (mint/mint.ts `backstopRedeems`).
 * Events younger than 90 s are left to the player's own `chain-redeem` call.
 */
import type { PrismaClient } from '../generated/prisma/client';
import type { ChainItem } from '../mint/nearChain';
import type { UnclaimedRedeem } from '../mint/mint';

export function unclaimedRedeems(prisma: PrismaClient, itemsContract: string, limit = 50) {
  return async (): Promise<UnclaimedRedeem[]> => {
    const rows = await prisma.$queryRaw<{ data: { token_id?: string; owner_id?: string; item?: ChainItem }; tx_hash: string | null; uid: string }[]>`
      SELECT e.data, e.tx_hash, a.uid
      FROM chain.events e
      JOIN accounts a ON a.account_id = e.data->>'owner_id'
      WHERE e.contract_id = ${itemsContract}
        AND e.event = 'item_redeem'
        AND e.block_time < now() - interval '90 seconds'
        AND NOT EXISTS (SELECT 1 FROM documents d WHERE d.path = 'redemptions/' || (e.data->>'token_id'))
      ORDER BY e.block_height
      LIMIT ${limit}`;
    return rows.flatMap((r) =>
      r.data.token_id && r.data.owner_id && r.data.item
        ? [{ tokenId: r.data.token_id, ownerId: r.data.owner_id, uid: r.uid, item: r.data.item, txHash: r.tx_hash }]
        : [],
    );
  };
}
