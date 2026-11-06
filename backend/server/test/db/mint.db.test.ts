import { createPublicKey, verify } from 'node:crypto';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { runCallable, type CallableDeps } from '../../../functions/src/core/callable';
import { memoryLogger } from '../../../functions/src/core/logger';
import { httpEngine } from '../../../functions/src/infra/islandEngine';
import { paths } from '../../../functions/src/infra/paths';
import { base58Decode } from '../../src/auth/nep413';
import { uidFor } from '../../src/auth/sessions';
import { callDefinitions, webMessenger, type Runtime } from '../../src/calls';
import { expireMint, EXPIRY_GRACE_MS, type MintDoc } from '../../src/mint/mint';
import type { ChainItem, ChainReader, RedeemSeen } from '../../src/mint/nearChain';
import { VoucherSigner, voucherBytes } from '../../src/mint/voucher';
import { prismaStore } from '../../src/store/prismaStore';
import { resetDatabase, testPrisma } from './setup';

/**
 * Mint and redeem end to end: Postgres store, the real C# island engine (ISLAND_ENGINE_URL,
 * default localhost:8080), and a fake NEAR chain the tests control.
 */
const ENGINE = process.env['ISLAND_ENGINE_URL'] ?? 'http://localhost:8080';
const prisma = testPrisma();
const log = memoryLogger();
const ACCOUNT = 'farmer.testnet';
const UID = uidFor(ACCOUNT);
const T0 = Date.UTC(2027, 0, 10, 12, 0, 0);
const meta = { clientVersion: '1.0.0', platform: 'webgl', locale: 'en' };

let nowMs = T0;
const clock = { nowMs: () => nowMs };
const minted = new Set<string>();
const redeemed = new Map<string, RedeemSeen[]>();
const chain: ChainReader = {
  wasMinted: async (tokenId) => minted.has(tokenId),
  redeemsIn: async (txHash, signer) => (redeemed.get(txHash) ?? []).filter((r) => r.ownerId === signer),
};
const expiries: { tokenId: string; atMs: number }[] = [];
const signer = new VoucherSigner(Buffer.alloc(32, 5));
const store = prismaStore(prisma);
const rt: Runtime = {
  store,
  clock,
  log,
  engine: httpEngine(ENGINE),
  wakes: { schedule: async () => {} },
  tasks: { schedule: async () => {} },
  messenger: webMessenger(log),
  deleteAccount: async () => {},
  chain: {
    reader: chain,
    signer,
    itemsContract: 'items.farmverse.testnet',
    limits: { dailyValueCap: 1_000, minAccountHours: 0 },
    scheduleExpiry: async (_uid, tokenId, atMs) => void expiries.push({ tokenId, atMs }),
    accountOf: async (uid) => (uid === UID ? ACCOUNT : null),
  },
};
const calls = callDefinitions(rt);
const deps: CallableDeps = { clock, log, minClientVersion: () => '0.0.0', exposeIssues: true };

async function call(name: string, data: object) {
  const out = await runCallable(calls.get(name)!, { meta, ...data }, { uid: UID, isAdmin: false, requestId: 't' }, deps);
  return out as { ok: boolean; data?: Record<string, unknown>; error?: { code: string; params: Record<string, string> } };
}

type Economy = { coins?: number; departments?: { uid: number; stock: { item: string; n: number }[] }[] };
const island = async () => (await store.read(paths.island(UID))).data as { state: string; audit?: unknown; rev: number };
const wheatIn = (state: string) =>
  ((JSON.parse(state) as { economy?: Economy }).economy?.departments ?? []).flatMap((d) => d.stock).filter((s) => s.item === 'wheat').reduce((a, s) => a + s.n, 0);
const heldIn = (state: string) => (JSON.parse(state) as { held?: { id: string; level: number }[] }).held ?? [];
const buildingsIn = (state: string) => (JSON.parse(state) as { buildings: { uid: number; id: string }[] }).buildings;

async function grant(id: string, ops: object[]) {
  await store.runTransaction(async (tx) => {
    const g = await tx.get(paths.grants(UID));
    const items = (g.data?.['items'] as object[] | undefined) ?? [];
    tx.set(paths.grants(UID), { items: [...items, { id, ops, at: nowMs }], updatedAt: nowMs });
  });
}

beforeAll(async () => {
  const health = await fetch(`${ENGINE}/healthz`).catch(() => undefined);
  if (!health?.ok) throw new Error(`island engine not reachable at ${ENGINE}: run tools/dotnet/Farmverse.IslandEngine`);
  await resetDatabase(prisma);
  expect((await call('player-create', { displayName: 'mintfarm', avatarId: 'avatar_default' })).ok).toBe(true);
  const save = {
    rulesVersion: 2,
    simAt: T0 / 1000,
    seed: 42,
    buildings: [
      { id: 'farmhouse', uid: 1, x: 150, z: 150, w: 4, d: 4 },
      { id: 'field', uid: 2, x: 156, z: 150, w: 3, d: 3 },
      { id: 'chicken_coop', uid: 3, x: 150, z: 156, w: 3, d: 3, price: 120 },
    ],
    held: [],
  };
  const adopted = await call('island-sync', { adopt: JSON.stringify(save) });
  expect(adopted.ok).toBe(true);
});

afterAll(() => prisma.$disconnect());

describe('grants', () => {
  it('are applied on the next sync, which tells the client to load the island', async () => {
    await grant('seed_wheat', [{ op: 'give', item: 'wheat', n: 300 }]);
    nowMs += 60_000;
    const synced = await call('island-sync', {});
    expect(synced.data?.['serverChanged']).toBe(true);
    expect(wheatIn(synced.data!['state'] as string)).toBe(300);
    const again = await call('island-sync', {});
    expect(again.data?.['serverChanged']).toBeUndefined();
  });
});

describe('chain-mint', () => {
  let firstToken = '';

  it('takes goods off the island and signs a voucher the contract will accept', async () => {
    const out = await call('chain-mint', { kind: 'good', item: 'wheat', quantity: 100, key: 'mint-wheat-1' });
    expect(out.ok).toBe(true);
    const d = out.data!;
    const voucher = d['voucher'] as Parameters<typeof voucherBytes>[0];
    firstToken = d['tokenId'] as string;
    expect(voucher).toMatchObject({ contract_id: 'items.farmverse.testnet', receiver_id: ACCOUNT, kind: 'good', item_id: 'wheat', quantity: 100, title: 'Wheat ×100' });
    const raw = base58Decode((d['signer'] as string).slice('ed25519:'.length));
    const key = createPublicKey({ key: Buffer.concat([Buffer.from('302a300506032b6570032100', 'hex'), raw]), format: 'der', type: 'spki' });
    expect(verify(null, voucherBytes(voucher), key, Buffer.from(d['signature'] as string, 'base64'))).toBe(true);
    expect(wheatIn(d['state'] as string)).toBe(200);
    expect(wheatIn((await island()).state)).toBe(200);
    expect(expiries.at(-1)).toEqual({ tokenId: firstToken, atMs: voucher.expires_at_ms + EXPIRY_GRACE_MS });
  });

  it('a retry with the same key returns the same voucher and takes nothing more', async () => {
    const out = await call('chain-mint', { kind: 'good', item: 'wheat', quantity: 100, key: 'mint-wheat-1' });
    expect(out.data?.['tokenId']).toBe(firstToken);
    expect(wheatIn((await island()).state)).toBe(200);
  });

  it('refuses more than is in store, saying how much there is', async () => {
    const out = await call('chain-mint', { kind: 'good', item: 'wheat', quantity: 999, key: 'mint-wheat-2' });
    expect(out.error).toEqual({ code: 'MINT_NOT_ENOUGH', params: { have: '200' } });
  });

  it('enforces the daily value cap', async () => {
    // 100 wheat (5 coins each) used 500 of 1000; 120 more would be 600
    const out = await call('chain-mint', { kind: 'good', item: 'wheat', quantity: 120, key: 'mint-wheat-3' });
    expect(out.error).toEqual({ code: 'MINT_LIMIT', params: { limit: '1000', left: '500' } });
    expect(wheatIn((await island()).state)).toBe(200);
  });

  it('a field is land, not a building', async () => {
    const out = await call('chain-mint', { kind: 'building', buildingUid: 2, key: 'mint-field-1' });
    expect(out.error?.code).toBe('MINT_NOT_ALLOWED');
  });

  it('a building with goods in it must be emptied first (the hens laid eggs)', async () => {
    const out = await call('chain-mint', { kind: 'building', buildingUid: 3, key: 'mint-coop-1' });
    expect(out.error).toEqual({ code: 'MINT_BUILDING_BUSY', params: { reason: 'not_empty' } });
    expect(buildingsIn((await island()).state).some((b) => b.uid === 3)).toBe(true);
  });

  it('picks up an empty field as land, with its level and price in the token', async () => {
    const out = await call('chain-mint', { kind: 'land', buildingUid: 2, key: 'mint-field-ok' });
    expect(out.ok).toBe(true);
    expect(out.data!['voucher']).toMatchObject({ kind: 'land', item_id: 'field', quantity: 1, props: '{"level":1,"price":0}' });
    expect(buildingsIn(out.data!['state'] as string).some((b) => b.uid === 2)).toBe(false);
  });

  it('the farmhouse stays', async () => {
    const out = await call('chain-mint', { kind: 'building', buildingUid: 1, key: 'mint-house-1' });
    expect(out.error).toEqual({ code: 'MINT_BUILDING_BUSY', params: { reason: 'core_building' } });
  });

  it('boosts come out of the inventory', async () => {
    await store.writeBatch([{ path: paths.inventory(UID), data: { boosts: { fert: 3 }, activeUntil: {}, updatedAt: nowMs } }]);
    const out = await call('chain-mint', { kind: 'boost', item: 'fert', quantity: 2, key: 'mint-fert-1' });
    expect(out.ok).toBe(true);
    expect(out.data!['state']).toBeUndefined();
    expect(((await store.read(paths.inventory(UID))).data!['boosts'] as Record<string, number>)['fert']).toBe(1);
  });

  it('an unused voucher gives the goods back after it expires', async () => {
    const m = (await store.read(paths.mint(UID, firstToken))).data as unknown as MintDoc;
    const md = (x: Runtime) => ({ env: { store: x.store, clock: x.clock, log }, island: { env: { store: x.store, clock: x.clock, log }, engine: x.engine, wakes: x.wakes }, chain, signer, itemsContract: 'items.farmverse.testnet', limits: x.chain!.limits, scheduleExpiry: x.chain!.scheduleExpiry });
    expect(await expireMint(md(rt), UID, firstToken, log)).toBe('early');
    nowMs = m.voucher.expires_at_ms + EXPIRY_GRACE_MS + 1;
    expect(await expireMint(md(rt), UID, firstToken, log)).toBe('refunded');
    expect(await expireMint(md(rt), UID, firstToken, log)).toBe('settled');
    const synced = await call('island-sync', {});
    expect(synced.data?.['serverChanged']).toBe(true);
    expect(wheatIn(synced.data!['state'] as string)).toBe(300);
  });

  it('a voucher that was minted is settled, not refunded', async () => {
    const coop = (await call('chain-mint', { kind: 'land', buildingUid: 2, key: 'mint-field-ok' })).data!['tokenId'] as string;
    minted.add(coop);
    const md = { env: { store, clock, log }, island: { env: { store, clock, log }, engine: rt.engine, wakes: rt.wakes }, chain, signer, itemsContract: 'items.farmverse.testnet', limits: rt.chain!.limits, scheduleExpiry: rt.chain!.scheduleExpiry };
    expect(await expireMint(md, UID, coop, log)).toBe('minted');
    expect(heldIn((await island()).state)).toEqual([]);
  });
});

describe('chain-redeem', () => {
  const TX = '7AuQS7RymNrG5yfz7mHh7HMbt5sM7gHmZfT8QfHYz1Lb';
  const coop: ChainItem = { kind: 'building', item_id: 'chicken_coop', quantity: 1, props: '{"level":3,"price":120}' };

  it('credits a redeemed building once, held with its level', async () => {
    redeemed.set(TX, [{ tokenId: 'g-redeemed-coop', ownerId: ACCOUNT, item: coop }]);
    const out = await call('chain-redeem', { txHash: TX });
    expect(out.data).toMatchObject({ tokenId: 'g-redeemed-coop', kind: 'building', item: 'chicken_coop', quantity: 1, fresh: true });
    expect(heldIn(out.data!['state'] as string)).toEqual([{ id: 'chicken_coop', level: 3, price: 120 }]);
    const again = await call('chain-redeem', { txHash: TX });
    expect(again.data?.['fresh']).toBe(false);
    expect(heldIn((await island()).state)).toHaveLength(1);
  });

  it('a gem pack pays its gems into the wallet', async () => {
    const tx = '3AuQS7RymNrG5yfz7mHh7HMbt5sM7gHmZfT8QfHYz1Lb';
    redeemed.set(tx, [{ tokenId: 's-gems-1', ownerId: ACCOUNT, item: { kind: 'pack', item_id: 'gems_500', quantity: 1, props: '{}' } }]);
    const before = ((await store.read(paths.wallet(UID))).data?.['gems'] as number | undefined) ?? 0;
    expect((await call('chain-redeem', { txHash: tx })).data?.['fresh']).toBe(true);
    expect((await store.read(paths.wallet(UID))).data!['gems']).toBe(before + 500);
  });

  it('refuses a transaction that is not this wallet’s redeem', async () => {
    const out = await call('chain-redeem', { txHash: '9AuQS7RymNrG5yfz7mHh7HMbt5sM7gHmZfT8QfHYz1Lb' });
    expect(out.error?.code).toBe('REDEEM_NOT_FOUND');
  });
});

describe('shadow-mode guard', () => {
  it('a forged upload locks minting (play goes on)', async () => {
    const forged = JSON.parse((await island()).state) as { economy: Economy };
    forged.economy.coins = 50_000_000;
    nowMs += 60_000;
    const up = await call('island-sync', { adopt: JSON.stringify(forged), mirror: true });
    expect(up.ok).toBe(true);
    expect((await island()).audit).toMatchObject({ reason: 'worth_jump' });
    const out = await call('chain-mint', { kind: 'good', item: 'wheat', quantity: 1, key: 'mint-after-forgery' });
    expect(out.error?.code).toBe('MINT_LOCKED');
  });
});
