import { describe, expect, it } from 'vitest';
import { eventsIn } from '../src/chain/parse';
import { txSource } from '../src/chain/txsource';

const W = { items: 'items.test', market: 'market.test' };
const log = (event: string, data: object) => `EVENT_JSON:${JSON.stringify({ standard: 'nep171', version: '1.0.0', event, data: [data] })}`;

function receipt(id: string, height: number, index: number, logs: string[]) {
  return {
    receipt: { receipt_id: id, receiver_id: W.items, predecessor_id: 'alice.test', receipt: { Action: { actions: [] } }, block_height: height, block_timestamp: 1_700_000_000_000_000_000 + height },
    execution_outcome: { id, block_height: height, block_timestamp: 1_700_000_000_000_000_000 + height, index, outcome: { executor_id: W.items, logs, status: { SuccessValue: '' } } },
  };
}

/** A fake transactions API: two pages for items (newest first), the market sharing one transaction. */
function fakeFetch(calls: string[]): typeof fetch {
  const txs = {
    t1: { transaction: { hash: 't1' }, receipts: [receipt('r1', 101, 0, [log('nft_mint', { owner_id: 'alice.test', token_ids: ['a'] })])] },
    t2: { transaction: { hash: 't2' }, receipts: [receipt('r2', 105, 1, [log('nft_burn', { owner_id: 'alice.test', token_ids: ['a'] })]), receipt('r3', 105, 0, [])] },
    t0: { transaction: { hash: 't0' }, receipts: [receipt('r0', 40, 0, [log('nft_mint', { owner_id: 'bob.test', token_ids: ['b'] })])] },
  } as Record<string, unknown>;
  return (async (url: string | URL, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body));
    calls.push(`${String(url).replace(/^.*\/v0\//, '')}:${body.account_id ?? body.tx_hashes?.join(',') ?? body.method}`);
    const json = (v: unknown) => new Response(JSON.stringify(v), { status: 200 });
    if (String(url).endsWith('/v0/account')) {
      if (body.account_id === W.market) return json({ account_txs: [{ transaction_hash: 't2', tx_block_height: 104 }] });
      return body.resume_token
        ? json({ account_txs: [{ transaction_hash: 't0', tx_block_height: 40 }], resume_token: null })
        : json({ account_txs: [{ transaction_hash: 't2', tx_block_height: 104 }, { transaction_hash: 't1', tx_block_height: 100 }], resume_token: 'next' });
    }
    if (String(url).endsWith('/v0/transactions')) return json({ transactions: body.tx_hashes.map((h: string) => txs[h]) });
    return json({ result: { header: { height: 110 } } });
  }) as typeof fetch;
}

describe('txSource', () => {
  it('reads only the watched contracts, pages back to the range, and rebuilds blocks in order', async () => {
    const calls: string[] = [];
    const source = txSource({ accounts: [W.items, W.market], txApiUrl: 'https://tx', rpcUrl: 'https://rpc', overlap: 10, fetch: fakeFetch(calls) });
    expect(await source.finalHeight()).toBe(110);

    const blocks = await source.blocks(95, 110);
    // the second items page goes back past 95 - 10: paging stops there; t0 (height 40) is left out
    expect(blocks.map((b) => b.block.header.height)).toEqual([101, 105]);
    // t2 is in both contracts' lists but fetched once
    expect(calls.filter((c) => c.startsWith('transactions'))).toEqual(['transactions:t2,t1']);
    // receipts in a block run in their outcome order
    expect(blocks[1]!.shards[0]!.receipt_execution_outcomes!.map((r) => r.receipt.receipt_id)).toEqual(['r3', 'r2']);

    const events = blocks.flatMap((b) => eventsIn(b, W));
    expect(events.map((e) => [e.event, e.id, e.blockHeight, e.txHash])).toEqual([
      ['nft_mint', 'r1:0', 101, 't1'],
      ['nft_burn', 'r2:0', 105, 't2'],
    ]);
    expect(events[0]!.blockTime.getTime()).toBe(1_700_000_000_000);
  });

  it('leaves out transactions after the range', async () => {
    const source = txSource({ accounts: [W.items], txApiUrl: 'https://tx', rpcUrl: 'https://rpc', overlap: 0, fetch: fakeFetch([]) });
    expect((await source.blocks(95, 102)).map((b) => b.block.header.height)).toEqual([101]);
  });
});
