import { paths } from '../infra/paths';
import type { DocData } from '../infra/store';
import type { Env } from './services';

export const EXPORT_LIMITS = { notifications: 500, friends: 500, devices: 20 } as const;

/**
 * `account-export` (GDPR art. 20, 09 §6): everything stored about a player in
 * one JSON document. Device push tokens are reduced to their platform and
 * date (a token is a credential, not personal data worth handing out), and
 * long lists are capped and flagged as truncated.
 */
export async function exportAccount(env: Env, uid: string): Promise<{ json: string; exportedAt: number; truncated: boolean }> {
  const read = async (path: string) => (await env.store.read(path)).data ?? null;
  const list = (collection: string, limit: number, order?: readonly [string, 'asc' | 'desc']) =>
    env.store.query({ collection, where: [], limit, ...(order ? { orderBy: order } : {}) });

  const [player, settings, island] = await Promise.all([read(paths.player(uid)), read(paths.settings(uid)), read(paths.island(uid))]);
  const [notifications, friends, devices] = await Promise.all([
    list(paths.notifications(uid), EXPORT_LIMITS.notifications, ['createdAt', 'desc']),
    list(paths.friends(uid), EXPORT_LIMITS.friends),
    list(paths.pushTokens(uid), EXPORT_LIMITS.devices),
  ]);
  const idOf = (path: string) => path.split('/').pop()!;
  const keyed = (rows: readonly { path: string; data: DocData }[]) => Object.fromEntries(rows.map((r) => [idOf(r.path), r.data]));
  const truncated = notifications.length === EXPORT_LIMITS.notifications || friends.length === EXPORT_LIMITS.friends;
  const exportedAt = env.clock.nowMs();

  const document = {
    format: 'farmverse-account-export',
    version: 2,
    uid,
    exportedAt: new Date(exportedAt).toISOString(),
    player,
    settings,
    island,
    friends: keyed(friends),
    notifications: keyed(notifications),
    devices: devices.map((d) => ({ installationId: idOf(d.path), platform: d.data['platform'] ?? null, updatedAt: d.data['updatedAt'] ?? null })),
  };
  return { json: JSON.stringify(document, null, 2), exportedAt, truncated };
}
