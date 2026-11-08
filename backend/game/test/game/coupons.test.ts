import { beforeEach, describe, expect, it } from 'vitest';
import { DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { AppError } from '../../src/core/errors';
import { resetCatalogueCache } from '../../src/game/catalogue';
import { claimCoupon, holdCouponPayout, newCouponCode, PAYOUT_HOLD_MS, redeemCoupon, releaseCouponPayout, settleCouponPayout } from '../../src/game/coupons';
import { paths } from '../../src/infra/paths';
import { DAY, FARMER, MAKER, World, islandWith } from './world';

const code = async (p: Promise<unknown>) => p.then(() => 'ok', (e: unknown) => (e instanceof AppError ? e.code : String(e)));

async function world() {
  const w = new World();
  await w.createPlayer(FARMER);
  await w.createPlayer(MAKER);
  return w;
}

describe('coupons', () => {
  beforeEach(() => resetCatalogueCache());

  it('needs a linked account for linked-only types', async () => {
    const w = await world();
    expect(DEFAULT_CATALOGUE.couponTypes['b3tr']!.linkedOnly).toBe(true);
    expect(await code(claimCoupon(w.env, FARMER, 'd1', false))).toBe('COUPON_LINK_REQUIRED');
  });

  it('makes one code per finished task (daily tasks once a day), worth the task’s amount', async () => {
    const w = await world();
    const task = DEFAULT_CATALOGUE.couponTasks['d1']!;   // harvest 20 today
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { harvested: 100 } }));
    expect(await code(claimCoupon(w.env, FARMER, 'd1', true))).toBe('COUPON_LOCKED');   // lifetime 100, but 0 today
    const today = islandWith(w.clock.nowMs(), { stats: { harvested: 100 } });
    const state = JSON.parse(today.state as string);
    state.economy.dayBase.push({ item: 'harvested', n: 100 - task.of });
    w.store.put(paths.island(FARMER), { ...today, state: JSON.stringify(state) });
    const first = await claimCoupon(w.env, FARMER, 'd1', true);
    expect(first).toMatchObject({ typeId: 'b3tr', amount: task.amount, unit: 'B3TR', fresh: true });
    expect(first.code).toMatch(/^B3TR-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$/);
    expect((await claimCoupon(w.env, FARMER, 'd1', true))).toEqual({ ...first, fresh: false });
    expect(w.store.get(paths.couponCode(first.code))).toMatchObject({ uid: FARMER, amount: task.amount, redeemedAt: 0 });
    // a lifetime goal on another player
    w.store.put(paths.island(MAKER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    const second = await claimCoupon(w.env, MAKER, 'g3', true);
    expect(second.code).not.toBe(first.code);
    // the next day the daily task pays again (from what's left)
    w.clock.advance(DAY);
    const s2 = JSON.parse(today.state as string);
    s2.economy.day += 1;
    s2.economy.dayBase.push({ item: 'harvested', n: 0 });
    w.store.put(paths.island(FARMER), { ...today, state: JSON.stringify(s2) });
    const third = await claimCoupon(w.env, FARMER, 'd1', true);
    expect(new Set([first.code, second.code, third.code]).size).toBe(3);
  });

  it('redeems a code once, never an unknown or expired one', async () => {
    const w = await world();
    w.store.put(paths.island(MAKER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    const got = await claimCoupon(w.env, MAKER, 'g3', true);
    expect(await code(redeemCoupon(w.env, 'B3TR-0000-0000'))).toBe('COUPON_UNKNOWN');
    expect(await redeemCoupon(w.env, got.code.toLowerCase())).toMatchObject({ typeId: 'b3tr', amount: got.amount, unit: 'B3TR' });
    expect(await code(redeemCoupon(w.env, got.code))).toBe('COUPON_USED');
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    const late = await claimCoupon(w.env, FARMER, 'g3', true);
    w.clock.advance(31 * DAY);
    expect(await code(redeemCoupon(w.env, late.code))).toBe('COUPON_USED');
  });

  it('makes another code when one is taken', async () => {
    const w = await world();
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    w.store.put(paths.island(MAKER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    let n = 0;
    const same = () => 0;
    const a = await claimCoupon(w.env, FARMER, 'g3', true, same);
    const b = await claimCoupon(w.env, MAKER, 'g3', true, (k) => (n++ < 8 ? 0 : 1 % k));
    expect(a.code).toBe(newCouponCode('B3TR', same));
    expect(b.code).not.toBe(a.code);
  });

  it('holds a code for one payout from the redeem page, then settles or releases it', async () => {
    const w = await world();
    w.store.put(paths.island(MAKER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    const got = await claimCoupon(w.env, MAKER, 'g3', true);
    const wallet = '0x' + 'ab'.repeat(20);
    expect(await holdCouponPayout(w.env, 'B3TR-0000-0000', wallet, ['B3TR'])).toEqual({ ok: false, why: 'unknown' });
    expect(await holdCouponPayout(w.env, got.code, wallet, ['USDT'])).toEqual({ ok: false, why: 'unsupported', unit: 'B3TR' });
    // typed in lower case with spaces around it
    expect(await holdCouponPayout(w.env, ` ${got.code.toLowerCase()} `, wallet, ['B3TR'])).toMatchObject({ ok: true, typeId: 'b3tr', amount: got.amount, unit: 'B3TR' });
    // a second request while the first is paying
    expect(await holdCouponPayout(w.env, got.code, wallet, ['B3TR'])).toEqual({ ok: false, why: 'busy' });
    // nor can the team mark it used meanwhile
    expect(await code(redeemCoupon(w.env, got.code))).toBe('COUPON_USED');
    // nothing was sent: it can be tried again
    await releaseCouponPayout(w.env, got.code, 'paused');
    expect(await holdCouponPayout(w.env, got.code, wallet, ['B3TR'])).toMatchObject({ ok: true });
    await settleCouponPayout(w.env, got.code, '0xtx');
    expect(w.store.get(paths.couponCode(got.code))).toMatchObject({ payoutState: 'paid', payoutTx: '0xtx', payoutWallet: wallet });
    expect(await holdCouponPayout(w.env, got.code, wallet, ['B3TR'])).toEqual({ ok: false, why: 'used', tx: '0xtx' });
  });

  it('frees a hold a crashed request left behind, but never one whose payment was sent', async () => {
    const w = await world();
    w.store.put(paths.island(MAKER), islandWith(w.clock.nowMs(), { stats: { harvested: 600 } }));
    const a = await claimCoupon(w.env, MAKER, 'g3', true);
    const wallet = '0x' + 'cd'.repeat(20);
    expect(await holdCouponPayout(w.env, a.code, wallet, ['B3TR'])).toMatchObject({ ok: true });
    w.clock.advance(PAYOUT_HOLD_MS + 1);
    expect(await holdCouponPayout(w.env, a.code, wallet, ['B3TR'])).toMatchObject({ ok: true });
    w.store.put(paths.couponCode(a.code), { ...(w.store.get(paths.couponCode(a.code)) as object), payoutTx: '0xsent' });
    w.clock.advance(PAYOUT_HOLD_MS + 1);
    expect(await holdCouponPayout(w.env, a.code, wallet, ['B3TR'])).toEqual({ ok: false, why: 'busy', tx: '0xsent' });
    // an expired code is said to be expired
    w.clock.advance(31 * DAY);
    w.store.put(paths.couponCode(a.code), { ...(w.store.get(paths.couponCode(a.code)) as object), payoutState: 'failed', payoutTx: '' });
    expect(await holdCouponPayout(w.env, a.code, wallet, ['B3TR'])).toEqual({ ok: false, why: 'expired' });
  });

  it('keeps level-locked tasks shut', async () => {
    const w = await world();
    w.store.put(paths.island(FARMER), islandWith(w.clock.nowMs(), { stats: { orders: 99 } }));
    expect(await code(claimCoupon(w.env, FARMER, 'g5', true))).toBe('COUPON_LOCKED');   // needs level 12
  });
});
