import { MS_PER_DAY } from '../domain';
import { newId } from '../core/ids';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import type { Env } from './services';

/**
 * The inbox (module 9) is the player's notifications (`players/{uid}/notifications`). A message may carry
 * a reward (`routeArgs.rewardId` → `players/{uid}/rewards/{id}`, collected with rewards-claim). The team
 * sends mail with tools/sendMail.ts; the game reads, collects and deletes.
 */

export interface Mail {
  readonly title: string;
  readonly body: string;
  readonly coins: number;
  readonly gems: number;
  /** A screen to open from the message ('' for none). */
  readonly route: string;
  readonly expireDays: number;
}

/** Write one mail (and its reward) to a player, inside the caller's unit of work. */
export function writeMail(uow: UnitOfWork, uid: string, mail: Mail, nowMs: number): void {
  const routeArgs: Record<string, string> = {};
  if (mail.coins > 0 || mail.gems > 0) {
    const rewardId = newId('r');
    uow.set(paths.reward(uid, rewardId), { reason: 'mail', coins: mail.coins, gems: mail.gems, about: mail.title.slice(0, 16), createdAt: nowMs, claimedAt: 0 });
    routeArgs['rewardId'] = rewardId;
  }
  uow.set(paths.notification(uid, newId('n')), {
    kind: 'mail',
    titleKey: 'notification.mail.title',
    bodyKey: 'notification.mail.body',
    params: { title: mail.title.slice(0, 64), body: mail.body.slice(0, 64), coins: String(mail.coins), gems: String(mail.gems) },
    route: mail.route,
    routeArgs,
    ...(routeArgs['rewardId'] ? { actionKey: 'claim' } : {}),
    createdAt: nowMs,
    readAt: 0,
    expireAt: new Date(nowMs + mail.expireDays * MS_PER_DAY),
  });
}

/** `inbox-delete`: remove messages, except ones whose reward hasn't been collected yet. */
export async function deleteMessages(env: Env, uid: string, ids: readonly string[]): Promise<{ deleted: number }> {
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const unique = [...new Set(ids)];
    const notes = await uow.readAll(unique.map((id) => paths.notification(uid, id)));
    const rewardIds = notes.map((n) => String((n?.data?.['routeArgs'] as Record<string, unknown> | undefined)?.['rewardId'] ?? ''));
    const rewards = await uow.readAll(rewardIds.map((r) => (r ? paths.reward(uid, r) : paths.notification(uid, 'n_none000000'))));
    let deleted = 0;
    unique.forEach((id, i) => {
      if (!notes[i]?.exists) return;
      const reward = rewardIds[i] ? rewards[i] : undefined;
      if (reward?.exists && Number(reward.data?.['claimedAt'] ?? 0) === 0) return;   // collect it first
      uow.delete(paths.notification(uid, id));
      deleted++;
    });
    uow.commit();
    return { deleted };
  });
}
