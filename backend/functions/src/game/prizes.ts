import type { Bundle, CatalogueDoc } from '@farmverse/contracts';
import { paths } from '../infra/paths';
import type { UnitOfWork } from '../infra/unitOfWork';
import { addBoosts, readInventory } from './shop';
import { creditInTransaction } from './wallet';

/** What a prize paid, as callables answer it: gems and boosts are already on the server; coins go to the island. */
export interface PrizePaid {
  coins: number;
  gems: number;
  boosts: Record<string, number>;
}

/**
 * Pay a bundle inside a transaction: gems to the wallet, boosts to the inventory (both read here, so
 * call this before the caller's first write), coins returned for the game to add to the island.
 * `apply()` performs the writes.
 */
export async function preparePrize(
  uow: UnitOfWork, cat: CatalogueDoc, uid: string, prize: Bundle, ref: string, nowMs: number,
): Promise<{ paid: PrizePaid; apply: () => void }> {
  const credit = await creditInTransaction(uow, cat, uid, prize.gems, ref, nowMs);
  const hasBoosts = Object.keys(prize.boosts).length > 0;
  const inventory = hasBoosts ? readInventory((await uow.read(paths.inventory(uid))).data, nowMs) : undefined;
  return {
    paid: { coins: prize.coins, gems: prize.gems, boosts: { ...prize.boosts } },
    apply: () => {
      credit.apply();
      if (inventory) uow.set(paths.inventory(uid), addBoosts(inventory, prize.boosts, nowMs));
    },
  };
}
