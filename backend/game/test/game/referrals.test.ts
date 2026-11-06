import { describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { acceptInvite, createInvite } from '../../src/game/friends';
import { claimReferral, claimReward, islandLevel, REFERRAL } from '../../src/game/referrals';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, MAKER, World } from './world';

const island = (bestLevel: number) => ({ rev: 1, state: JSON.stringify({ rulesVersion: 1, economy: { bestLevel } }) });
const rewardsOf = (w: World, uid: string) => w.store.paths(`players/${uid}/rewards/`).map((p) => ({ id: p.split('/').pop()!, ...w.store.get(p)! }));

/** FARMER invites MAKER, a brand-new player. */
async function referred() {
  const w = new World();
  await w.createPlayer(FARMER);
  await w.createPlayer(MAKER);
  const { code } = await createInvite(w.env, FARMER);
  await acceptInvite(w.env, MAKER, code);
  return w;
}

describe('referrals', () => {
  it('records a referral when a new player accepts an invite', async () => {
    const w = await referred();
    expect(w.store.get(paths.referral(MAKER))).toMatchObject({ inviterUid: FARMER, status: 'pending' });
  });

  it('does not count an established player as referred', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    w.clock.advance(8 * DAY);
    const { code } = await createInvite(w.env, FARMER);
    await acceptInvite(w.env, MAKER, code);
    expect(w.store.get(paths.referral(MAKER))).toBeUndefined();
    expect(await claimReferral(w.env, MAKER)).toEqual({ rewarded: false, reason: 'not_referred' });
  });

  it('waits for the referral level on the server copy of the island', async () => {
    const w = await referred();
    expect(await claimReferral(w.env, MAKER)).toEqual({ rewarded: false, reason: 'level' });
    w.store.put(paths.island(MAKER), island(REFERRAL.level - 1));
    expect(await claimReferral(w.env, MAKER)).toEqual({ rewarded: false, reason: 'level' });
    expect(rewardsOf(w, FARMER)).toEqual([]);
  });

  it('rewards both players once at the referral level, and tells both', async () => {
    const w = await referred();
    w.store.put(paths.island(MAKER), island(REFERRAL.level));
    expect(await claimReferral(w.env, MAKER)).toEqual({ rewarded: true, reason: 'none' });
    expect(await claimReferral(w.env, MAKER)).toEqual({ rewarded: false, reason: 'already' });
    const mine = rewardsOf(w, MAKER);
    const theirs = rewardsOf(w, FARMER);
    expect(mine).toEqual([expect.objectContaining({ reason: 'referral_invitee', coins: 600, gems: 5, claimedAt: 0, about: w.player(FARMER).displayName })]);
    expect(theirs).toEqual([expect.objectContaining({ reason: 'referral_inviter', coins: 600, gems: 5, about: w.player(MAKER).displayName })]);
    const notes = w.store.paths(`players/${FARMER}/notifications/`).map((p) => w.store.get(p)!);
    expect(notes).toEqual(expect.arrayContaining([expect.objectContaining({ kind: 'referral_reward', params: expect.objectContaining({ coins: '600', gems: '5' }) })]));
  });

  it('pays a reward out exactly once', async () => {
    const w = await referred();
    w.store.put(paths.island(MAKER), island(9));
    await claimReferral(w.env, MAKER);
    const [reward] = rewardsOf(w, MAKER);
    expect(await claimReward(w.env, MAKER, reward!.id)).toEqual({ coins: 600, gems: 5, fresh: true });
    expect(await claimReward(w.env, MAKER, reward!.id)).toEqual({ coins: 0, gems: 0, fresh: false });
    // the gems went into the server wallet, once
    expect((w.store.get(paths.wallet(MAKER)) as { gems: number }).gems).toBe(DEFAULT_CATALOGUE.startingGems + 5);
    await expect(claimReward(w.env, FARMER, reward!.id)).rejects.toMatchObject({ code: 'NOT_FOUND' });
  });

  it('reads the island level defensively', () => {
    expect(islandLevel(undefined)).toBe(0);
    expect(islandLevel({ state: 'not json' })).toBe(0);
    expect(islandLevel({ state: '{"economy":{"bestLevel":"5"}}' })).toBe(0);
    expect(islandLevel(island(7))).toBe(7);
  });
});
