import { weekOf, type LeaderboardEntryDoc } from '@farmverse/contracts';
import { MS_PER_DAY } from '../domain';
import { newId } from '../core/ids';
import { paths } from '../infra/paths';
import type { IslandSummary } from '../infra/islandEngine';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import type { Env } from './services';

/**
 * The weekly leaderboard (module 7). Whenever the server runs an island (a sync, a mirror, a wake) it
 * writes the player's row on this week's board with the net worth the engine computed; players read
 * the board directly (top pages, their country, their friends). After the week ends, `payWeek` gives
 * the catalogue's prizes as rewards (collected with rewards-claim) and tells the winners.
 */

/** Write the player's row on this week's board (best effort: a failed row only waits for the next run). */
export async function postToBoard(env: Env, uid: string, summary: IslandSummary | undefined): Promise<void> {
  if (!summary) return;
  try {
    const [player, settings] = await Promise.all([env.store.read(paths.player(uid)), env.store.read(paths.settings(uid))]);
    if (!player.exists) return;
    const nowMs = env.clock.nowMs();
    const entry: LeaderboardEntryDoc = {
      name: String(player.data?.['displayName'] ?? '').slice(0, 16),
      avatarId: String(player.data?.['avatarId'] ?? 'avatar_default').slice(0, 48),
      locale: String(settings.data?.['locale'] ?? 'en').slice(0, 16),
      worth: Math.max(0, Math.floor(summary.netWorth)),
      level: Math.max(1, Math.floor(summary.level)),
      updatedAt: nowMs,
    };
    await env.store.writeBatch([{ path: paths.boardEntry(weekOf(nowMs).id, uid), data: entry as unknown as Record<string, unknown> }]);
  } catch (e) {
    env.log.warn('leaderboard row not written', { uid, error: String(e) });
  }
}

/** The prize for a rank (1-based), or undefined. */
export function prizeFor(prizes: readonly { rankTo: number; coins: number; gems: number }[], rank: number) {
  return prizes.find((p) => rank <= p.rankTo);
}

/**
 * Pay last week's prizes, once (scheduled every Monday). Ranks are by net worth on the week's board;
 * each winner gets a reward (coins + gems, collected in the game) and a notification.
 */
export async function payWeek(env: Env): Promise<{ weekId: string; paid: number }> {
  const cat = await catalogue(env);
  const nowMs = env.clock.nowMs();
  const last = weekOf(nowMs - 7 * MS_PER_DAY);
  const deepest = cat.leaderboardPrizes.reduce((m, p) => Math.max(m, p.rankTo), 0);
  if (deepest === 0) return { weekId: last.id, paid: 0 };
  const marker = await env.store.read(paths.boardPaid(last.id));
  if (marker.exists && marker.data?.['paidAt']) return { weekId: last.id, paid: 0 };
  const rows = await env.store.query({ collection: paths.board(last.id), where: [], orderBy: ['worth', 'desc'], limit: deepest });
  let paid = 0;
  await env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const again = await uow.read(paths.boardPaid(last.id));
    if (again.exists && again.data?.['paidAt']) return;
    rows.forEach((row, i) => {
      const prize = prizeFor(cat.leaderboardPrizes, i + 1);
      if (!prize || (prize.coins === 0 && prize.gems === 0)) return;
      const uid = row.path.slice(row.path.lastIndexOf('/') + 1);
      const rank = String(i + 1);
      const rewardId = newId('r');
      uow.set(paths.reward(uid, rewardId), { reason: 'leaderboard_week', coins: prize.coins, gems: prize.gems, about: `#${rank}`, createdAt: nowMs, claimedAt: 0 });
      uow.set(paths.notification(uid, newId('n')), {
        kind: 'leaderboard_prize',
        titleKey: 'notification.leaderboard_prize.title',
        bodyKey: 'notification.leaderboard_prize.body',
        params: { rank, coins: String(prize.coins), gems: String(prize.gems) },
        route: 'leaderboard',
        routeArgs: { rewardId },
        actionKey: 'claim',
        createdAt: nowMs,
        readAt: 0,
        expireAt: new Date(nowMs + 30 * MS_PER_DAY),
      });
      paid++;
    });
    uow.set(paths.boardPaid(last.id), { paidAt: nowMs, winners: paid });
    uow.commit();
  });
  env.log.info('weekly prizes paid', { weekId: last.id, paid });
  return { weekId: last.id, paid };
}
