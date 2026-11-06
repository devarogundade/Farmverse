/**
 * The Farmhands a player can use (plan 15 §7.3, §12.1), from the indexer's tables:
 *  - workers they own (not burned),
 *  - workers they lent to the market that nobody is renting right now (they keep working for the lender),
 *  - workers they are renting right now.
 * Levels come from `workers/{tokenId}` (XP the server counted). Players without a wallet get no crew.
 */
import { FARMHAND_MAX_LEVEL, type Farmhand, type WorkerStatsDoc, workerPath } from '../../../game/src/game/crew';
import type { Store } from '../../../game/src/infra/store';
import type { PrismaClient } from '../generated/prisma/client';

const trait = (v: unknown) => (typeof v === 'number' && Number.isFinite(v) ? Math.max(1, Math.min(5, Math.round(v))) : 1);

export function crewOf(prisma: PrismaClient, store: Store, itemsContract: string) {
  return async (uid: string): Promise<Farmhand[] | undefined> => {
    const acc = await prisma.account.findUnique({ where: { uid }, select: { accountId: true } });
    if (!acc) return undefined;
    const rows = await prisma.$queryRaw<{ token_id: string; title: string; props: Record<string, unknown> | null; rented: boolean }[]>`
      SELECT t.token_id, t.title, t.props, false AS rented FROM chain.tokens t
      WHERE t.contract_id = ${itemsContract} AND t.kind = 'worker' AND t.burned_at IS NULL AND t.owner_id = ${acc.accountId}
      UNION ALL
      SELECT t.token_id, t.title, t.props, false AS rented FROM chain.rentals r
      JOIN chain.tokens t ON t.contract_id = r.contract_id AND t.token_id = r.token_id
      WHERE r.contract_id = ${itemsContract} AND r.lender_id = ${acc.accountId} AND (r.rented_until IS NULL OR r.rented_until <= now())
      UNION ALL
      SELECT t.token_id, t.title, t.props, true AS rented FROM chain.rentals r
      JOIN chain.tokens t ON t.contract_id = r.contract_id AND t.token_id = r.token_id
      WHERE r.contract_id = ${itemsContract} AND r.renter_id = ${acc.accountId} AND r.rented_until > now()
      ORDER BY 1
      LIMIT 50`;
    const crew: Farmhand[] = [];
    for (const r of rows) {
      const p = r.props ?? {};
      const traits = (p['traits'] ?? {}) as Record<string, unknown>;
      const stats = (await store.read(workerPath(r.token_id))).data as WorkerStatsDoc | undefined;
      crew.push({
        token: r.token_id,
        name: typeof p['name'] === 'string' ? (p['name'] as string) : r.title,
        specialty: typeof p['specialty'] === 'string' ? (p['specialty'] as string) : '',
        speed: trait(traits['speed']),
        carry: trait(traits['carry']),
        stamina: trait(traits['stamina']),
        level: Math.max(1, Math.min(FARMHAND_MAX_LEVEL, stats?.level ?? 1)),
        rented: r.rented,
      });
    }
    return crew;
  };
}
