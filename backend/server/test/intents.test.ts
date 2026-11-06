import { describe, expect, it } from 'vitest';
import { memoryLogger } from '../../game/src/core/logger';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { farmFor, farmQuote, previewRefundTo, settleIntents } from '../src/chain/intents';

const E24 = 10n ** 24n;
const E18 = 10n ** 18n;

describe('buy FARM with any token', () => {
  it('prices FARM at the fixed rate', () => {
    expect(farmFor(E24, 100)).toBe(100n * E18);
    expect(farmFor(E24 / 2n, 100)).toBe(50n * E18);
  });

  it('picks a preview refund address on the token chain', () => {
    expect(previewRefundTo('nep141:arb-0xaf88d065e77c8cc2239327c5edb3a432268e5831.omft.near')).toBe('0x0000000000000000000000000000000000000001');
    expect(previewRefundTo('nep141:sol-5ce3bf3a31af18be40ba30f721101b4341690186.omft.near')).toBe('11111111111111111111111111111111');
    expect(previewRefundTo('nep141:wrap.near')).toBe('near');
  });

  it('a live quote records the order; settling pays FARM once', async () => {
    const store = new MemoryStore();
    const statuses: Record<string, string> = { dep1: 'SUCCESS' };
    const fetchImpl = (async (url: string) => {
      if (url.endsWith('/quote')) return new Response(JSON.stringify({ quote: { amountOut: String(2n * E24), depositAddress: 'dep1', deadline: 'x' } }));
      const dep = new URL(url).searchParams.get('depositAddress')!;
      return new Response(JSON.stringify({ status: statuses[dep] }));
    }) as typeof fetch;
    const cfg = { live: true, treasury: 'treasury.near', farmPerNear: 100, fetchImpl };
    const q = await farmQuote(cfg, store, 'alice.near', 'nep141:wrap.near', '1', 'alice.near', 1);
    expect(q).toMatchObject({ live: true, depositAddress: 'dep1', farmOut: String(200n * E18) });
    const sent: string[] = [];
    const sender = { registered: async () => true, register: async () => {}, sendFarm: async (a: string, u: bigint) => { sent.push(`${a}:${u / E18}`); } };
    expect(await settleIntents(cfg, store, sender, memoryLogger(), 2)).toBe(1);
    expect(await settleIntents(cfg, store, sender, memoryLogger(), 3)).toBe(0);
    expect(sent).toEqual(['alice.near:200']);
  });
});
