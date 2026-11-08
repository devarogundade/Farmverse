/**
 * NEAR blocks in neardata's shape, carrying logs exactly as chain/contracts emit them (items:
 * nep171 + farmverse_items; market: farmverse_market). Shared by the parser and indexer tests.
 */
import type { NearBlock } from '../src/chain/parse';

// Contract ids no real deployment uses, so tests never touch a dev indexer's rows in the same database.
export const ITEMS = 'items.fv-fixture.testnet';
export const MARKET = 'market.fv-fixture.testnet';
export const FARM = 'farm.fv-fixture.testnet';
export const WATCHED = { items: ITEMS, market: MARKET };
/** Recent, so sales fall inside the price-history window whenever the tests run. */
export const T0 = Math.floor(Date.now() / 1000) * 1000 - 3_600_000;
export const FARM_UNIT = 10n ** 18n;
export const farm = (n: number) => (BigInt(Math.round(n * 100)) * FARM_UNIT / 100n).toString();

const log = (standard: string, version: string, event: string, data: unknown) =>
  `EVENT_JSON:${JSON.stringify({ standard, version, event, data: [data] })}`;

export interface Receipt {
  executor: string;
  logs: string[];
  status?: unknown;
  method?: string;
  args?: unknown;
  predecessor?: string;
}

// Receipt ids are unique on chain; keep fixture ids unique across test runs too.
const RUN = Math.random().toString(36).slice(2, 8);
let n = 0;
export function receipt(r: Receipt) {
  const id = `R${RUN}${++n}`;
  const actions = r.method
    ? [{ FunctionCall: { method_name: r.method, args: Buffer.from(JSON.stringify(r.args ?? {})).toString('base64'), gas: 1, deposit: '0' } }]
    : [];
  return {
    tx_hash: `T${n}`,
    execution_outcome: {
      id,
      block_hash: 'h',
      outcome: { executor_id: r.executor, logs: r.logs, status: r.status ?? { SuccessValue: '' }, gas_burnt: 1, tokens_burnt: '0', receipt_ids: [] },
    },
    receipt: { receipt_id: id, receiver_id: r.executor, predecessor_id: r.predecessor ?? 'someone.testnet', receipt: { Action: { actions, signer_id: 'x' } } },
  };
}

export function block(height: number, receipts: ReturnType<typeof receipt>[], shardId = 0): NearBlock {
  return {
    block: { header: { height, timestamp: 0, timestamp_nanosec: String(BigInt(T0 + height * 1000) * 1_000_000n) } },
    shards: [{ shard_id: shardId, receipt_execution_outcomes: receipts }],
  };
}

interface Item {
  kind: string;
  item_id: string;
  quantity: number;
  props?: Record<string, unknown>;
  minted_from?: string | null;
}

const itemJson = (i: Item, at: number) => ({
  kind: i.kind,
  item_id: i.item_id,
  quantity: i.quantity,
  props: JSON.stringify(i.props ?? {}),
  minted_from: i.minted_from ?? null,
  minted_at_ms: at,
});

/** `mint_with_voucher` (player) or `studio_mint`, as the items contract logs it. */
export function mint(tokenId: string, owner: string, item: Item, title: string, source: 'voucher' | 'studio' = 'voucher') {
  const it = itemJson(item, T0);
  const media = `items/${item.item_id}.png`;
  return receipt({
    executor: ITEMS,
    method: source === 'voucher' ? 'mint_with_voucher' : 'studio_mint',
    args:
      source === 'voucher'
        ? { voucher: { token_id: tokenId, receiver_id: owner, kind: item.kind, item_id: item.item_id, quantity: item.quantity, title, media }, signer: 'ed25519:x', signature: 'AA==' }
        : { token_id: tokenId, receiver_id: owner, kind: item.kind, item_id: item.item_id, quantity: item.quantity, props: it.props, title, media },
    logs: [
      log('nep171', '1.2.0', 'nft_mint', { owner_id: owner, token_ids: [tokenId], memo: source }),
      log('farmverse_items', '1.0.0', 'item_mint', { token_id: tokenId, owner_id: owner, source, item: it }),
    ],
  });
}

/** List (or relist) via nft_approve → market.nft_on_approve. */
export function list(tokenId: string, owner: string, price: string, approvalId = 0) {
  return receipt({
    executor: MARKET,
    method: 'nft_on_approve',
    predecessor: ITEMS,
    logs: [log('farmverse_market', '1.0.0', 'market_list', { nft_contract_id: ITEMS, token_id: tokenId, owner_id: owner, approval_id: approvalId, price, listed_at_ms: T0 })],
  });
}

export function delist(tokenId: string, reason: 'seller' | 'stale' = 'seller') {
  return receipt({
    executor: MARKET,
    method: 'delist',
    logs: [log('farmverse_market', '1.0.0', 'market_delist', { nft_contract_id: ITEMS, token_id: tokenId, reason })],
  });
}

/**
 * An NFT transfer. `byMarket` mirrors a market sale as testnet logged it: nft_transfer_payout called
 * by the market, with the sale memo and no authorized_id.
 */
export function transfer(tokenId: string, from: string, to: string, byMarket = false) {
  return receipt({
    executor: ITEMS,
    method: byMarket ? 'nft_transfer_payout' : 'nft_transfer',
    predecessor: byMarket ? MARKET : from,
    logs: [log('nep171', '1.2.0', 'nft_transfer', { old_owner_id: from, new_owner_id: to, token_ids: [tokenId], ...(byMarket ? { memo: 'farmverse market sale' } : {}) })],
  });
}

export function sale(tokenId: string, seller: string, buyer: string, price: string, fee: string) {
  return receipt({
    executor: MARKET,
    method: 'resolve_purchase',
    logs: [
      log('farmverse_market', '1.0.0', 'market_sale', { nft_contract_id: ITEMS, token_id: tokenId, seller_id: seller, buyer_id: buyer, price, fee, payout_valid: true }),
    ],
  });
}

export function redeem(tokenId: string, owner: string, item: Item) {
  return receipt({
    executor: ITEMS,
    method: 'redeem',
    logs: [
      log('nep171', '1.2.0', 'nft_burn', { owner_id: owner, token_ids: [tokenId], memo: 'redeem' }),
      log('farmverse_items', '1.0.0', 'item_redeem', { token_id: tokenId, owner_id: owner, item: itemJson(item, T0) }),
    ],
  });
}

export { log as eventLog };
