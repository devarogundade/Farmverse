import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import type { Env } from './services';
import { creditInTransaction } from './wallet';

/**
 * Gems for island levels (economy plan §7): 2 a level, 5 at the end of a chapter (10, 25, 50), 3 a level
 * past the last chapter. The island lives on the phone and is mirrored to the server; its level comes from
 * that mirror (state/island summary). Paid once per level, from session-ping.
 */

export const CHAPTER_ENDS = [10, 25, 50] as const;
export const CONTENT_LEVELS = 50;
/** A player who already had levels when this started gets at most this many levels' gems back-paid. */
export const MAX_BACK_PAY_LEVELS = 10;
/** At most this many levels are paid in one go (a jump bigger than that is suspicious, not generous). */
export const MAX_LEVELS_PER_PAY = 20;

/** Gems for reaching a level (GameplayRules.LevelGems in the game). */
export function levelGems(level: number): number {
  if (level <= 1) return 0;
  if ((CHAPTER_ENDS as readonly number[]).includes(level)) return 5;
  return level > CONTENT_LEVELS ? 3 : 2;
}

/** Pay the gems for levels reached since the last payment. Returns what was paid (0 when nothing new). */
export async function payLevelGems(env: Env, uid: string): Promise<{ gems: number; level: number }> {
  const cat = await catalogue(env);
  const island = (await env.store.read(paths.island(uid))).data;
  const level = Math.max(1, Math.floor(Number((island?.['summary'] as { level?: number } | undefined)?.level ?? 1)));
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const wallet = (await uow.read(paths.wallet(uid))).data;
    const raw = wallet?.['levelPaid'];
    const paidTo = typeof raw === 'number' ? raw : Math.max(1, level - MAX_BACK_PAY_LEVELS);
    if (level <= paidTo) return { gems: 0, level: paidTo };
    const to = Math.min(level, paidTo + MAX_LEVELS_PER_PAY);
    let gems = 0;
    for (let l = paidTo + 1; l <= to; l++) gems += levelGems(l);
    const credit = await creditInTransaction(uow, cat, uid, gems, `level_${to}`, nowMs);
    // one write to the wallet: the gems and how far they're paid (merge keeps what was bought today)
    uow.set(paths.wallet(uid), { gems: credit.balance, levelPaid: to, updatedAt: nowMs }, { merge: true });
    uow.set(paths.ledger(uid, `g_level_${to}`), { kind: 'reward', ref: `level_${to}`, delta: gems, balance: credit.balance, at: nowMs });
    uow.commit();
    return { gems, level: to };
  });
}
