import { describe, expect, it } from 'vitest';
import { FixedClock } from '../../game/src/core/clock';
import { memoryLogger } from '../../game/src/core/logger';
import { paths } from '../../game/src/infra/paths';
import { MemoryStore } from '../../game/src/infra/memoryStore';
import { backstopRedeems, type UnclaimedRedeem } from '../src/mint/mint';
import { parseRedeems, type TxStatus } from '../src/mint/nearChain';

describe('redeem backstop', () => {
  it('credits a redeem nobody reported, once', async () => {
    const store = new MemoryStore();
    const env = { store, clock: new FixedClock(1_800_000_000_000), log: memoryLogger() };
    const wheat: UnclaimedRedeem = { tokenId: 'g-1', ownerId: 'alice.testnet', uid: 'uA', item: { kind: 'good', item_id: 'wheat', quantity: 50, props: '{}' }, txHash: 'H' };
    const fert: UnclaimedRedeem = { tokenId: 'g-2', ownerId: 'alice.testnet', uid: 'uA', item: { kind: 'boost', item_id: 'fert', quantity: 2, props: '{}' }, txHash: null };
    expect(await backstopRedeems(env, async () => [wheat, fert], memoryLogger())).toBe(2);
    expect(store.get(paths.redemption('g-1'))?.['uid']).toBe('uA');
    expect((store.get(paths.grants('uA'))?.['items'] as unknown[]).length).toBe(1);
    expect((store.get(paths.inventory('uA'))?.['boosts'] as Record<string, number>)['fert']).toBe(2);
    // the finder returns it again (raced with the player's own call): nothing more is given
    expect(await backstopRedeems(env, async () => [wheat, fert], memoryLogger())).toBe(0);
    expect((store.get(paths.grants('uA'))?.['items'] as unknown[]).length).toBe(1);
  });
});

describe('buy-and-use transactions', () => {
  it('a drop bought with use:true counts as the buyer redeeming it', () => {
    const redeem = `EVENT_JSON:${JSON.stringify({
      standard: 'farmverse_items',
      version: '1.0.0',
      event: 'item_redeem',
      data: [{ token_id: 'd-farm_pass_s1-7', owner_id: 'alice.testnet', item: { kind: 'pack', item_id: 'farm_pass', quantity: 1, props: '{"edition":7}' } }],
    })}`;
    const tx: TxStatus = {
      status: { SuccessValue: 'IjI1MDAwMDAwMDAwMDAwMDAwMDAwMCI=' },
      // sent to the FARM contract (ft_transfer_call), not to items.redeem
      transaction: { signer_id: 'alice.testnet', receiver_id: 'farm.farmverse.testnet' },
      receipts_outcome: [
        { outcome: { executor_id: 'farm.farmverse.testnet', logs: [], status: { SuccessValue: '' } } },
        { outcome: { executor_id: 'items.farmverse.testnet', logs: [redeem], status: { SuccessValue: '' } } },
      ],
    };
    const seen = parseRedeems(tx, 'alice.testnet', 'items.farmverse.testnet');
    expect(seen.map((s) => s.tokenId)).toEqual(['d-farm_pass_s1-7']);
    // the drop failed inside items (sold out): its receipt failed, nothing to credit
    const failed = { ...tx, receipts_outcome: [tx.receipts_outcome[0]!, { outcome: { ...tx.receipts_outcome[1]!.outcome, status: { Failure: {} } } }] };
    expect(parseRedeems(failed, 'alice.testnet', 'items.farmverse.testnet')).toEqual([]);
  });
});

describe('land deeds', () => {
  it('a redeemed deed opens a wild plot', async () => {
    const { redeemOps } = await import('../src/mint/mint');
    expect(redeemOps({ kind: 'land', item_id: 'deed', quantity: 1, props: '{"edition":3}' })).toEqual([{ op: 'openPlot' }]);
    expect(redeemOps({ kind: 'land', item_id: 'field', quantity: 1, props: '{"level":2,"price":50}' })).toEqual([{ op: 'hold', id: 'field', level: 2, price: 50 }]);
  });
});
