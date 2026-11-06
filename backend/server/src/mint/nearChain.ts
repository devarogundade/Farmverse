/**
 * The chain reads mint and redeem rely on, straight from a NEAR RPC node (never the indexer:
 * money-moving decisions check the chain itself, docs 13 §2). An interface so tests need no network.
 */

/** A Farmverse item as the contract stores it (`items.get_item` / the `item_redeem` event). */
export interface ChainItem {
  readonly kind: string;
  readonly item_id: string;
  readonly quantity: number;
  readonly props: string;
}

export interface RedeemSeen {
  readonly tokenId: string;
  readonly ownerId: string;
  readonly item: ChainItem;
}

export interface ChainReader {
  /** Whether the token exists now, or existed and was burned (either way: it was minted). */
  wasMinted(tokenId: string): Promise<boolean>;
  /**
   * The redeems in a transaction signed by `signerId`, if it succeeded and its `item_redeem` events
   * came from the items contract. Empty when the transaction is unknown, failed, or isn't a redeem.
   */
  redeemsIn(txHash: string, signerId: string, ownerId?: string): Promise<RedeemSeen[]>;
}

type RpcResult<T> = { result?: T; error?: { cause?: { name?: string }; message?: string } };

export function rpcChainReader(rpcUrl: string, itemsContract: string, fetchImpl: typeof fetch = fetch): ChainReader {
  async function rpc<T>(method: string, params: unknown): Promise<RpcResult<T>> {
    const res = await fetchImpl(rpcUrl, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ jsonrpc: '2.0', id: 'farmverse', method, params }),
      signal: AbortSignal.timeout(10_000),
    });
    if (!res.ok) throw new Error(`NEAR RPC answered ${res.status}`);
    return (await res.json()) as RpcResult<T>;
  }

  async function view<T>(methodName: string, args: object): Promise<T> {
    const out = await rpc<{ result: number[] }>('query', {
      request_type: 'call_function',
      finality: 'final',
      account_id: itemsContract,
      method_name: methodName,
      args_base64: Buffer.from(JSON.stringify(args)).toString('base64'),
    });
    if (!out.result) throw new Error(`view ${methodName} failed: ${out.error?.message ?? 'no result'}`);
    return JSON.parse(Buffer.from(out.result.result).toString('utf8')) as T;
  }

  return {
    async wasMinted(tokenId) {
      const token = await view<unknown>('nft_token', { token_id: tokenId });
      if (token !== null) return true;
      return view<boolean>('is_burned', { token_id: tokenId });
    },

    async redeemsIn(txHash, signerId, ownerId = signerId) {
      const out = await rpc<TxStatus>('tx', { tx_hash: txHash, sender_account_id: signerId, wait_until: 'FINAL' });
      if (!out.result) return []; // unknown (yet) or not this signer's
      return parseRedeems(out.result, signerId, itemsContract, ownerId);
    },
  };
}

/** The parts of an RPC `tx` result we read. */
export interface TxStatus {
  readonly status: { SuccessValue?: string; Failure?: unknown } | string;
  readonly transaction: { readonly signer_id: string; readonly receiver_id: string };
  readonly receipts_outcome: readonly {
    readonly outcome: { readonly executor_id: string; readonly logs: readonly string[]; readonly status: { SuccessValue?: string; Failure?: unknown } | string };
  }[];
}

/** Pure: the `item_redeem` events of a successful transaction by `signerId`, from `itemsContract`. */
/**
 * `ownerId` is whose redeems count: the signer itself, or (a relayed meta-transaction, plan 15 §14) the player
 * whose session key signed the inner call while our relayer signed the transaction.
 */
export function parseRedeems(tx: TxStatus, signerId: string, itemsContract: string, ownerId: string = signerId): RedeemSeen[] {
  const ok = (s: TxStatus['status']) => typeof s === 'object' && s !== null && 'SuccessValue' in s;
  if (!ok(tx.status) || tx.transaction.signer_id !== signerId) return [];
  const seen: RedeemSeen[] = [];
  for (const r of tx.receipts_outcome) {
    if (r.outcome.executor_id !== itemsContract || !ok(r.outcome.status)) continue;
    for (const line of r.outcome.logs) {
      if (!line.startsWith('EVENT_JSON:')) continue;
      let event: { standard?: string; event?: string; data?: { token_id?: string; owner_id?: string; item?: ChainItem }[] };
      try {
        event = JSON.parse(line.slice('EVENT_JSON:'.length));
      } catch {
        continue;
      }
      if (event.standard !== 'farmverse_items' || event.event !== 'item_redeem') continue;
      for (const d of event.data ?? []) {
        if (d.token_id && d.owner_id === ownerId && d.item) seen.push({ tokenId: d.token_id, ownerId: d.owner_id, item: d.item });
      }
    }
  }
  return seen;
}
