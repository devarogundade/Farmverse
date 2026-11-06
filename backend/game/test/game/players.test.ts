import { describe, expect, it } from 'vitest';
import * as players from '../../src/game/players';
import { paths } from '../../src/infra/paths';
import { FARMER, MAKER, World } from './world';

describe('player services', () => {
  it('creates a player once and keeps names unique', async () => {
    const w = new World();
    expect(await players.create(w.env, FARMER, { displayName: 'Ada', avatarId: 'avatar_default' })).toEqual({ uid: FARMER });
    await expect(players.create(w.env, FARMER, { displayName: 'Ada', avatarId: 'avatar_default' })).rejects.toMatchObject({ code: 'PLAYER_EXISTS' });
    await expect(players.create(w.env, MAKER, { displayName: 'ada', avatarId: 'avatar_default' })).rejects.toMatchObject({ code: 'NAME_TAKEN' });
    expect(w.store.get(paths.username('ada'))).toEqual({ uid: FARMER });
  });

  it('starts every player the same, with a short number and default settings', async () => {
    const w = new World();
    await w.createPlayer(FARMER);
    await w.createPlayer(MAKER);
    expect(w.player(FARMER)).toMatchObject({ rev: 1, shortId: '10000', avatarId: 'avatar_default', createdAt: w.clock.nowMs() });
    expect(w.player(MAKER).shortId).toBe('10001');
    expect(w.store.get(paths.settings(FARMER))).toMatchObject({ music: true, analyticsConsent: false, termsAcceptedAt: 0 });
  });

  it('renames through the uniqueness index and updates settings', async () => {
    const w = new World();
    await players.create(w.env, FARMER, { displayName: 'Ada', avatarId: 'avatar_default' });
    await players.create(w.env, MAKER, { displayName: 'Bob', avatarId: 'avatar_default' });
    await expect(players.updateProfile(w.env, MAKER, { displayName: 'ADA' })).rejects.toMatchObject({ code: 'NAME_TAKEN' });
    await players.updateProfile(w.env, MAKER, { displayName: 'Bobby Two', allowVisitors: false });
    expect(w.store.get(paths.username('bob'))).toBeUndefined();
    expect(w.store.get(paths.username('bobby_two'))).toEqual({ uid: MAKER });
    expect(w.player(MAKER)).toMatchObject({ displayName: 'Bobby Two', rev: 2 });
    expect(w.store.get(paths.settings(MAKER))).toMatchObject({ allowVisitors: false });
    await expect(players.updateProfile(w.env, 'nobodyAAAAAAAAAAAAAAAAAA', { avatarId: 'a' })).rejects.toMatchObject({ code: 'PLAYER_NOT_FOUND' });
  });

  it('suggests free farm names', async () => {
    const w = new World();
    const { names } = await players.suggestNames(w.env, 5);
    expect(names).toHaveLength(5);
    for (const name of names) expect(name).toMatch(/^[A-Z][a-z]+ [A-Z][a-z]+$/);
  });
});
