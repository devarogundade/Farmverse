import { randomInt } from 'node:crypto';
import { DailyDoc, type CatalogueDoc } from '@farmverse/contracts';
import { AppError, corrupt, legacyDoc } from '../core/errors';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { preparePrize, type PrizePaid } from './prizes';
import type { Env } from './services';
import { utcDay } from './wallet';

/**
 * Daily login ladder and the Lucky Wheel (module 4). `state/daily` keeps where the player is; the
 * server picks every prize from the catalogue (`dailyLogin`, `wheel`), never the device.
 */

/** A random integer in [0, max): crypto in production, fixed in tests. */
export type Random = (max: number) => number;
export const cryptoRandom: Random = (max) => randomInt(max);

function readDaily(data: Record<string, unknown> | undefined, nowMs: number): DailyDoc {
  if (!data || legacyDoc('daily', data)) return { loginDay: 1, loginOn: '', wheelOn: '', wheelUsed: 0, updatedAt: nowMs };
  const parsed = DailyDoc.safeParse(data);
  if (!parsed.success) throw corrupt('daily', parsed.error);
  return { ...parsed.data };
}

async function replay<T>(uow: UnitOfWork, path: string): Promise<T | undefined> {
  const entry = await uow.read(path);
  if (!entry.exists) return undefined;
  const r = entry.data?.['result'] as T | undefined;
  if (!r) throw new AppError('CONFLICT');
  return { ...r, fresh: false };
}

/** `daily-claimLogin`: today's step of the ladder, once per UTC day. Missed days pause the ladder. */
export async function claimLogin(env: Env, uid: string, key: string): Promise<PrizePaid & { day: number; fresh: boolean }> {
  const cat = await catalogue(env);
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'd_' + key);
    const seen = await replay<PrizePaid & { day: number; fresh: boolean }>(uow, ledgerPath);
    if (seen) return seen;
    const daily = readDaily((await uow.read(paths.daily(uid))).data, nowMs);
    const today = utcDay(nowMs);
    if (daily.loginOn === today) throw new AppError('DAILY_CLAIMED');
    const day = daily.loginDay;
    const prize = cat.dailyLogin[day - 1]!;
    const pay = await preparePrize(uow, cat, uid, prize, 'login_' + key, nowMs);
    const result = { ...pay.paid, day, fresh: true };
    uow.set(paths.daily(uid), { ...daily, loginDay: day >= 7 ? 1 : day + 1, loginOn: today, updatedAt: nowMs });
    pay.apply();
    uow.set(ledgerPath, { kind: 'login', ref: String(day), at: nowMs, result });
    uow.commit();
    return result;
  });
}

/** The wheel slice for a roll in [0, total weight). */
export function sliceFor(cat: CatalogueDoc, roll: number): number {
  let r = roll;
  const segs = cat.wheel.segments;
  for (let i = 0; i < segs.length; i++) {
    if (r < segs[i]!.weight) return i;
    r -= segs[i]!.weight;
  }
  return segs.length - 1;
}

/**
 * `wheel-spin`: one spin per watched rewarded ad, at most `spinsPerDay` per UTC day — no free spins, no
 * extra spins. `key` is the ad's key: on live backends AdMob's verification for it must have arrived
 * (`adViews/{uid}_{key}`), and a key spins once. The server rolls the slice and pays it.
 */
export async function spinWheel(
  env: Env, uid: string, key: string, requireVerification: boolean, random: Random = cryptoRandom,
): Promise<PrizePaid & { segment: number; spinsLeft: number; fresh: boolean }> {
  const cat = await catalogue(env);
  type Spin = PrizePaid & { segment: number; spinsLeft: number; fresh: boolean };
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const ledgerPath = paths.ledger(uid, 'w_' + key);
    const seen = await replay<Spin>(uow, ledgerPath);
    if (seen) return seen;
    const [dailySnap, view, bought] = await uow.readAll([paths.daily(uid), paths.adView(uid, key), paths.purchases(uid)]);
    // No Ads: the spin is paid without watching an ad
    if (requireVerification && !view?.exists && bought?.data?.['noAds'] !== true) throw new AppError('AD_NOT_VERIFIED');
    const daily = readDaily(dailySnap?.data, nowMs);
    const today = utcDay(nowMs);
    const used = daily.wheelOn === today ? daily.wheelUsed : 0;
    if (used >= cat.wheel.spinsPerDay) throw new AppError('WHEEL_NO_SPINS');
    const total = cat.wheel.segments.reduce((sum, s) => sum + s.weight, 0);
    const segment = sliceFor(cat, random(total));
    const pay = await preparePrize(uow, cat, uid, cat.wheel.segments[segment]!.prize, 'wheel_' + key, nowMs);
    const result: Spin = { ...pay.paid, segment, spinsLeft: Math.max(0, cat.wheel.spinsPerDay - used - 1), fresh: true };
    uow.set(paths.daily(uid), { ...daily, wheelOn: today, wheelUsed: used + 1, updatedAt: nowMs });
    pay.apply();
    uow.set(ledgerPath, { kind: 'wheel', ref: String(segment), verified: view?.exists === true, at: nowMs, result });
    uow.commit();
    return result;
  });
}
