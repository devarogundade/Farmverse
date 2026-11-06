import type { NearBlock, ReceiptOutcome } from './parse';

/**
 * Blocks for a height range, holding only what the indexer needs. The neardata source downloads
 * every block (about 70 KB, one request each) and the indexer throws nearly all of it away; this
 * one asks FastNEAR's transactions API for the watched contracts' own transactions instead, so a
 * quiet minute costs one request per contract rather than sixty blocks.
 */
export interface RangeSource {
  /** Height of the latest final block. */
  finalHeight(): Promise<number>;
  /** What happened in heights `from`..`to` (inclusive), as blocks `eventsIn` can read, in order. */
  blocks(from: number, to: number): Promise<NearBlock[]>;
}

export const TX_API_URL = { testnet: 'https://tx.test.fastnear.com', mainnet: 'https://tx.main.fastnear.com' } as const;

export interface TxSourceOptions {
  /** The contracts to follow (their transactions include every cross-contract call into them). */
  readonly accounts: readonly string[];
  /** FastNEAR's transactions API (TX_API_URL). */
  readonly txApiUrl: string;
  /** A NEAR RPC for the final height. */
  readonly rpcUrl: string;
  /**
   * Transactions this many blocks before `from` are read again. A transaction's receipts run a few
   * blocks after it, so ones near the head may not have all run yet: reading them again on the
   * next pass picks up the late ones (events have stable ids, so the rest are skipped).
   */
  readonly overlap?: number;
  /** Hashes per details request. */
  readonly detailsBatch?: number;
  readonly fetch?: typeof fetch;
  readonly timeoutMs?: number;
}

interface AccountTx {
  readonly transaction_hash: string;
  readonly tx_block_height: number;
}

interface TxDetails {
  readonly transaction: { hash: string };
  readonly receipts: Array<{
    receipt: ReceiptOutcome['receipt'] & { block_height: number; block_timestamp: number | string };
    execution_outcome: ReceiptOutcome['execution_outcome'] & { block_height: number; block_timestamp: number | string; index?: number };
  }>;
}

export function txSource(o: TxSourceOptions): RangeSource {
  const doFetch = o.fetch ?? fetch;
  const timeoutMs = o.timeoutMs ?? 20_000;
  const overlap = o.overlap ?? 60;
  const detailsBatch = o.detailsBatch ?? 20;
  const txApi = o.txApiUrl.replace(/\/$/, '');

  async function post<T>(url: string, body: unknown): Promise<T> {
    const res = await doFetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
    return (await res.json()) as T;
  }

  /** The account's transactions at heights >= `from` (pages run newest first). */
  async function accountTxs(account: string, from: number): Promise<AccountTx[]> {
    const out: AccountTx[] = [];
    let resume: string | undefined;
    for (;;) {
      const page = await post<{ account_txs?: AccountTx[]; resume_token?: string | null }>(`${txApi}/v0/account`, {
        account_id: account,
        ...(resume ? { resume_token: resume } : {}),
      });
      const txs = page.account_txs ?? [];
      out.push(...txs.filter((t) => t.tx_block_height >= from));
      // done once a page reaches back past `from`, or there is nothing older
      if (!txs.length || !page.resume_token || txs[txs.length - 1]!.tx_block_height < from) return out;
      resume = page.resume_token;
    }
  }

  return {
    async finalHeight() {
      const json = await post<{ result?: { header?: { height?: number } }; error?: unknown }>(o.rpcUrl, {
        jsonrpc: '2.0',
        id: 'fv-indexer',
        method: 'block',
        params: { finality: 'final' },
      });
      const height = json.result?.header?.height;
      if (!height) throw new Error(`final block: ${JSON.stringify(json.error ?? json).slice(0, 200)}`);
      return height;
    },

    async blocks(from, to) {
      const since = Math.max(0, from - overlap);
      // one list per contract; a transaction touching both comes back twice
      const hashes = new Map<string, number>();
      for (const account of o.accounts) {
        for (const t of await accountTxs(account, since)) if (t.tx_block_height <= to) hashes.set(t.transaction_hash, t.tx_block_height);
      }
      if (!hashes.size) return [];

      // the transactions' receipts, grouped back into the blocks they ran in
      const byHeight = new Map<number, { time: string; rows: Array<ReceiptOutcome & { order: number }> }>();
      const list = [...hashes.keys()];
      for (let i = 0; i < list.length; i += detailsBatch) {
        const { transactions } = await post<{ transactions: (TxDetails | null)[] }>(`${txApi}/v0/transactions`, {
          tx_hashes: list.slice(i, i + detailsBatch),
        });
        for (const t of transactions) {
          if (!t) continue;
          for (const r of t.receipts) {
            const height = r.execution_outcome.block_height;
            const time = BigInt(Math.trunc(Number(r.execution_outcome.block_timestamp))).toString();
            const block = byHeight.get(height) ?? { time, rows: [] };
            block.rows.push({
              tx_hash: t.transaction.hash,
              execution_outcome: r.execution_outcome,
              receipt: r.receipt,
              order: r.execution_outcome.index ?? 0,
            });
            byHeight.set(height, block);
          }
        }
      }
      return [...byHeight.entries()]
        .sort(([a], [b]) => a - b)
        .map(([height, b]) => ({
          block: { header: { height, timestamp_nanosec: b.time, timestamp: Number(b.time) } },
          shards: [{ shard_id: 0, receipt_execution_outcomes: b.rows.sort((x, y) => x.order - y.order).map(({ order: _, ...r }) => r) }],
        }));
    },
  };
}
