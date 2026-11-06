import { DEFAULT_CATALOGUE, RewardDoc, type ReferralDoc } from '@farmverse/contracts';
import { AppError, fail, corrupt } from '../core/errors';
import { newId } from '../core/ids';
import { MS_PER_DAY } from '../domain';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import type { Env } from './services';
import { catalogue } from './catalogue';
import { creditInTransaction } from './wallet';

/** The built-in referral rules (the live ones are the catalogue's `referral`). */
export const REFERRAL = { level: DEFAULT_CATALOGUE.referral.level, coins: DEFAULT_CATALOGUE.referral.reward.coins, gems: DEFAULT_CATALOGUE.referral.reward.gems };

/** Only players this new when they accept an invite count as referred (an old account can't be "referred"). */
export const REFERRAL_NEW_PLAYER_MS = 7 * MS_PER_DAY;

type Outcome = 'none' | 'not_referred' | 'already' | 'level';

/** The island level in the server's copy of the player's island (`state/island`), 0 when there is none. */
export function islandLevel(islandDoc: Record<string, unknown> | undefined): number {
  const state = islandDoc?.['state'];
  if (typeof state !== 'string') return 0;
  try {
    const level = (JSON.parse(state) as { economy?: { bestLevel?: unknown } }).economy?.bestLevel;
    return typeof level === 'number' && Number.isInteger(level) ? level : 0;
  } catch {
    return 0;
  }
}

/**
 * `referral-claim`: the invited player says their island reached the referral level. The server
 * checks its own copy of the island, then in one transaction marks the referral rewarded and gives
 * both players their reward (collected with `rewards-claim`), and tells both.
 */
export async function claimReferral(env: Env, uid: string): Promise<{ rewarded: boolean; reason: Outcome }> {
  const cat = await catalogue(env);
  const REFERRAL = { level: cat.referral.level, coins: cat.referral.reward.coins, gems: cat.referral.reward.gems };
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const [referralSnap, islandSnap, meSnap] = await uow.readAll([paths.referral(uid), paths.island(uid), paths.player(uid)]);
    if (!referralSnap?.exists) return { rewarded: false, reason: 'not_referred' as const };
    const referral = referralSnap.data as unknown as ReferralDoc;
    if (referral.status === 'rewarded') return { rewarded: false, reason: 'already' as const };
    if (islandLevel(islandSnap?.data) < REFERRAL.level) return { rewarded: false, reason: 'level' as const };
    if (!meSnap?.exists) fail('PLAYER_NOT_FOUND');
    const [inviterSnap] = await uow.readAll([paths.player(referral.inviterUid)]);
    const myName = String(meSnap.data?.['displayName'] ?? '');
    const theirName = String(inviterSnap?.data?.['displayName'] ?? '');

    uow.set(paths.referral(uid), { ...referral, status: 'rewarded', rewardedAt: nowMs });
    const give = (to: string, reason: RewardDoc['reason'], about: string) => {
      const reward: RewardDoc = { reason, coins: REFERRAL.coins, gems: REFERRAL.gems, about, createdAt: nowMs, claimedAt: 0 };
      const rewardId = newId('r');
      uow.set(paths.reward(to, rewardId), reward);
      uow.set(paths.notification(to, newId('n')), {
        kind: 'referral_reward',
        titleKey: 'notification.referral_reward.title',
        bodyKey: 'notification.referral_reward.body',
        params: { name: about, level: String(REFERRAL.level), coins: String(REFERRAL.coins), gems: String(REFERRAL.gems) },
        route: 'friends',
        routeArgs: { rewardId },
        actionKey: 'claim',
        createdAt: nowMs,
        readAt: 0,
        expireAt: new Date(nowMs + 30 * MS_PER_DAY),
      });
    };
    // the inviter's player may have been deleted: the newcomer is still rewarded
    if (inviterSnap?.exists) give(referral.inviterUid, 'referral_inviter', myName);
    give(uid, 'referral_invitee', theirName);
    uow.commit();
    return { rewarded: true, reason: 'none' as const };
  });
}

/**
 * `rewards-claim`: collect a reward once. Its gems go into the wallet here (server-side); the coins
 * are for the game to add to the island. A second claim gives nothing (`fresh: false`).
 */
export async function claimReward(env: Env, uid: string, id: string): Promise<{ coins: number; gems: number; fresh: boolean }> {
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const snap = await uow.read(paths.reward(uid, id));
    if (!snap.exists) throw new AppError('NOT_FOUND');
    const parsed = RewardDoc.safeParse(snap.data);
    if (!parsed.success) throw corrupt('reward', parsed.error);
    const reward = parsed.data;
    if (reward.claimedAt > 0) return { coins: 0, gems: 0, fresh: false };
    const credit = await creditInTransaction(uow, cat, uid, reward.gems, id, nowMs);
    uow.set(paths.reward(uid, id), { ...reward, claimedAt: nowMs });
    credit.apply();
    uow.commit();
    return { coins: reward.coins, gems: reward.gems, fresh: true };
  });
}
