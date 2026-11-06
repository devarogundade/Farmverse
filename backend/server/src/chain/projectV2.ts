/**
 * Projections for the v2 contract events (plan 15 §5, §12): drops on the items contract, offers and
 * rentals on the market. `project` calls this first; it returns null for events it doesn't own.
 */
import type { Prisma } from '../generated/prisma/client';
import { formatFarm } from './items';
import type { MarketMessage } from './project';
import type { ChainEvent, Watched } from './parse';

type Tx = Prisma.TransactionClient;

const str = (v: unknown): string | null => (typeof v === 'string' && v.length > 0 ? v : null);
const uint = (v: unknown): string | null => {
  const s = typeof v === 'number' ? String(Math.trunc(v)) : typeof v === 'string' ? v : null;
  return s && /^\d{1,40}$/.test(s) ? s : null;
};
const ms = (v: unknown): Date | null => {
  const s = uint(v);
  return s ? new Date(Number(s)) : null;
};
const tokenHref = (tokenId: string) => `#/token/${encodeURIComponent(tokenId)}`;

const DROP_EVENTS = new Set(['drop_update', 'drop_sale']);
const MARKET_EVENTS = new Set([
  'offer_make', 'offer_cancel', 'offer_accept', 'offer_accept_failed', 'rent_list', 'rent_start', 'rent_withdraw', 'rent_end',
]);

async function titleOf(tx: Tx, contractId: string, tokenId: string): Promise<string> {
  const rows = await tx.$queryRaw<{ title: string }[]>`SELECT title FROM chain.tokens WHERE contract_id = ${contractId} AND token_id = ${tokenId}`;
  return rows[0]?.title ?? tokenId;
}

async function activity(tx: Tx, e: ChainEvent, contractId: string, tokenId: string, kind: string, account: string, counterparty: string | null, price: string | null) {
  await tx.$executeRaw`
    INSERT INTO chain.activity (id, contract_id, token_id, kind, account_id, counterparty, price, at, block_height)
    VALUES (${e.id}, ${contractId}, ${tokenId}, ${kind}, ${account}, ${counterparty}, ${price}::numeric, ${e.blockTime}, ${e.blockHeight})
    ON CONFLICT (id) DO NOTHING`;
}

export async function projectV2(tx: Tx, e: ChainEvent, w: Watched): Promise<MarketMessage[] | null> {
  const d = e.data;

  // ---------------------------------------------------------------- drops (items)
  if (e.contractId === w.items && e.standard === 'farmverse_items' && DROP_EVENTS.has(e.event)) {
    if (e.event === 'drop_update') {
      const drop = (d['drop'] ?? {}) as Record<string, unknown>;
      const id = str(drop['id']);
      const price = uint(drop['price']);
      if (!id || !price) return [];
      let props: unknown = {};
      try {
        props = JSON.parse(str(drop['props']) ?? '{}');
      } catch {
        props = {};
      }
      await tx.$executeRaw`
        INSERT INTO chain.drops (contract_id, drop_id, kind, item_id, quantity, props, title, media, price, cap, sold, per_wallet,
                                 starts_at, ends_at, paused, updated_at)
        VALUES (${e.contractId}, ${id}, ${str(drop['kind']) ?? 'pack'}, ${str(drop['item_id']) ?? id}, ${Number(uint(drop['quantity']) ?? '1')},
                ${JSON.stringify(props)}::jsonb, ${str(drop['title']) ?? id}, ${str(drop['media']) ?? ''}, ${price}::numeric,
                ${uint(drop['cap'])}::bigint, ${Number(uint(drop['sold']) ?? '0')}, ${uint(drop['per_wallet'])}::int,
                ${ms(drop['starts_at_ms']) ?? new Date(0)}, ${ms(drop['ends_at_ms'])}, ${drop['paused'] === true}, ${e.blockTime})
        ON CONFLICT (contract_id, drop_id) DO UPDATE
          SET price = EXCLUDED.price, cap = EXCLUDED.cap, sold = GREATEST(chain.drops.sold, EXCLUDED.sold),
              per_wallet = EXCLUDED.per_wallet, starts_at = EXCLUDED.starts_at, ends_at = EXCLUDED.ends_at,
              paused = EXCLUDED.paused, updated_at = EXCLUDED.updated_at`;
      return [{ type: 'drop', token_id: id }];
    }
    // drop_sale
    const id = str(d['drop_id']);
    const tokenId = str(d['token_id']);
    const buyer = str(d['buyer_id']);
    const edition = uint(d['edition']);
    if (!id || !tokenId || !buyer || !edition) return [];
    await tx.$executeRaw`
      UPDATE chain.drops SET sold = GREATEST(sold, ${Number(edition)}), updated_at = ${e.blockTime}
      WHERE contract_id = ${e.contractId} AND drop_id = ${id}`;
    await activity(tx, e, e.contractId, tokenId, 'bought', buyer, null, uint(d['price']));
    return [{ type: 'drop', token_id: id }];
  }

  if (e.contractId !== w.market || e.standard !== 'farmverse_market' || !MARKET_EVENTS.has(e.event)) return null;
  const out: MarketMessage[] = [];

  // ---------------------------------------------------------------- offers (market)
  if (e.event === 'offer_make') {
    const id = uint(d['id']);
    const nft = str(d['nft_contract_id']);
    const buyer = str(d['buyer_id']);
    const amount = uint(d['amount']);
    if (!id || !nft || !buyer || !amount) return out;
    const tokenId = str(d['token_id']);
    const itemId = str(d['item_id']);
    await tx.$executeRaw`
      INSERT INTO chain.offers (market_id, offer_id, contract_id, buyer_id, token_id, item_id, min_quantity, amount, made_at, expires_at, status)
      VALUES (${e.contractId}, ${id}::bigint, ${nft}, ${buyer}, ${tokenId}, ${itemId}, ${uint(d['min_quantity'])}::bigint, ${amount}::numeric,
              ${ms(d['made_at_ms']) ?? e.blockTime}, ${ms(d['expires_at_ms']) ?? e.blockTime}, 'open')
      ON CONFLICT (market_id, offer_id) DO NOTHING`;
    if (tokenId) {
      const owners = await tx.$queryRaw<{ owner_id: string; title: string }[]>`
        SELECT owner_id, title FROM chain.tokens WHERE contract_id = ${nft} AND token_id = ${tokenId} AND burned_at IS NULL`;
      const o = owners[0];
      if (o && o.owner_id !== buyer) {
        out.push({ type: 'note', to: o.owner_id, text: `${buyer} offered ${formatFarm(amount)} FARM for your ${o.title}`, icon: 'ResourceBar_Icon_Gold', href: tokenHref(tokenId) });
      }
      out.push({ type: 'offer', token_id: tokenId });
    } else if (itemId) {
      out.push({ type: 'offer', token_id: `#${itemId}` });
    }
    return out;
  }
  if (e.event === 'offer_cancel' || e.event === 'offer_accept_failed' || e.event === 'offer_accept') {
    const id = uint(d['id']);
    if (!id) return out;
    if (e.event === 'offer_accept_failed') {
      // the contract reopens it when the NFT didn't move; "gone"/"no match" leave it as it was
      if (str(d['reason']) === 'transfer') {
        await tx.$executeRaw`UPDATE chain.offers SET status = 'open', closed_at = NULL WHERE market_id = ${e.contractId} AND offer_id = ${id}::bigint`;
      }
      return out;
    }
    const status = e.event === 'offer_accept' ? 'accepted' : 'cancelled';
    const rows = await tx.$queryRaw<{ token_id: string | null }[]>`
      UPDATE chain.offers SET status = ${status}, closed_at = ${e.blockTime}
      WHERE market_id = ${e.contractId} AND offer_id = ${id}::bigint RETURNING token_id`;
    if (e.event === 'offer_accept') {
      const nft = str(d['nft_contract_id']);
      const tokenId = str(d['token_id']);
      const seller = str(d['seller_id']);
      const buyer = str(d['buyer_id']);
      const price = uint(d['price']);
      if (nft && tokenId && seller && buyer && price) {
        await tx.$executeRaw`
          INSERT INTO chain.sales (id, contract_id, token_id, item_id, seller_id, buyer_id, price, fee, sold_at)
          VALUES (${e.id}, ${nft}, ${tokenId},
                  COALESCE((SELECT item_id FROM chain.tokens WHERE contract_id = ${nft} AND token_id = ${tokenId}), ''),
                  ${seller}, ${buyer}, ${price}::numeric, ${uint(d['fee']) ?? '0'}::numeric, ${e.blockTime})
          ON CONFLICT (id) DO NOTHING`;
        await activity(tx, e, nft, tokenId, 'bought', buyer, seller, price);
        const title = await titleOf(tx, nft, tokenId);
        out.push({ type: 'sale', token_id: tokenId });
        out.push({ type: 'note', to: buyer, text: `${seller} accepted your offer: ${title} is yours`, icon: 'Icon_Gift', href: tokenHref(tokenId) });
      }
    }
    const tokenId = rows[0]?.token_id;
    if (tokenId) out.push({ type: 'offer', token_id: tokenId });
    return out;
  }

  // ---------------------------------------------------------------- rentals (market)
  const nft = str(d['nft_contract_id']);
  const tokenId = str(d['token_id']);
  if (!nft || !tokenId) return out;
  if (e.event === 'rent_list') {
    const lender = str(d['lender_id']);
    const perDay = uint(d['price_per_day']);
    if (!lender || !perDay) return out;
    const until = uint(d['rented_until_ms']);
    await tx.$executeRaw`
      INSERT INTO chain.rentals (contract_id, token_id, market_id, lender_id, price_per_day, max_days, renter_id, rented_until, earned, listed_at, updated_at)
      VALUES (${nft}, ${tokenId}, ${e.contractId}, ${lender}, ${perDay}::numeric, ${Number(uint(d['max_days']) ?? '1')}, ${str(d['renter_id'])},
              ${until && until !== '0' ? new Date(Number(until)) : null}, ${uint(d['earned']) ?? '0'}::numeric,
              ${ms(d['listed_at_ms']) ?? e.blockTime}, ${e.blockTime})
      ON CONFLICT (contract_id, token_id) DO UPDATE
        SET price_per_day = EXCLUDED.price_per_day, max_days = EXCLUDED.max_days, updated_at = EXCLUDED.updated_at`;
    out.push({ type: 'rental', token_id: tokenId });
  } else if (e.event === 'rent_start') {
    const renter = str(d['renter_id']);
    const paid = uint(d['paid']);
    const fee = uint(d['fee']) ?? '0';
    if (!renter || !paid) return out;
    const rows = await tx.$queryRaw<{ lender_id: string }[]>`
      UPDATE chain.rentals SET renter_id = ${renter}, rented_until = ${ms(d['rented_until_ms']) ?? e.blockTime},
             earned = earned + ${paid}::numeric - ${fee}::numeric, updated_at = ${e.blockTime}
      WHERE contract_id = ${nft} AND token_id = ${tokenId} RETURNING lender_id`;
    await activity(tx, e, nft, tokenId, 'rented', renter, rows[0]?.lender_id ?? null, paid);
    const lender = rows[0]?.lender_id;
    if (lender) {
      const title = await titleOf(tx, nft, tokenId);
      out.push({ type: 'note', to: lender, text: `${renter} hired ${title} for ${uint(d['days']) ?? '?'} days (${formatFarm(paid)} FARM)`, icon: 'ResourceBar_Icon_Gold', href: tokenHref(tokenId) });
    }
    out.push({ type: 'rental', token_id: tokenId });
  } else if (e.event === 'rent_withdraw') {
    await tx.$executeRaw`UPDATE chain.rentals SET earned = 0, updated_at = ${e.blockTime} WHERE contract_id = ${nft} AND token_id = ${tokenId}`;
    out.push({ type: 'rental', token_id: tokenId });
  } else if (e.event === 'rent_end') {
    await tx.$executeRaw`DELETE FROM chain.rentals WHERE contract_id = ${nft} AND token_id = ${tokenId}`;
    out.push({ type: 'rental', token_id: tokenId });
  }
  return out;
}
