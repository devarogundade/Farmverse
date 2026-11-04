import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { recordAdView } from '../../src/game/ads';
import { claimLogin, sliceFor, spinWheel } from '../../src/game/daily';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, World } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));
const gems = (w: World) => (w.store.get(paths.wallet(FARMER)) as { gems: number }).gems;
const boosts = (w: World) => (w.store.get(paths.inventory(FARMER)) as { boosts: Record<string, number> } | undefined)?.boosts ?? {};

async function player() {
  const w = new World();
  await w.createPlayer(FARMER);
  return w;
}

describe('daily login', () => {
  beforeEach(() => resetCatalogueCache());

  it('pays the ladder a day at a time; a missed day pauses it; day 7 starts it again', async () => {
    const w = await player();
    const first = await claimLogin(w.env, FARMER, 'k_login_0001');
    expect(first).toMatchObject({ day: 1, coins: DEFAULT_CATALOGUE.dailyLogin[0]!.coins, fresh: true });
    expect(await code(claimLogin(w.env, FARMER, 'k_login_0002'))).toBe('DAILY_CLAIMED');
    expect((await claimLogin(w.env, FARMER, 'k_login_0001')).fresh).toBe(false);
    w.clock.advance(3 * DAY);   // away for days: no reset
    expect((await claimLogin(w.env, FARMER, 'k_login_0003')).day).toBe(2);
    expect(boosts(w)['fert']).toBe(1);
    for (let d = 3; d <= 7; d++) {
      w.clock.advance(DAY);
      expect((await claimLogin(w.env, FARMER, `k_login_00${d}x`)).day).toBe(d);
    }
    w.clock.advance(DAY);
    expect((await claimLogin(w.env, FARMER, 'k_login_0099')).day).toBe(1);
  });
});

describe('lucky wheel', () => {
  beforeEach(() => resetCatalogueCache());

  it('picks slices by weight', () => {
    expect(sliceFor(DEFAULT_CATALOGUE, 0)).toBe(0);
    expect(sliceFor(DEFAULT_CATALOGUE, DEFAULT_CATALOGUE.wheel.segments[0]!.weight)).toBe(1);
    const total = DEFAULT_CATALOGUE.wheel.segments.reduce((s, x) => s + x.weight, 0);
    expect(sliceFor(DEFAULT_CATALOGUE, total - 1)).toBe(7);
  });

  it('spins once per watched ad, five a day, no extra spins', async () => {
    const w = await player();
    const start = gems(w);
    const total = DEFAULT_CATALOGUE.wheel.segments.reduce((s, x) => s + x.weight, 0);
    const jackpot = () => total - 1;   // the last slice: 25 gems
    const spins = DEFAULT_CATALOGUE.wheel.spinsPerDay;
    // live backends need AdMob's verification of the ad first
    expect(await code(spinWheel(w.env, FARMER, 'k_spin_0000', true, jackpot))).toBe('AD_NOT_VERIFIED');
    await recordAdView(w.env, { uid: FARMER, key: 'k_spin_0000', transactionId: 't', adUnit: 'u', rewardItem: 'spin', rewardAmount: 1 });
    expect(await spinWheel(w.env, FARMER, 'k_spin_0000', true, jackpot)).toMatchObject({ segment: 7, gems: 25, spinsLeft: spins - 1, fresh: true });
    expect((await spinWheel(w.env, FARMER, 'k_spin_0000', true, jackpot)).fresh).toBe(false);   // one spin per ad
    for (let i = 1; i < spins; i++) {
      const r = await spinWheel(w.env, FARMER, `k_spin_000${i}`, false, jackpot);
      expect(r.spinsLeft).toBe(spins - i - 1);
    }
    expect(gems(w)).toBe(start + 25 * spins);
    expect(await code(spinWheel(w.env, FARMER, 'k_spin_0100', false, jackpot))).toBe('WHEEL_NO_SPINS');
    w.clock.advance(DAY);
    expect((await spinWheel(w.env, FARMER, 'k_spin_0200', false, () => 0)).spinsLeft).toBe(spins - 1);
  });
});
