import type { Prisma } from '../generated/prisma/client';
import { fallbackTitle, formatFarm, nftTypeOf, parseProps } from './items';
import type { ChainEvent, Watched } from './parse';

type Tx = Prisma.TransactionClient;

/**
 * What the marketplace's live socket sends (web/marketplace/src/state/store.ts `MarketMessage`):
 * each one tells open pages which queries went stale; notes go to one account.
 */
export type MarketMessage =
  | { type: 'listing' | 'delist' | 'sale' | 'redeem'; token_id: string }
  | { type: 'note'; to: string; text: string; icon?: string; href?: string };

const str = (v: unknown): string | null => (typeof v === 'string' && v.length > 0 ? v : null);
const strs = (v: unknown): string[] => (Array.isArray(v) ? v.filter((x): x is string => typeof x === 'string') : []);
/** U128 and u64 travel as strings or numbers; keep only whole non-negative values. */
const uint = (v: unknown): string | null => {
  const s = typeof v === 'number' ? String(Math.trunc(v)) : typeof v === 'string' ? v : null;
  return s && /^\d{1,40}$/.test(s) ? s : null;
};
const tokenHref = (tokenId: string) => `#/token/${encodeURIComponent(tokenId)}`;

async function titleOf(tx: Tx, contractId: string, tokenId: string): Promise<string> {
  const rows = await tx.$queryRaw<{ title: string }[]>`SELECT title FROM chain.tokens WHERE contract_id = ${contractId} AND token_id = ${tokenId}`;
  return rows[0]?.title ?? tokenId;
}

async function activity(
  tx: Tx,
  id: string,
  e: ChainEvent,
  contractId: string,
  tokenId: string,
  kind: string,
  account: string,
  counterparty: string | null = null,
  price: string | null = null,
): Promise<void> {
  await tx.$executeRaw`
    INSERT INTO chain.activity (id, contract_id, token_id, kind, account_id, counterparty, price, at, block_height)
    VALUES (${id}, ${contractId}, ${tokenId}, ${kind}, ${account}, ${counterparty}, ${price}::numeric, ${e.blockTime}, ${e.blockHeight})
    ON CONFLICT (id) DO NOTHING`;
}

/** Removes a listing; returns its seller if there was one. */
async function unlist(tx: Tx, contractId: string, tokenId: string): Promise<string | null> {
  const rows = await tx.$queryRaw<{ seller_id: string }[]>`
    DELETE FROM chain.listings WHERE contract_id = ${contractId} AND token_id = ${tokenId} RETURNING seller_id`;
  return rows[0]?.seller_id ?? null;
}

/**
 * Applies one newly seen event to the marketplace tables, inside the indexer's transaction.
 * Called once per event id (the caller only projects events whose insert was new), in chain order.
 */
export async function project(tx: Tx, e: ChainEvent, w: Watched): Promise<MarketMessage[]> {
  const d = e.data;
  const out: MarketMessage[] = [];
  const many = (i: number, n: number) => (n > 1 ? `${e.id}#${i}` : e.id);

  if (e.contractId === w.items && e.standard === 'farmverse_items') {
    const tokenId = str(d['token_id']);
    const owner = str(d['owner_id']);
    const item = (d['item'] ?? {}) as Record<string, unknown>;
    if (!tokenId || !owner) return out;

    if (e.event === 'item_mint') {
      const kind = str(item['kind']) ?? 'good';
      const itemId = str(item['item_id']) ?? 'unknown';
      const quantity = Number(uint(item['quantity']) ?? '1');
      const mintedAtMs = Number(uint(item['minted_at_ms']) ?? e.blockTime.getTime());
      const title = str(d['title']) ?? fallbackTitle(itemId, quantity);
      await tx.$executeRaw`
        INSERT INTO chain.tokens (contract_id, token_id, owner_id, kind, nft_type, item_id, quantity, props, title, media, source,
                                  minted_from, minted_at, updated_at)
        VALUES (${e.contractId}, ${tokenId}, ${owner}, ${kind}, ${nftTypeOf(kind, itemId)}, ${itemId}, ${quantity},
                ${JSON.stringify(parseProps(item['props']))}::jsonb, ${title}, ${str(d['media'])}, ${str(d['source']) ?? 'voucher'},
                ${str(item['minted_from'])}, ${new Date(mintedAtMs)}, ${e.blockTime})
        ON CONFLICT (contract_id, token_id) DO NOTHING`;
      await activity(tx, e.id, e, e.contractId, tokenId, 'minted', owner);
      if (str(d['source']) === 'studio') out.push({ type: 'note', to: owner, text: `${title} arrived in your items`, icon: 'Icon_Gift', href: tokenHref(tokenId) });
    } else if (e.event === 'item_redeem') {
      await tx.$executeRaw`
        UPDATE chain.tokens SET burned_at = COALESCE(burned_at, ${e.blockTime}), updated_at = ${e.blockTime}
        WHERE contract_id = ${e.contractId} AND token_id = ${tokenId}`;
      await unlist(tx, e.contractId, tokenId);
      await activity(tx, e.id, e, e.contractId, tokenId, 'redeemed', owner);
      out.push({ type: 'redeem', token_id: tokenId });
    }
    return out;
  }

  if (e.contractId === w.items && e.standard === 'nep171') {
    const ids = strs(d['token_ids']);
    if (e.event === 'nft_transfer') {
      const from = str(d['old_owner_id']);
      const to = str(d['new_owner_id']);
      if (!from || !to) return out;
      const byMarket = str(d['authorized_id']) === w.market;
      for (const [i, tokenId] of ids.entries()) {
        await tx.$executeRaw`
          UPDATE chain.tokens SET owner_id = ${to}, updated_at = ${e.blockTime}
          WHERE contract_id = ${e.contractId} AND token_id = ${tokenId}`;
        // A transfer clears every approval, so any listing for the token can no longer sell.
        if (await unlist(tx, e.contractId, tokenId)) out.push({ type: 'delist', token_id: tokenId });
        // Market sales are recorded from market_sale (with the price); everything else is a send.
        if (!byMarket) {
          await activity(tx, many(i, ids.length), e, e.contractId, tokenId, 'sent', from, to);
          const title = await titleOf(tx, e.contractId, tokenId);
          out.push({ type: 'note', to, text: `${from} sent you ${title}`, icon: 'Icon_Gift', href: tokenHref(tokenId) });
        }
      }
    } else if (e.event === 'nft_burn') {
      for (const tokenId of ids) {
        await tx.$executeRaw`
          UPDATE chain.tokens SET burned_at = COALESCE(burned_at, ${e.blockTime}), updated_at = ${e.blockTime}
          WHERE contract_id = ${e.contractId} AND token_id = ${tokenId}`;
        if (await unlist(tx, e.contractId, tokenId)) out.push({ type: 'delist', token_id: tokenId });
      }
    }
    return out;
  }

  if (e.contractId === w.market && e.standard === 'farmverse_market') {
    const nft = str(d['nft_contract_id']);
    const tokenId = str(d['token_id']);
    if (!nft || !tokenId) return out;

    if (e.event === 'market_list') {
      const seller = str(d['owner_id']);
      const price = uint(d['price']);
      const approval = uint(d['approval_id']) ?? '0';
      const listedAt = new Date(Number(uint(d['listed_at_ms']) ?? e.blockTime.getTime()));
      if (!seller || !price) return out;
      await tx.$executeRaw`
        INSERT INTO chain.listings (contract_id, token_id, seller_id, approval_id, price, listed_at, market_id)
        VALUES (${nft}, ${tokenId}, ${seller}, ${approval}::bigint, ${price}::numeric, ${listedAt}, ${e.contractId})
        ON CONFLICT (contract_id, token_id) DO UPDATE
          SET seller_id = EXCLUDED.seller_id, approval_id = EXCLUDED.approval_id, price = EXCLUDED.price,
              listed_at = EXCLUDED.listed_at, market_id = EXCLUDED.market_id`;
      await activity(tx, e.id, e, nft, tokenId, 'listed', seller, null, price);
      out.push({ type: 'listing', token_id: tokenId });
    } else if (e.event === 'market_delist' || e.event === 'market_sale_failed') {
      // A failed sale took the listing off the book on chain before it failed.
      const seller = await unlist(tx, nft, tokenId);
      if (seller) {
        await activity(tx, e.id, e, nft, tokenId, 'delisted', seller);
        out.push({ type: 'delist', token_id: tokenId });
      }
    } else if (e.event === 'market_sale') {
      const seller = str(d['seller_id']);
      const buyer = str(d['buyer_id']);
      const price = uint(d['price']);
      const fee = uint(d['fee']) ?? '0';
      if (!seller || !buyer || !price) return out;
      await tx.$executeRaw`
        INSERT INTO chain.sales (id, contract_id, token_id, item_id, seller_id, buyer_id, price, fee, sold_at)
        VALUES (${e.id}, ${nft}, ${tokenId},
                COALESCE((SELECT item_id FROM chain.tokens WHERE contract_id = ${nft} AND token_id = ${tokenId}), ''),
                ${seller}, ${buyer}, ${price}::numeric, ${fee}::numeric, ${e.blockTime})
        ON CONFLICT (id) DO NOTHING`;
      await unlist(tx, nft, tokenId);
      await activity(tx, e.id, e, nft, tokenId, 'bought', buyer, seller, price);
      const title = await titleOf(tx, nft, tokenId);
      out.push({ type: 'sale', token_id: tokenId });
      out.push({ type: 'note', to: seller, text: `${buyer} bought your ${title} for ${formatFarm(price)} FARM`, icon: 'ResourceBar_Icon_Gold', href: tokenHref(tokenId) });
    }
  }
  return out;
}
