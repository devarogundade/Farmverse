import { describe, expect, it } from 'vitest';
import { renderPush } from '@farmverse/contracts';
import { exportAccount } from '../../src/game/export';
import { acceptInvite, createInvite } from '../../src/game/friends';
import { deliverNotification, inQuietHours, localHour, type Messenger, type PushMessage } from '../../src/game/push';
import { paths } from '../../src/infra/paths';
import { FARMER, MAKER, World } from './world';

const rows = (w: World, uid: string) => w.store.paths(`players/${uid}/notifications/`).map((p) => w.store.get(p)!);

class FakeMessenger implements Messenger {
  readonly sent: PushMessage[] = [];
  stale: string[] = [];
  async send(messages: readonly PushMessage[]) {
    this.sent.push(...messages);
    return this.stale;
  }
}

const notification = (overrides: Record<string, unknown> = {}) => ({
  kind: 'friend_joined',
  titleKey: 'notification.friend_joined.title',
  bodyKey: 'notification.friend_joined.body',
  params: { name: 'Bob' },
  route: 'ranks.friends',
  routeArgs: { uid: MAKER },
  createdAt: 1,
  readAt: 0,
  ...overrides,
});

describe('friends', () => {
  it('makes both players friends from an invite code, once', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    const invite = await createInvite(w.env, FARMER);
    expect(invite.code).toMatch(/^[A-Z0-9]{8}$/);
    expect(invite.link).toContain(invite.code);
    await expect(acceptInvite(w.env, FARMER, invite.code)).rejects.toMatchObject({ code: 'INVITE_INVALID' });
    expect(await acceptInvite(w.env, MAKER, invite.code)).toMatchObject({ friendUid: FARMER, displayName: w.player(FARMER).displayName });
    expect(w.store.get(paths.friend(FARMER, MAKER))).toMatchObject({ displayName: w.player(MAKER).displayName });
    expect(w.store.get(paths.friend(MAKER, FARMER))).toMatchObject({ shortId: w.player(FARMER).shortId });
    expect(rows(w, FARMER)).toEqual(expect.arrayContaining([expect.objectContaining({ kind: 'friend_joined', actionKey: 'view' })]));
    await expect(acceptInvite(w.env, MAKER, invite.code)).rejects.toMatchObject({ code: 'ALREADY_FRIENDS' });
    await expect(acceptInvite(w.env, MAKER, 'ZZZZZZZZ')).rejects.toMatchObject({ code: 'INVITE_INVALID' });
  });
});

describe('push delivery', () => {
  async function farmerWithDevice(settings: Record<string, unknown>) {
    const w = new World();
    await w.createPlayer(FARMER);
    w.patch(paths.settings(FARMER), { pushEnabled: true, quietStart: 0, quietEnd: 0, timezone: 'UTC', locale: 'en', ...settings });
    w.store.put(paths.pushToken(FARMER, 'install_one'), { token: 'token-one-aaaaaaaaaaaa', platform: 'android', updatedAt: 2 });
    w.store.put(paths.pushToken(FARMER, 'install_two'), { token: 'token-two-aaaaaaaaaaaa', platform: 'ios', updatedAt: 1 });
    return w;
  }

  it('sends the rendered copy with routing data to every device', async () => {
    const w = await farmerWithDevice({});
    const fcm = new FakeMessenger();
    expect(await deliverNotification(w.env, fcm, FARMER, 'n_1', notification())).toBe('sent');
    expect(fcm.sent).toHaveLength(2);
    expect(fcm.sent[0]).toMatchObject({
      title: 'A friend joined',
      body: 'Bob joined your farm circle.',
      collapseKey: 'friend_joined',
      data: { route: 'ranks.friends', notificationId: 'n_1', arg_uid: MAKER },
    });
  });

  it('respects opt-out, quiet hours and missing devices', async () => {
    const off = await farmerWithDevice({ pushEnabled: false });
    expect(await deliverNotification(off.env, new FakeMessenger(), FARMER, 'n_1', notification())).toBe('disabled');
    const quiet = await farmerWithDevice({ quietStart: 6, quietEnd: 9 }); // the world clock is 08:00 UTC
    expect(await deliverNotification(quiet.env, new FakeMessenger(), FARMER, 'n_1', notification())).toBe('quiet');
    const none = new World();
    await none.createPlayer(MAKER);
    none.patch(paths.settings(MAKER), { pushEnabled: true });
    expect(await deliverNotification(none.env, new FakeMessenger(), MAKER, 'n_1', notification())).toBe('no_devices');
    expect(await deliverNotification(none.env, new FakeMessenger(), MAKER, 'n_1', { kind: 'nonsense' })).toBe('invalid');
  });

  it('forgets tokens FCM no longer knows', async () => {
    const w = await farmerWithDevice({});
    const fcm = new FakeMessenger();
    fcm.stale = ['token-two-aaaaaaaaaaaa'];
    await deliverNotification(w.env, fcm, FARMER, 'n_1', notification());
    expect(w.store.get(paths.pushToken(FARMER, 'install_two'))).toMatchObject({ token: '' });
    fcm.sent.length = 0;
    await deliverNotification(w.env, fcm, FARMER, 'n_2', notification());
    expect(fcm.sent).toHaveLength(1);
  });

  it('computes quiet hours across midnight and in the player time zone', () => {
    expect(inQuietHours(23, 22, 7)).toBe(true);
    expect(inQuietHours(6, 22, 7)).toBe(true);
    expect(inQuietHours(12, 22, 7)).toBe(false);
    expect(inQuietHours(3, 5, 5)).toBe(false);
    expect(localHour(Date.UTC(2026, 9, 21, 8), 'Africa/Lagos')).toBe(9);
    expect(localHour(Date.UTC(2026, 9, 21, 8), 'Not/AZone')).toBe(8);
    expect(renderPush('fr-FR', 'system', { message: 'Hi' })).toEqual({ title: 'Farmverse', body: 'Hi' });
  });
});

describe('account export', () => {
  it('contains the player documents and hides push tokens', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    w.store.put(paths.pushToken(FARMER, 'install_one'), { token: 'secret-token-aaaaaaaaaa', platform: 'android', updatedAt: 2 });
    w.store.put(paths.island(FARMER), { rev: 1, state: '{}' });
    const { json, truncated } = await exportAccount(w.env, FARMER);
    const doc = JSON.parse(json);
    expect(doc).toMatchObject({ format: 'farmverse-account-export', uid: FARMER, player: { shortId: '10000' }, island: { rev: 1 } });
    expect(doc.devices).toEqual([{ installationId: 'install_one', platform: 'android', updatedAt: 2 }]);
    expect(json).not.toContain('secret-token');
    expect(truncated).toBe(false);
  });
});
