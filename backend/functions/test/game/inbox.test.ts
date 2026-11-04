import { describe, expect, it } from 'vitest';
import { deleteMessages, writeMail } from '../../src/game/inbox';
import { claimReward } from '../../src/game/referrals';
import { UnitOfWork } from '../../src/infra/unitOfWork';
import { FARMER, World } from './world';

describe('inbox', () => {
  it('sends mail with a reward; the reward is collected once; read mail can be deleted after', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.store.runTransaction(async (tx) => {
      const uow = new UnitOfWork(tx);
      writeMail(uow, FARMER, { title: 'Thanks!', body: 'A gift', coins: 500, gems: 10, route: '', expireDays: 30 }, w.clock.nowMs());
      writeMail(uow, FARMER, { title: 'News', body: 'No gift', coins: 0, gems: 0, route: 'news', expireDays: 30 }, w.clock.nowMs());
      uow.commit();
    });
    const notes = w.store.paths(`players/${FARMER}/notifications/`).map((p) => ({ id: p.split('/').pop()!, ...(w.store.get(p) as Record<string, unknown>) }) as Record<string, unknown> & { id: string });
    expect(notes).toHaveLength(2);
    const gift = notes.find((n) => (n['routeArgs'] as Record<string, string>)['rewardId'])!;
    const plain = notes.find((n) => n !== gift)!;
    expect(gift).toMatchObject({ kind: 'mail', actionKey: 'claim', params: expect.objectContaining({ title: 'Thanks!', coins: '500', gems: '10' }) });
    // a message with a reward still to collect stays
    expect(await deleteMessages(w.env, FARMER, [gift.id, plain.id])).toEqual({ deleted: 1 });
    const rewardId = (gift['routeArgs'] as Record<string, string>)['rewardId']!;
    expect(await claimReward(w.env, FARMER, rewardId)).toMatchObject({ coins: 500, gems: 10, fresh: true });
    expect(await deleteMessages(w.env, FARMER, [gift.id])).toEqual({ deleted: 1 });
  });
});
