import { randomInt } from 'node:crypto';
import { CouponDoc, type CatalogueDoc, type CouponTask } from '@farmverse/contracts';
import { AppError, corrupt } from '../core/errors';
import { MS_PER_DAY } from '../domain';
import { paths } from '../infra/paths';
import { UnitOfWork } from '../infra/unitOfWork';
import { catalogue } from './catalogue';
import { dayNumber, islandProgress, type IslandProgress } from './goals';
import type { Env } from './services';
import { utcDay } from './wallet';

/**
 * Coupons (module 6): partner vouchers with real value. There are no code pools: a finished task
 * (checked on the server's island) makes a new random code, worth the task's `amount` in the type's
 * unit, records it on the player (`players/{uid}/coupons/{taskId}_{day|once}` — so a task pays once, or
 * once a day) and in the registry `couponCodes/{code}` (server only), which the redeem page checks and
 * marks used through `coupons-redeem`.
 */

/** Crockford base32 without I, L, O, U: easy to read and type. */
const ALPHABET = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';

/** A new code: PREFIX-XXXX-XXXX (40 random bits). */
export function newCouponCode(prefix: string, random: (n: number) => number = randomInt): string {
  const part = () => Array.from({ length: 4 }, () => ALPHABET[random(ALPHABET.length)]).join('');
  return `${prefix}-${part()}-${part()}`;
}

/** A task's progress on the island (see CouponTask.progress). */
export function couponProgress(task: CouponTask, island: IslandProgress | undefined, nowMs: number): number {
  if (!island) return 0;
  const p = task.progress;
  if (p.startsWith('today:')) return island.day === dayNumber(nowMs) ? island.today(p.slice(6)) : 0;
  if (p === 'goals') return island.goalIndex;
  if (p === 'level') return island.bestLevel;
  return island.stat(p);
}

const ATTEMPTS = 4;

export async function claimCoupon(
  env: Env, uid: string, taskId: string, linked: boolean, random?: (n: number) => number,
): Promise<{ code: string; typeId: string; expiresAt: number; amount: number; unit: string; fresh: boolean }> {
  const cat: CatalogueDoc = await catalogue(env);
  const task = cat.couponTasks[taskId];
  if (!task) throw new AppError('INVALID_ARGUMENT');
  const type = cat.couponTypes[task.typeId];
  if (!type) throw new AppError('INVALID_ARGUMENT');
  if (type.linkedOnly && !linked) throw new AppError('COUPON_LINK_REQUIRED');

  for (let attempt = 0; attempt < ATTEMPTS; attempt++) {
    const nowMs = env.clock.nowMs();
    const day = task.daily ? utcDay(nowMs) : '';
    const docId = `${taskId}_${task.daily ? day : 'once'}`;
    const code = newCouponCode(type.codePrefix, random);
    const outcome = await env.store.runTransaction(async (tx) => {
      const uow = new UnitOfWork(tx);
      const [mine, islandSnap, taken] = await uow.readAll([paths.coupon(uid, docId), paths.island(uid), paths.couponCode(code)]);
      if (mine?.exists) {
        const had = CouponDoc.safeParse(mine.data);
        if (!had.success) throw corrupt('coupon', had.error);
        return { code: had.data.code, typeId: had.data.typeId, expiresAt: had.data.expiresAt, amount: had.data.amount, unit: type.unit, fresh: false };
      }
      const island = islandProgress(islandSnap?.data);
      if (task.lockLevel > 0 && (island?.bestLevel ?? 1) < task.lockLevel) throw new AppError('COUPON_LOCKED', { have: island?.bestLevel ?? 1, need: task.lockLevel });
      const have = couponProgress(task, island, nowMs);
      if (have < task.of) throw new AppError('COUPON_LOCKED', { have, need: task.of });
      if (taken?.exists) return undefined;   // that code exists already (rare): make another
      const coupon: CouponDoc = {
        code, typeId: task.typeId, taskId,
        task: `${task.daily ? 'Daily' : 'Goal'} · ${task.name}`.slice(0, 60),
        day, issuedAt: nowMs, expiresAt: nowMs + type.expiryDays * MS_PER_DAY, amount: task.amount,
      };
      uow.set(paths.couponCode(code), { ...coupon, uid, unit: type.unit, redeemedAt: 0 });
      uow.set(paths.coupon(uid, docId), coupon);
      uow.commit();
      return { code, typeId: task.typeId, expiresAt: coupon.expiresAt, amount: task.amount, unit: type.unit, fresh: true };
    });
    if (outcome) return outcome;
    env.log.info('coupon code taken; making another', { uid, taskId, attempt });
  }
  throw new AppError('CONFLICT');
}

/** `coupons-redeem` (the redeem page, admin accounts only): what a code is worth; marks it used, once. */
export async function redeemCoupon(env: Env, code: string): Promise<{ typeId: string; amount: number; unit: string; redeemedAt: number }> {
  const normalised = code.trim().toUpperCase();
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const snap = await uow.read(paths.couponCode(normalised));
    if (!snap.exists || !snap.data) throw new AppError('COUPON_UNKNOWN');
    const d = snap.data;
    if (Number(d['redeemedAt'] ?? 0) > 0 || Number(d['expiresAt'] ?? 0) < nowMs) throw new AppError('COUPON_USED');
    uow.set(paths.couponCode(normalised), { redeemedAt: nowMs }, { merge: true });
    uow.commit();
    return { typeId: String(d['typeId']), amount: Number(d['amount']), unit: String(d['unit']), redeemedAt: nowMs };
  });
}
