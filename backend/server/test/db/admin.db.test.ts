import type { FastifyInstance } from 'fastify';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { systemClock } from '../../../game/src/core/clock';
import { memoryLogger } from '../../../game/src/core/logger';
import { resetCatalogueCache } from '../../../game/src/game/catalogue';
import { paths } from '../../../game/src/infra/paths';
import { OpsConfig } from '../../src/admin/ops';
import { buildApp } from '../../src/app';
import { Sessions, uidFor, type NonceStore } from '../../src/auth/sessions';
import { webMessenger, type Runtime } from '../../src/calls';
import { localChangeBus } from '../../src/infra/redis';
import { LiveHub } from '../../src/live';
import { prismaReader } from '../../src/runtime';
import { prismaStore } from '../../src/store/prismaStore';
import { resetDatabase, testPrisma } from './setup';

const prisma = testPrisma();
const log = memoryLogger();
const ADMIN = 'boss.testnet';
const PLAYER = 'farmer.testnet';
const UID = uidFor(PLAYER);
const NOW = Date.now();

let app: FastifyInstance;
let hub: LiveHub;
let ops: OpsConfig;
const store = prismaStore(prisma);

const nonces: NonceStore = { put: async () => {}, take: async () => true };

beforeAll(async () => {
  await resetDatabase(prisma);
  await prisma.$executeRawUnsafe('TRUNCATE admin_audit');
  resetCatalogueCache();
  const bus = localChangeBus();
  const sessions = new Sessions(prisma, nonces, { isFullAccessKey: async () => true }, {
    jwtSeed: Buffer.alloc(32, 7),
    recipient: 'play.farmverse.world',
    network: 'testnet',
    adminAccounts: [ADMIN],
    allowDevSignIn: true,
  });
  const rt: Runtime = {
    store,
    clock: systemClock,
    log,
    engine: {} as Runtime['engine'],
    wakes: { schedule: async () => {} },
    tasks: { schedule: async () => {} },
    messenger: webMessenger(log),
    deleteAccount: async () => {},
  };
  const reader = prismaReader(prisma);
  hub = new LiveHub(reader, (t) => sessions.verify(t), log);
  await hub.start(bus);
  ops = new OpsConfig(store, '0.0.0', log);
  await ops.start();
  app = await buildApp({
    rt,
    sessions,
    reader,
    hub,
    limiter: { hit: async () => true },
    callDeps: { clock: systemClock, log, minClientVersion: () => ops.current.minClientVersion, exposeIssues: true },
    allowedOrigins: [],
    minClientVersion: '0.0.0',
    environment: 'dev',
    ops,
    admin: {
      prisma,
      env: { store, clock: systemClock, log },
      sessions,
      ops,
      limiter: { hit: async () => true },
      itemsContract: 'items.test.testnet',
      adminAccounts: [ADMIN],
    },
  });

  // A player with a profile, an island and two coupon codes.
  await store.writeBatch([
    { path: paths.player(UID), data: { displayName: 'Daisy', avatarId: 'a1', shortId: '48211', lastSeenAt: NOW, createdAt: NOW, schemaVersion: 1, rev: 1 } },
    { path: paths.island(UID), data: { rev: 1, summary: { level: 7, netWorth: 1234, xp: 50 } } },
    { path: paths.couponCode('B3TR-AAAA-BBBB'), data: { code: 'B3TR-AAAA-BBBB', typeId: 'b3tr', taskId: 'd1', task: 'Harvest 20 crops', day: '', issuedAt: NOW, expiresAt: NOW + 86_400_000, amount: 10, uid: UID, unit: 'B3TR', redeemedAt: 0 } },
    { path: paths.couponCode('USDT-CCCC-DDDD'), data: { code: 'USDT-CCCC-DDDD', typeId: 'usdt', taskId: 'g2', task: 'Island level 10', day: '', issuedAt: NOW - 1000, expiresAt: NOW - 1, amount: 1, uid: UID, unit: 'USDT', redeemedAt: 0 } },
  ]);
});

afterAll(async () => {
  ops.stop();
  await hub.stop();
  await app.close();
  await prisma.$disconnect();
});

const dev = async (accountId: string) =>
  (await app.inject({ method: 'POST', url: '/v1/auth/dev', payload: { accountId } })).json() as { accessToken: string; refreshToken: string };

let adminToken = '';
let playerTokens: { accessToken: string; refreshToken: string };

const as = (token: string) => ({ authorization: `Bearer ${token}` });
const get = (url: string, token = adminToken) => app.inject({ method: 'GET', url: `/v1/admin${url}`, headers: as(token) });
const send = (method: 'POST' | 'PUT' | 'DELETE', url: string, payload: object, token = adminToken) =>
  app.inject({ method, url: `/v1/admin${url}`, payload, headers: as(token) });

describe('admin access', () => {
  it('lets in only wallets on the admin list', async () => {
    playerTokens = await dev(PLAYER);
    adminToken = (await dev(ADMIN)).accessToken;
    expect((await app.inject({ method: 'GET', url: '/v1/admin/me' })).statusCode).toBe(401);
    expect((await get('/me', playerTokens.accessToken)).statusCode).toBe(403);
    expect((await get('/me')).json()).toMatchObject({ accountId: ADMIN, boosts: expect.arrayContaining(['fert', 'shield']) });
  });
});

describe('players', () => {
  it('finds a player by name, wallet and player number', async () => {
    for (const q of ['dais', 'farmer', '48211']) {
      const { rows } = (await get(`/players?q=${q}`)).json();
      expect(rows.map((r: { uid: string }) => r.uid)).toEqual([UID]);
    }
    const one = (await get(`/players/${UID}`)).json();
    expect(one.player).toMatchObject({ accountId: PLAYER, name: 'Daisy', level: 7, netWorth: 1234 });
    expect(one.coupons).toHaveLength(0);
  });

  it('grants coins and gems as an inbox gift, goods to the island, boosts to the inventory', async () => {
    const res = await send('POST', `/players/${UID}/grant`, { coins: 500, gems: 20, goods: { wheat: 40 }, boosts: { fert: 2 }, reason: 'lost harvest, ticket 12' });
    expect(res.statusCode).toBe(200);
    const rewards = await prisma.document.findMany({ where: { collection: paths.rewards(UID) } });
    expect(rewards.map((r) => r.data)).toEqual([expect.objectContaining({ coins: 500, gems: 20, claimedAt: 0, reason: 'mail' })]);
    expect((await store.read(paths.grants(UID))).data?.['items']).toEqual([expect.objectContaining({ ops: [{ op: 'give', item: 'wheat', n: 40 }] })]);
    expect((await store.read(paths.inventory(UID))).data?.['boosts']).toEqual({ fert: 2 });
    expect(await prisma.document.count({ where: { collection: paths.notifications(UID) } })).toBe(1);
  });

  it('refuses a grant without a reason, and an unknown boost', async () => {
    expect((await send('POST', `/players/${UID}/grant`, { coins: 5 })).statusCode).toBe(400);
    expect((await send('POST', `/players/${UID}/grant`, { boosts: { nope: 1 }, reason: 'testing it' })).statusCode).toBe(400);
  });

  it('a ban ends sessions and blocks sign-in until unbanned; admins cannot be banned', async () => {
    expect((await send('POST', `/players/${UID}/ban`, { reason: 'botting' })).statusCode).toBe(200);
    expect((await app.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: playerTokens.refreshToken } })).statusCode).toBe(403);
    expect((await app.inject({ method: 'POST', url: '/v1/auth/dev', payload: { accountId: PLAYER } })).statusCode).toBe(403);
    expect((await get('/players?status=banned')).json().rows).toHaveLength(1);
    expect((await send('POST', `/players/${UID}/unban`, { reason: 'appeal accepted' })).statusCode).toBe(200);
    expect((await app.inject({ method: 'POST', url: '/v1/auth/dev', payload: { accountId: PLAYER } })).statusCode).toBe(200);
    expect((await send('POST', `/players/${uidFor(ADMIN)}/ban`, { reason: 'oops' })).statusCode).toBe(409);
  });
});

describe('coupons', () => {
  it('lists codes by status and redeems one once', async () => {
    expect((await get('/coupons/codes?status=open')).json().rows.map((r: { code: string }) => r.code)).toEqual(['B3TR-AAAA-BBBB']);
    expect((await get('/coupons/codes?status=expired')).json().rows.map((r: { code: string }) => r.code)).toEqual(['USDT-CCCC-DDDD']);
    expect((await get(`/coupons/codes?q=${PLAYER}`)).json().rows).toHaveLength(2);
    const ok = await send('POST', '/coupons/codes/b3tr-aaaa-bbbb/redeem', { reason: 'paid out on VeWorld' });
    expect(ok.statusCode).toBe(200);
    expect(ok.json().redeemedAt).toBeGreaterThan(0);
    expect((await send('POST', '/coupons/codes/B3TR-AAAA-BBBB/redeem', { reason: 'again' })).statusCode).toBe(409);
    expect((await send('POST', '/coupons/codes/NOPE-0000/redeem', { reason: 'unknown' })).statusCode).toBe(404);
  });

  it('publishes edited types and tasks, and refuses a task for a missing type', async () => {
    const live = (await get('/coupons')).json();
    expect(live.source).toBe('built-in');
    const tasks = { ...live.tasks, d1: { ...live.tasks.d1, amount: 25 } };
    const saved = await send('PUT', '/coupons', { types: live.types, tasks, reason: 'double the daily coupon' });
    expect(saved.statusCode).toBe(200);
    expect((await get('/coupons')).json()).toMatchObject({ source: 'published', tasks: { d1: { amount: 25 } } });
    const bad = await send('PUT', '/coupons', { types: live.types, tasks: { x: { ...live.tasks.d1, typeId: 'ghost' } }, reason: 'broken' });
    expect(bad.statusCode).toBe(400);
  });
});

describe('ops, news, stats, audit', () => {
  it('maintenance shows in /v1/config and refuses game calls for players only', async () => {
    expect((await send('PUT', '/ops', { maintenance: true, message: 'Back at 3pm', minClientVersion: '1.2.0', reason: 'db upgrade' })).statusCode).toBe(200);
    const cfg = (await app.inject({ method: 'GET', url: '/v1/config' })).json();
    expect(cfg.values).toMatchObject({ maintenance: true, maintenance_message: 'Back at 3pm', min_client_version: '1.2.0' });
    const player = (await dev(PLAYER)).accessToken;
    const refused = await app.inject({ method: 'POST', url: '/v1/call/account-export', payload: {}, headers: as(player) });
    expect(refused.json().error.code).toBe('MAINTENANCE');
    const admin = await app.inject({ method: 'POST', url: '/v1/call/account-export', payload: {}, headers: as(adminToken) });
    expect(admin.json().error?.code).not.toBe('MAINTENANCE');
    await send('PUT', '/ops', { maintenance: false, message: '', minClientVersion: '', reason: 'done' });
    expect((await app.inject({ method: 'GET', url: '/v1/config' })).json().values.maintenance).toBe(false);
  });

  it('publishes and deletes news', async () => {
    const made = await send('POST', '/news', { title: 'Harvest festival', body: 'Double coins all weekend.', reason: 'weekend event' });
    expect(made.statusCode).toBe(200);
    const id = made.json().id as string;
    expect((await get('/news')).json().items.map((n: { id: string }) => n.id)).toEqual([id]);
    expect((await send('DELETE', `/news/${id}`, { reason: 'event over' })).statusCode).toBe(200);
    expect((await get('/news')).json().items).toHaveLength(0);
  });

  it('counts players and coupons for the dashboard', async () => {
    const s = (await get('/stats')).json();
    expect(s.players).toMatchObject({ total: 2, active24h: 1 });
    expect(s.coupons).toMatchObject({ issued: 2, redeemed: 1, open: 0 });
    expect(s.days).toHaveLength(30);
  });

  it('wrote every change to the audit log', async () => {
    const actions = (await get('/audit')).json().entries.map((e: { action: string }) => e.action);
    expect(actions).toEqual(expect.arrayContaining(['player.grant', 'player.ban', 'player.unban', 'coupon.redeem', 'coupons.publish', 'ops.update', 'news.publish', 'news.delete']));
    const forPlayer = (await get(`/audit?targetType=player&targetId=${UID}`)).json().entries;
    expect(forPlayer.every((e: { actor: string }) => e.actor === ADMIN)).toBe(true);
  });
});
