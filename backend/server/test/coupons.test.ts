import Fastify from 'fastify';
import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../game/src/core/clock';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { paths } from '../../game/src/infra/paths';
import { couponRoutes } from '../src/coupons/routes';
import { redeemForPayout, type Payer, type PayoutResult, type RedeemDeps } from '../src/coupons/redeem';
import { toWei } from '../src/coupons/vebetter';

const WALLET = '0x' + 'Ab'.repeat(20);
const B3TR = 'B3TR-7KQ2-M9XD';
const USDT = 'USDT-7KQ2-M9XD';

function setup(result: (n: number) => PayoutResult | 'throw' = () => ({ sent: true, tx: '0xt1', confirmed: true }), payers = true) {
  const clock = new FixedClock(Date.UTC(2026, 10, 8, 9));
  const store = new MemoryStore();
  const code = (c: string, unit: string, extra: object = {}) =>
    store.put(paths.couponCode(c), { code: c, typeId: unit.toLowerCase(), taskId: 'd1', task: 'Daily · Harvest 20 crops', day: '', issuedAt: clock.nowMs(), expiresAt: clock.nowMs() + 864e5, amount: 10, uid: 'u1', unit, redeemedAt: 0, ...extra });
  code(B3TR, 'B3TR');
  code(USDT, 'USDT');
  const calls: { amount: number; receiver: string }[] = [];
  const payer: Payer = {
    unit: 'B3TR',
    async pay(req, onSent) {
      calls.push({ amount: req.amount, receiver: req.receiver });
      const r = result(calls.length);
      if (r === 'throw') throw new Error('node down');
      if (r.sent || r.tx) await onSent(r.tx!);
      return r;
    },
  };
  const deps: RedeemDeps = { env: { store, clock, log: memoryLogger() }, payers: payers ? [payer] : [], log: memoryLogger() };
  return { deps, store, clock, calls, doc: (c: string) => store.get(paths.couponCode(c)) as Record<string, unknown> };
}

describe('coupon redeem page', () => {
  it('pays a B3TR code to the wallet once', async () => {
    const t = setup();
    expect(await redeemForPayout(t.deps, ' b3tr-7kq2-m9xd ', WALLET)).toEqual({ status: 200, ok: true, amount: 10, unit: 'B3TR', tx: '0xt1', confirmed: true });
    expect(t.calls).toEqual([{ amount: 10, receiver: WALLET }]);
    expect(t.doc(B3TR)).toMatchObject({ payoutState: 'paid', payoutTx: '0xt1', payoutWallet: WALLET.toLowerCase() });
    expect(Number(t.doc(B3TR)['redeemedAt'])).toBeGreaterThan(0);
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toEqual({ status: 409, ok: false, code: 'used', tx: '0xt1' });
    expect(t.calls).toHaveLength(1);
  });

  it('checks the code and the wallet before anything else', async () => {
    const t = setup();
    expect(await redeemForPayout(t.deps, 'hello', WALLET)).toMatchObject({ status: 400, code: 'bad_code' });
    expect(await redeemForPayout(t.deps, B3TR, '0x123')).toMatchObject({ status: 400, code: 'bad_wallet' });
    expect(await redeemForPayout(t.deps, 'B3TR-0000-0000', WALLET)).toMatchObject({ status: 404, code: 'unknown' });
    t.clock.advance(2 * 864e5);
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toMatchObject({ status: 410, code: 'expired' });
    expect(t.calls).toHaveLength(0);
  });

  it('says USDT is coming soon, and B3TR is closed while no payer is set up, holding neither', async () => {
    const t = setup();
    expect(await redeemForPayout(t.deps, USDT, WALLET)).toEqual({ status: 409, ok: false, code: 'coming_soon', unit: 'USDT' });
    expect(t.doc(USDT)['payoutState']).toBeUndefined();
    const off = setup(undefined, false);
    expect(await redeemForPayout(off.deps, B3TR, WALLET)).toEqual({ status: 503, ok: false, code: 'closed', unit: 'B3TR' });
    expect(off.doc(B3TR)['payoutState']).toBeUndefined();
  });

  it('frees the code when nothing was paid, so it can be tried again', async () => {
    const t = setup((n) => (n === 1 ? { sent: false, why: 'paused' } : n === 2 ? { sent: false, why: 'reverted', tx: '0xbad' } : { sent: true, tx: '0xok', confirmed: true }));
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toEqual({ status: 503, ok: false, code: 'paused' });
    expect(t.doc(B3TR)).toMatchObject({ payoutState: 'failed', redeemedAt: 0 });
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toEqual({ status: 502, ok: false, code: 'failed' });
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toMatchObject({ ok: true, tx: '0xok' });
    expect(t.calls).toHaveLength(3);
  });

  it('never pays twice: an unconfirmed payment keeps the code held with its transaction', async () => {
    const t = setup(() => ({ sent: true, tx: '0xpending', confirmed: false }));
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toMatchObject({ ok: true, tx: '0xpending', confirmed: false });
    t.clock.advance(60 * 60_000);
    expect(await redeemForPayout(t.deps, B3TR, WALLET)).toEqual({ status: 409, ok: false, code: 'busy', tx: '0xpending' });
    expect(t.calls).toHaveLength(1);
  });

  it('answers over HTTP with the explorer link, and limits how often a code is tried', async () => {
    const t = setup();
    const hits = new Map<string, number>();
    const limiter = { hit: async (key: string, limit: number) => (hits.set(key, (hits.get(key) ?? 0) + 1), hits.get(key)! <= limit) };
    const app = Fastify();
    await app.register(couponRoutes({ ...t.deps, limiter, explorerTxUrl: 'https://explore.test/tx/' }), { prefix: '/v1/coupons' });
    const ok = await app.inject({ method: 'POST', url: '/v1/coupons/redeem', payload: { code: B3TR, wallet: WALLET } });
    expect(ok.statusCode).toBe(200);
    expect(ok.json()).toMatchObject({ ok: true, amount: 10, unit: 'B3TR', txUrl: 'https://explore.test/tx/0xt1' });
    const codes = await Promise.all(Array.from({ length: 6 }, () => app.inject({ method: 'POST', url: '/v1/coupons/redeem', payload: { code: USDT, wallet: WALLET } })));
    expect(codes.map((r) => r.statusCode).sort()).toEqual([409, 409, 409, 409, 409, 429]);
    await app.close();
  });

  it('turns whole tokens into wei', () => {
    expect(toWei(10)).toBe(10n * 10n ** 18n);
    expect(toWei(0.5)).toBe(5n * 10n ** 17n);
    expect(toWei(1_000_000)).toBe(10n ** 24n);
  });
});
