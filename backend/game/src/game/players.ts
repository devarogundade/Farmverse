import { randomInt } from 'node:crypto';
import { PlayerDoc } from '@farmverse/contracts';
import { fail, corrupt } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { createPlayer, usernameKey, type NewPlayer } from './onboarding';
import type { Env } from './services';
import { catalogue } from './catalogue';

/** `player-create`: the signed-in player, under a name nobody has. */
export async function create(env: Env, uid: string, input: NewPlayer): Promise<{ uid: string }> {
  const cat = await catalogue(env);
  await env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    await createPlayer(uow, uid, env.clock.nowMs(), input, cat);
    uow.commit();
  });
  return { uid };
}

/** Two-word farm names (S07 dice). Original word lists, title case, at most 16 characters. */
const NAME_FIRST = ['Sunny', 'Clover', 'Maple', 'Willow', 'Golden', 'Honey', 'Pine', 'River', 'Meadow', 'Apple', 'Copper', 'Misty', 'Amber', 'Cedar', 'Bramble', 'Thistle', 'Hazel', 'Rosy', 'Oak', 'Fern'];
const NAME_SECOND = ['Acres', 'Ridge', 'Hollow', 'Patch', 'Furrow', 'Brook', 'Field', 'Meadow', 'Grove', 'Farm', 'Glen', 'Vale', 'Orchard', 'Pasture', 'Barn', 'Hill'];

/** Name ideas nobody has taken; tries a few rounds and returns what it found (maybe fewer than asked). */
export async function suggestNames(env: Env, count: number): Promise<{ names: string[] }> {
  const names: string[] = [];
  const tried = new Set<string>();
  for (let round = 0; round < 3 && names.length < count; round++) {
    const batch: string[] = [];
    while (batch.length < count * 2 && tried.size < NAME_FIRST.length * NAME_SECOND.length) {
      const name = `${pick(NAME_FIRST)} ${pick(NAME_SECOND)}`;
      if (name.length > 16 || tried.has(name) || name.split(' ')[0] === name.split(' ')[1]) continue;
      tried.add(name);
      batch.push(name);
    }
    const snaps = await Promise.all(batch.map((name) => env.store.read(paths.username(usernameKey(name)))));
    batch.forEach((name, i) => {
      if (!snaps[i]?.exists && names.length < count) names.push(name);
    });
  }
  return { names };
}

function pick<T>(list: readonly T[]): T {
  return list[randomInt(list.length)]!;
}

export interface ProfileChange {
  readonly displayName?: string | undefined;
  readonly avatarId?: string | undefined;
  readonly frameId?: string | undefined;
  readonly allowVisitors?: boolean | undefined;
  readonly pushEnabled?: boolean | undefined;
  readonly timezone?: string | undefined;
  readonly locale?: string | undefined;
}

/** Rename (keeping `usernames/` unique in the same transaction), cosmetics and settings. */
export async function updateProfile(env: Env, uid: string, change: ProfileChange): Promise<Record<string, never>> {
  await env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const newName = change.displayName?.trim();
    const newKey = newName === undefined ? undefined : usernameKey(newName);
    const [snap, claimed] = await uow.readAll([paths.player(uid), ...(newKey === undefined ? [] : [paths.username(newKey)])]);
    if (!snap?.exists) fail('PLAYER_NOT_FOUND');
    const parsed = PlayerDoc.safeParse(snap.data);
    if (!parsed.success) throw corrupt('player', parsed.error);
    const player = parsed.data;
    const oldKey = usernameKey(player.displayName);

    if (newName !== undefined && newKey !== undefined && newKey !== oldKey) {
      if (claimed?.exists && claimed.data?.['uid'] !== uid) fail('NAME_TAKEN');
      uow.delete(paths.username(oldKey));
      uow.set(paths.username(newKey), { uid });
    }
    if (newName !== undefined) player.displayName = newName;
    if (change.avatarId !== undefined) player.avatarId = change.avatarId;
    if (change.frameId !== undefined) player.frameId = change.frameId;
    player.rev += 1;
    uow.set(paths.player(uid), player);

    const settings: Record<string, unknown> = {};
    if (change.allowVisitors !== undefined) settings['allowVisitors'] = change.allowVisitors;
    if (change.pushEnabled !== undefined) settings['pushEnabled'] = change.pushEnabled;
    if (change.timezone !== undefined) settings['timezone'] = change.timezone;
    if (change.locale !== undefined) settings['locale'] = change.locale;
    if (Object.keys(settings).length > 0) uow.set(paths.settings(uid), settings, { merge: true });
    uow.commit();
  });
  return {};
}

export async function registerPushToken(env: Env, uid: string, installationId: string, token: string, platform: string): Promise<Record<string, never>> {
  await env.store.writeBatch([{ path: paths.pushToken(uid, installationId), data: { token, platform, updatedAt: env.clock.nowMs() } }]);
  return {};
}
