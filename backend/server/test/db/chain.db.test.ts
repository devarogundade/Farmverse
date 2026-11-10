import websocket from '@fastify/websocket';
import Fastify, { type FastifyInstance } from 'fastify';
import type { AddressInfo } from 'node:net';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import WebSocket from 'ws';
import { memoryLogger } from '../../../game/src/core/logger';
import { ChainIndexer, blockRange } from '../../src/chain/indexer';
import type { BlockSource } from '../../src/chain/neardata';
import { eventsIn, type NearBlock } from '../../src/chain/parse';
import type { MarketMessage } from '../../src/chain/project';
import { localMarketBus } from '../../src/infra/redis';
import { memoryNoteMarks } from '../../src/market/marks';
import { MarketReader } from '../../src/market/read';
import { MarketHub, marketRoutes } from '../../src/market/routes';
import { block, delist, farm, ITEMS, list, MARKET, mint, redeem, sale, transfer, WATCHED } from '../chainFixtures';
import { testPrisma } from './setup';

const prisma = testPrisma();
const log = memoryLogger();

const wheat = { kind: 'good', item_id: 'wheat', quantity: 250, minted_from: 'ann.testnet' };
const tractor = { kind: 'tool', item_id: 'tractor_red', quantity: 1, minted_from: 'ann.testnet' };
const worker = { kind: 'worker', item_id: 'worker', quantity: 1, props: { outfit: { hair: 'Hairstyle_Male_005' }, speed: 3 } };

/** A market story across blocks 10–17 (16 skipped). */
const BLOCKS: Record<number, NearBlock | null> = {
  10: block(10, [
    mint('g-wheat', 'ann.testnet', wheat, 'Wheat ×250'),
    mint('s-w1', 'bob.testnet', worker, 'Tilly Furrow', 'studio'),
    mint('g-trac', 'ann.testnet', tractor, 'Red Tractor'),
    mint('g-trac2', 'ann.testnet', tractor, 'Red Tractor'),
    mint('s-pack', 'ann.testnet', { kind: 'pack', item_id: 'gems_100', quantity: 1 }, '100 Gems', 'studio'),
    mint('s-boost', 'ann.testnet', { kind: 'boost', item_id: 'speed_boost', quantity: 1 }, 'Speed boost', 'studio'),
  ]),
  11: block(11, [list('g-wheat', 'ann.testnet', farm(12.5)), list('g-trac', 'ann.testnet', farm(40)), list('s-w1', 'bob.testnet', farm(30))]),
  12: block(12, [list('g-wheat', 'ann.testnet', farm(10)), list('g-trac2', 'ann.testnet', farm(55))]),
  13: block(13, [transfer('g-trac', 'ann.testnet', 'cy.testnet', true), sale('g-trac', 'ann.testnet', 'cy.testnet', farm(40), farm(2))]),
  14: block(14, [transfer('s-w1', 'bob.testnet', 'dee.testnet')]),
  15: block(15, [redeem('g-trac', 'cy.testnet', tractor)]),
  16: null,
  17: block(17, [delist('s-w1', 'stale')]),
};

const source: BlockSource = {
  finalHeight: async () => 17,
  block: async (h) => BLOCKS[h] ?? null,
};

const published: MarketMessage[] = [];
const bus = localMarketBus();
let app: FastifyInstance;
let hub: MarketHub;
let indexer: ChainIndexer;

beforeAll(async () => {
  // Only this test's rows: a dev indexer may be running against the same database.
  await prisma.$executeRaw`DELETE FROM chain.cursors WHERE name = 'test'`;
  for (const table of ['events', 'tokens', 'listings', 'sales', 'activity']) {
    await prisma.$executeRawUnsafe(`DELETE FROM chain.${table} WHERE contract_id IN ($1, $2)`, ITEMS, MARKET);
  }
  indexer = new ChainIndexer({ prisma, source: blockRange(source, 2), watched: WATCHED, log, name: 'test', startHeight: 10, batch: 3, publish: (m) => (published.push(...m), bus.publish(m)) });

  hub = new MarketHub(log);
  await hub.start(bus);
  app = Fastify();
  await app.register(websocket);
  await app.register(marketRoutes({ reader: new MarketReader(prisma, ITEMS), marks: memoryNoteMarks(), limiter: { hit: async () => true }, hub, now: () => Date.now() + 60_000 }), {
    prefix: '/v1/market',
  });
  await app.listen({ host: '127.0.0.1', port: 0 });
});

afterAll(async () => {
  await hub.stop();
  await app.close();
  await prisma.$disconnect();
});

const get = async (url: string) => {
  const res = await app.inject({ method: 'GET', url: `/v1/market${url}` });
  return { status: res.statusCode, body: res.json() as any };
};

describe('chain indexer → marketplace API', () => {
  it('follows the chain to the final block in batches, skipping missing heights', async () => {
    const socket = new WebSocket(`ws://127.0.0.1:${(app.server.address() as AddressInfo).port}/v1/market/live?account=ann.testnet`);
    const received: MarketMessage[] = [];
    socket.on('message', (d) => received.push(JSON.parse(String(d))));
    await new Promise((r) => socket.once('open', r));

    let steps = 0;
    while ((await indexer.step()) > 0) steps++;
    expect(steps).toBe(3); // 10-12, 13-15, 16-17
    expect(await indexer.cursor()).toBe(17);

    await new Promise((r) => setTimeout(r, 50));
    socket.close();
    // Every page hears about listings and sales; only ann's socket gets ann's note.
    expect(received).toContainEqual({ type: 'sale', token_id: 'g-trac' });
    expect(received).toContainEqual(expect.objectContaining({ type: 'note', to: 'ann.testnet', text: 'cy.testnet bought your Red Tractor for 40 FARM' }));
    expect(received.some((m) => m.type === 'note' && m.to !== 'ann.testnet')).toBe(false);
    expect(published).toContainEqual(expect.objectContaining({ type: 'note', to: 'dee.testnet', text: 'bob.testnet sent you Tilly Furrow' }));
    expect(published).toContainEqual(expect.objectContaining({ type: 'note', to: 'bob.testnet', text: 'Tilly Furrow arrived in your items' }));
  });

  it('serves only live listings whose seller still owns the token', async () => {
    const { body } = await get('/listings?sort=price_asc');
    expect(body.total).toBe(2);
    expect(body.items.map((t: any) => [t.token_id, t.price])).toEqual([
      ['g-wheat', farm(10)],
      ['g-trac2', farm(55)],
    ]);
    expect(body.items[0]).toMatchObject({
      title: 'Wheat ×250',
      owner_id: 'ann.testnet',
      metadata: { type: 'good', item_id: 'wheat', quantity: 250, minted_from: 'ann.testnet' },
    });

    expect((await get('/listings?kind=tractor')).body.items.map((t: any) => t.token_id)).toEqual(['g-trac2']);
    expect((await get('/listings?q=whe')).body.total).toBe(1);
    expect((await get('/listings?kind=worker')).body.total).toBe(0);
    expect((await get('/listings?kind=dragon')).status).toBe(400);
  });

  it('pages with a cursor', async () => {
    const first = await get('/listings?sort=price_desc&limit=1');
    expect(first.body).toMatchObject({ total: 2, next_cursor: '1' });
    expect(first.body.items[0].token_id).toBe('g-trac2');
    const second = await get(`/listings?sort=price_desc&limit=1&cursor=${first.body.next_cursor}`);
    expect(second.body.items[0].token_id).toBe('g-wheat');
    expect(second.body.next_cursor).toBeNull();
  });

  it('lists what an account holds, listed or not, without burned or unrenderable tokens', async () => {
    const ann = await get('/owners/ann.testnet/tokens');
    expect(ann.body.items.map((t: any) => t.token_id).sort()).toEqual(['g-trac2', 'g-wheat', 's-pack']);
    // The game's NFT tab (?all=1) shows boosts too.
    const all = await get('/owners/ann.testnet/tokens?all=1');
    expect(all.body.items.map((t: any) => t.token_id).sort()).toEqual(['g-trac2', 'g-wheat', 's-boost', 's-pack']);
    const dee = await get('/owners/dee.testnet/tokens');
    expect(dee.body.items).toHaveLength(1);
    expect(dee.body.items[0]).toMatchObject({
      token_id: 's-w1',
      price: null,
      metadata: { type: 'worker', outfit: { hair: 'Hairstyle_Male_005' }, props: { speed: 3 } },
    });
    expect((await get('/owners/cy.testnet/tokens')).body.total).toBe(0);
  });

  it('shows a token with its history, floor and price trend', async () => {
    const { body } = await get('/tokens/g-wheat');
    expect(body.token.events.map((e: any) => [e.kind, e.price])).toEqual([
      ['listed', farm(10)],
      ['listed', farm(12.5)],
      ['minted', null],
    ]);
    expect(body.floor).toBe(farm(10));
    expect(body.related.map((t: any) => t.token_id)).toEqual(['g-trac2']);

    const trac = await get('/tokens/g-trac2');
    expect(trac.body.token.price_history).toEqual([40]);
    expect(trac.body.floor).toBe(farm(55));

    expect((await get('/tokens/g-trac')).status).toBe(404); // redeemed
    expect((await get('/tokens/s-pack')).body.token.metadata).toMatchObject({ type: 'pack' }); // flat art
    expect((await get('/tokens/s-boost')).status).toBe(404); // no renderer
    expect((await get('/tokens?ids=g-wheat,s-w1,g-trac,nope')).body.map((t: any) => t.token_id).sort()).toEqual(['g-wheat', 's-w1']);
  });

  it('notifies sellers and receivers, and remembers what was read', async () => {
    const before = await get('/accounts/ann.testnet/notifications');
    expect(before.body).toEqual([expect.objectContaining({ text: 'cy.testnet bought your Red Tractor for 40 FARM', href: '#/token/g-trac', read: false })]);
    expect((await app.inject({ method: 'POST', url: '/v1/market/accounts/ann.testnet/notifications/read' })).statusCode).toBe(200);
    expect((await get('/accounts/ann.testnet/notifications')).body[0].read).toBe(true);
    expect((await get('/accounts/dee.testnet/notifications')).body[0].text).toBe('bob.testnet sent you Tilly Furrow');
  });

  it('records a market sale as bought, not sent, and a gift as sent', async () => {
    const kinds = async (tokenId: string) =>
      (await prisma.$queryRaw<{ kind: string }[]>`SELECT kind FROM chain.activity WHERE contract_id = ${ITEMS} AND token_id = ${tokenId} ORDER BY block_height, id`).map((r) => r.kind);
    expect(await kinds('g-trac')).toEqual(['minted', 'listed', 'bought', 'redeemed']);
    expect(await kinds('s-w1')).toEqual(['minted', 'listed', 'sent']);
    // The buyer hears nothing about a "send"; the seller hears about the sale.
    expect(published.some((m) => m.type === 'note' && m.to === 'cy.testnet')).toBe(false);
  });

  it('applies a repeated batch once, and refuses a cursor someone else moved', async () => {
    const events = BLOCKS[13] ? eventsIn(BLOCKS[13], WATCHED) : [];
    expect(await indexer.apply(events, 17, 17)).toEqual([]);
    expect(Number((await prisma.$queryRaw<{ n: bigint }[]>`SELECT count(*) AS n FROM chain.sales WHERE contract_id = ${ITEMS}`)[0]!.n)).toBe(1);
    await expect(indexer.apply([], 12, 13)).rejects.toThrow(/moved underneath/);
  });

  it('reads offers; making one is a wallet transaction, not a server route', async () => {
    expect((await get('/tokens/g-wheat/offers')).body).toEqual([]);
    const res = await app.inject({ method: 'POST', url: '/v1/market/tokens/g-wheat/offers', payload: { amount: '1', days: 3 } });
    expect(res.statusCode).toBe(404);
  });
});
