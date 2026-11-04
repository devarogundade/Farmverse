import { randomInt } from 'node:crypto';
import type { PlayerDoc, ReferralDoc } from '@farmverse/contracts';
import { REFERRAL_NEW_PLAYER_MS } from './referrals';
import { AppError, fail } from '../core/errors';
import { MS_PER_DAY } from '../domain';
import { newId } from '../core/ids';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import type { Env } from './services';

/** Invite codes: 8 characters without look-alikes (no 0/O, 1/I). */
const ALPHABET = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
export const INVITE_USES = 20;
export const INVITE_DAYS = 30;
/** The share link; the app opens it as a deep link on first launch. */
export const inviteLink = (code: string) => `https://farmverse.world/invite/${code}`;

/** `invites/{code}` — server only. */
interface InviteDoc {
  inviterUid: string;
  createdAt: number;
  expiresAt: number;
  usesLeft: number;
}

function newCode(): string {
  return Array.from({ length: 8 }, () => ALPHABET[randomInt(ALPHABET.length)]).join('');
}

/** `friends-createInvite` (R44): a fresh code; a few retries cover the rare collision. */
export async function createInvite(env: Env, uid: string) {
  const nowMs = env.clock.nowMs();
  for (let attempt = 0; attempt < 5; attempt++) {
    const code = newCode();
    const created = await env.store.runTransaction(async (tx) => {
      const uow = new UnitOfWork(tx);
      const [player, existing] = await uow.readAll([paths.player(uid), paths.invite(code)]);
      if (!player?.exists) fail('PLAYER_NOT_FOUND');
      if (existing?.exists) return false;
      const invite: InviteDoc = { inviterUid: uid, createdAt: nowMs, expiresAt: nowMs + INVITE_DAYS * MS_PER_DAY, usesLeft: INVITE_USES };
      uow.set(paths.invite(code), { ...invite });
      uow.commit();
      return true;
    });
    if (created) return { code, link: inviteLink(code), usesLeft: INVITE_USES, expiresAt: nowMs + INVITE_DAYS * MS_PER_DAY };
  }
  throw new Error('could not find a free invite code');
}

const faceOf = (player: PlayerDoc) => ({ displayName: player.displayName, avatarId: player.avatarId, shortId: player.shortId });

/**
 * `friends-accept`: both players become friends (each gets the other's row) and
 * the inviter hears about it. One transaction, so a code's last use is never
 * spent twice.
 */
export async function acceptInvite(env: Env, uid: string, code: string) {
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const inviteSnap = await uow.read(paths.invite(code));
    const invite = inviteSnap.data as InviteDoc | undefined;
    if (!inviteSnap.exists || !invite || invite.expiresAt <= nowMs || invite.usesLeft <= 0 || invite.inviterUid === uid) {
      throw new AppError('INVITE_INVALID');
    }
    const [me, inviter, already, referred] = await uow.readAll([
      paths.player(uid), paths.player(invite.inviterUid), paths.friend(uid, invite.inviterUid), paths.referral(uid),
    ]);
    if (!me?.exists) fail('PLAYER_NOT_FOUND');
    if (!inviter?.exists) throw new AppError('INVITE_INVALID');
    if (already?.exists) fail('ALREADY_FRIENDS');
    const mine = me.data as unknown as PlayerDoc;
    const theirs = inviter.data as unknown as PlayerDoc;

    uow.set(paths.friend(uid, invite.inviterUid), { ...faceOf(theirs), since: nowMs });
    uow.set(paths.friend(invite.inviterUid, uid), { ...faceOf(mine), since: nowMs });
    uow.set(paths.invite(code), { ...invite, usesLeft: invite.usesLeft - 1 });
    // a new player brought in by this invite: both are rewarded when the newcomer's island reaches the referral level
    if (!referred?.exists && nowMs - mine.createdAt <= REFERRAL_NEW_PLAYER_MS) {
      const referral: ReferralDoc = { inviterUid: invite.inviterUid, createdAt: nowMs, status: 'pending', rewardedAt: 0 };
      uow.set(paths.referral(uid), referral);
    }
    uow.set(paths.notification(invite.inviterUid, newId('n')), {
      kind: 'friend_joined',
      titleKey: 'notification.friend_joined.title',
      bodyKey: 'notification.friend_joined.body',
      params: { name: mine.displayName },
      route: 'ranks.friends',
      routeArgs: { uid },
      actionKey: 'view',
      createdAt: nowMs,
      readAt: 0,
      expireAt: new Date(nowMs + 30 * MS_PER_DAY),
    });
    uow.commit();
    return { friendUid: invite.inviterUid, ...faceOf(theirs) };
  });
}
