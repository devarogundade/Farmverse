import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE, passSeason } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { grantEntitlement } from '../../src/game/entitlements';
import { claimPass, joinPass } from '../../src/game/pass';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, World, islandWith } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));
const P = DEFAULT_CATALOGUE.pass;
const xpForTier = (t: number) => (t - 1) * P.xpPerTier * P.islandXpPerPassXp;

describe('farm pass', () => {
  beforeEach(() => resetCatalogueCache());

  it('rolls seasons from the epoch', () => {
    const s0 = passSeason(P, P.epochMs + 1);
    expect(s0).toMatchObject({ id: 's0', startsAt: P.epochMs });
    expect(passSeason(P, s0.endsAt).id).toBe('s1');
  });

  it('opens tiers with island XP earned since joining; each once; premium needs the pass', async () => {
    const w = new World();
    w.clock.set(P.epochMs + DAY);
    await w.createPlayer(FARMER);
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { xp: 5_000 }));
    expect(await joinPass(w.env, FARMER)).toMatchObject({ seasonId: 's0', baseXp: 5_000 });
    expect((await joinPass(w.env, FARMER)).baseXp).toBe(5_000);   // idempotent
    expect(await claimPass(w.env, FARMER, 1, false, 'k_pass_00001')).toMatchObject({ ...P.tiers[0]!.free, fresh: true });
    expect(await code(claimPass(w.env, FARMER, 1, false, 'k_pass_00002'))).toBe('PASS_CLAIMED');
    expect(await code(claimPass(w.env, FARMER, 3, false, 'k_pass_00003'))).toBe('PASS_LOCKED');
    expect(await code(claimPass(w.env, FARMER, 1, true, 'k_pass_00004'))).toBe('PASS_PREMIUM_REQUIRED');
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { xp: 5_000 + xpForTier(3) }));
    expect((await claimPass(w.env, FARMER, 3, false, 'k_pass_00005')).fresh).toBe(true);
    expect((w.store.get(paths.pass(FARMER)) as { free: number[] }).free).toEqual([1, 3]);
  });

  it('starts over in a new season', async () => {
    const w = new World();
    w.clock.set(P.epochMs + DAY);
    await w.createPlayer(FARMER);
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { xp: 0 }));
    await claimPass(w.env, FARMER, 1, false, 'k_pass_00010');
    w.clock.advance(P.seasonDays * DAY);
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { xp: 900 }));
    expect(await joinPass(w.env, FARMER)).toMatchObject({ seasonId: 's1', baseXp: 900 });
    expect((await claimPass(w.env, FARMER, 1, false, 'k_pass_00011')).fresh).toBe(true);
  });

  it('a pass used while premium is on is banked and turns on the next season', async () => {
    const w = new World();
    w.clock.set(P.epochMs + DAY);
    await w.createPlayer(FARMER);
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { xp: 0 }));
    await grantEntitlement(w.env, FARMER, { kind: 'pass' }, 'nft_d-farm_pass_s1-1');
    await grantEntitlement(w.env, FARMER, { kind: 'pass' }, 'nft_d-farm_pass_s1-2');
    expect(w.store.get(paths.pass(FARMER))?.['premiumActive']).toBe(true);
    expect(w.store.get(paths.purchases(FARMER))?.['passCredits']).toBe(1);

    w.clock.advance(P.seasonDays * DAY);
    await joinPass(w.env, FARMER);
    expect(w.store.get(paths.pass(FARMER))).toMatchObject({ seasonId: 's1', premiumActive: true });
    expect(w.store.get(paths.purchases(FARMER))?.['passCredits']).toBe(0);

    // the season after that: no credit left, premium is off
    w.clock.advance(P.seasonDays * DAY);
    await joinPass(w.env, FARMER);
    expect(w.store.get(paths.pass(FARMER))).toMatchObject({ seasonId: 's2', premiumActive: false });
  });
});
