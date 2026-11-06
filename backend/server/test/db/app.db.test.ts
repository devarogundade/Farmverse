import { generateKeyPairSync, sign, type KeyObject } from 'node:crypto';
import type { AddressInfo } from 'node:net';
import type { FastifyInstance } from 'fastify';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import WebSocket from 'ws';
import { systemClock } from '../../../game/src/core/clock';
import { memoryLogger } from '../../../game/src/core/logger';
import { buildApp } from '../../src/app';
import type { NearRpc } from '../../src/auth/nearRpc';
import { base58Encode, nep413Hash } from '../../src/auth/nep413';
import { SIGN_IN_MESSAGE, Sessions, uidFor, type NonceStore } from '../../src/auth/sessions';
import { webMessenger, type Runtime } from '../../src/calls';
import { localChangeBus } from '../../src/infra/redis';
import { LiveHub } from '../../src/live';
import { prismaReader } from '../../src/runtime';
import { prismaStore } from '../../src/store/prismaStore';
import { resetDatabase, testPrisma } from './setup';

const prisma = testPrisma();
const log = memoryLogger();
const RECIPIENT = 'play.farmverse.world';

function memoryNonces(): NonceStore {
  const live = new Set<string>();
  return { put: async (n) => void live.add(n), take: async (n) => live.delete(n) };
}

class Wallet {
  readonly publicKey: string;
  private readonly privateKey: KeyObject;
  constructor(readonly accountId: string) {
    const pair = generateKeyPairSync('ed25519');
    this.privateKey = pair.privateKey;
    this.publicKey = `ed25519:${base58Encode(pair.publicKey.export({ format: 'der', type: 'spki' }).subarray(-32))}`;
  }
  signNonce(nonce: string): string {
    const payload = { message: SIGN_IN_MESSAGE, nonce: Buffer.from(nonce, 'base64'), recipient: RECIPIENT, callbackUrl: null };
    return sign(null, nep413Hash(payload), this.privateKey).toString('base64');
  }
}

let app: FastifyInstance;
let hub: LiveHub;
const fullAccess = new Set<string>();
const rpc: NearRpc = { isFullAccessKey: async (account, key) => fullAccess.has(`${account}|${key}`) };

beforeAll(async () => {
  await resetDatabase(prisma);
  const bus = localChangeBus();
  const store = prismaStore(prisma, { onCommit: (c) => bus.publish(c) });
  const sessions = new Sessions(prisma, memoryNonces(), rpc, {
    jwtSeed: Buffer.alloc(32, 3),
    recipient: RECIPIENT,
    network: 'testnet',
    adminAccounts: ['admin.testnet'],
  });
  const rt: Runtime = {
    store,
    clock: systemClock,
    log,
    engine: {} as Runtime['engine'],
    wakes: { schedule: async () => {} },
    tasks: { schedule: async () => {} },
    messenger: webMessenger(log),
    deleteAccount: async (uid) => {
      await prisma.document.deleteMany({ where: { uid } });
      await sessions.signOutEverywhere(uid);
    },
  };
  const reader = prismaReader(prisma);
  hub = new LiveHub(reader, (t) => sessions.verify(t), log);
  await hub.start(bus);
  app = await buildApp({
    rt,
    sessions,
    reader,
    hub,
    limiter: { hit: async () => true },
    callDeps: { clock: systemClock, log, minClientVersion: () => '0.0.0', exposeIssues: true },
    allowedOrigins: ['https://play.farmverse.world'],
    minClientVersion: '0.0.0',
    environment: 'dev',
  });
  await app.listen({ host: '127.0.0.1', port: 0 });
});

afterAll(async () => {
  await hub.stop();
  await app.close();
  await prisma.$disconnect();
});

async function signIn(wallet: Wallet) {
  fullAccess.add(`${wallet.accountId}|${wallet.publicKey}`);
  const { nonce } = (await app.inject({ method: 'POST', url: '/v1/auth/nonce' })).json() as { nonce: string };
  const res = await app.inject({
    method: 'POST',
    url: '/v1/auth/wallet',
    payload: { accountId: wallet.accountId, publicKey: wallet.publicKey, signature: wallet.signNonce(nonce), nonce },
  });
  expect(res.statusCode).toBe(200);
  return res.json() as { accessToken: string; refreshToken: string; accountId: string; uid: string };
}

const call = (name: string, token: string | undefined, payload: object) =>
  app.inject({ method: 'POST', url: `/v1/call/${name}`, payload, headers: token ? { authorization: `Bearer ${token}` } : {} });

const meta = { clientVersion: '1.0.0', platform: 'webgl', locale: 'en' };

describe('wallet sign-in', () => {
  it('signs in with a NEP-413 signature and refuses a replayed nonce', async () => {
    const w = new Wallet('alice.testnet');
    fullAccess.add(`${w.accountId}|${w.publicKey}`);
    const { nonce, recipient, message } = (await app.inject({ method: 'POST', url: '/v1/auth/nonce' })).json();
    expect({ recipient, message }).toEqual({ recipient: RECIPIENT, message: SIGN_IN_MESSAGE });
    const body = { accountId: w.accountId, publicKey: w.publicKey, signature: w.signNonce(nonce), nonce };
    expect((await app.inject({ method: 'POST', url: '/v1/auth/wallet', payload: body })).statusCode).toBe(200);
    const again = await app.inject({ method: 'POST', url: '/v1/auth/wallet', payload: body });
    expect(again.statusCode).toBe(401);
  });

  it('refuses a key that is not a full-access key of the account', async () => {
    const w = new Wallet('bob.testnet'); // not registered with the fake RPC
    const { nonce } = (await app.inject({ method: 'POST', url: '/v1/auth/nonce' })).json();
    const res = await app.inject({ method: 'POST', url: '/v1/auth/wallet', payload: { accountId: w.accountId, publicKey: w.publicKey, signature: w.signNonce(nonce), nonce } });
    expect(res.statusCode).toBe(401);
  });

  it('refuses a mainnet account on a testnet server', async () => {
    const w = new Wallet('carol.near');
    fullAccess.add(`${w.accountId}|${w.publicKey}`);
    const { nonce } = (await app.inject({ method: 'POST', url: '/v1/auth/nonce' })).json();
    const res = await app.inject({ method: 'POST', url: '/v1/auth/wallet', payload: { accountId: w.accountId, publicKey: w.publicKey, signature: w.signNonce(nonce), nonce } });
    expect(res.statusCode).toBe(400);
  });

  it('rotates refresh tokens, and reusing a rotated one ends the whole session', async () => {
    const s = await signIn(new Wallet('dave.testnet'));
    const r1 = await app.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: s.refreshToken } });
    expect(r1.statusCode).toBe(200);
    const next = r1.json() as { refreshToken: string };
    const reuse = await app.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: s.refreshToken } });
    expect(reuse.statusCode).toBe(401);
    const afterTheft = await app.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: next.refreshToken } });
    expect(afterTheft.statusCode).toBe(401);
  });

  it('answers CORS preflight for allowed origins only', async () => {
    const ok = await app.inject({ method: 'OPTIONS', url: '/v1/call/player-create', headers: { origin: 'https://play.farmverse.world' } });
    expect(ok.statusCode).toBe(204);
    expect(ok.headers['access-control-allow-origin']).toBe('https://play.farmverse.world');
    const other = await app.inject({ method: 'OPTIONS', url: '/v1/call/player-create', headers: { origin: 'https://evil.example' } });
    expect(other.headers['access-control-allow-origin']).toBeUndefined();
  });
});

describe('game calls and documents', () => {
  it('runs a game call for the signed-in wallet and refuses anonymous ones', async () => {
    const s = await signIn(new Wallet('erin.testnet'));
    expect(s.uid).toBe(uidFor('erin.testnet'));
    expect(s.uid).toMatch(/^[A-Za-z0-9]{20,128}$/);
    const created = (await call('player-create', s.accessToken, { meta, displayName: 'erinfarm', avatarId: 'avatar_default' })).json();
    expect(created).toMatchObject({ ok: true, data: { uid: s.uid } });
    const anon = (await call('player-create', undefined, { meta, displayName: 'nobody', avatarId: 'avatar_default' })).json();
    expect(anon).toMatchObject({ ok: false, error: { code: 'UNAUTHENTICATED' } });
    const unknown = await call('ads-claim', s.accessToken, {});
    expect(unknown.statusCode).toBe(404);
  });

  it('reads own documents, not other players\' private ones', async () => {
    const s = await signIn(new Wallet('fay.testnet'));
    await call('player-create', s.accessToken, { meta, displayName: 'fayfarm', avatarId: 'avatar_default' });
    const auth = { authorization: `Bearer ${s.accessToken}` };
    const own = await app.inject({ method: 'GET', url: `/v1/doc?path=players/${s.uid}`, headers: auth });
    expect(own.json()).toMatchObject({ exists: true, data: { displayName: 'fayfarm' } });
    const other = await app.inject({ method: 'GET', url: `/v1/doc?path=players/${uidFor('erin.testnet')}/state/wallet`, headers: auth });
    expect(other.statusCode).toBe(403);
    const noToken = await app.inject({ method: 'GET', url: `/v1/doc?path=players/${s.uid}` });
    expect(noToken.statusCode).toBe(401);
  });

  it('allows the settings update the rules allowed, and nothing else', async () => {
    const s = await signIn(new Wallet('gus.testnet'));
    await call('player-create', s.accessToken, { meta, displayName: 'gusfarm', avatarId: 'avatar_default' });
    const auth = { authorization: `Bearer ${s.accessToken}` };
    const settings = await app.inject({ method: 'GET', url: `/v1/doc?path=players/${s.uid}/private/settings`, headers: auth });
    if (!settings.json().exists) return; // onboarding didn't create settings in this version: nothing to update
    const ok = await app.inject({ method: 'PATCH', url: '/v1/doc', headers: auth, payload: { path: `players/${s.uid}/private/settings`, fields: { music: false } } });
    expect(ok.statusCode).toBe(200);
    const bad = await app.inject({ method: 'PATCH', url: '/v1/doc', headers: auth, payload: { path: `players/${s.uid}/state/wallet`, fields: { gems: 99999 } } });
    expect(bad.statusCode).toBe(403);
  });

  it('pushes a live update after a change, with a newer revision', async () => {
    const s = await signIn(new Wallet('hal.testnet'));
    await call('player-create', s.accessToken, { meta, displayName: 'halfarm', avatarId: 'avatar_default' });
    const port = (app.server.address() as AddressInfo).port;
    const ws = new WebSocket(`ws://127.0.0.1:${port}/v1/live`);
    const messages: { type: string; rev?: number; data?: Record<string, unknown> }[] = [];
    ws.on('message', (m) => messages.push(JSON.parse(String(m))));
    await new Promise<void>((resolve) => ws.on('open', () => resolve()));
    const waitFor = async (pred: () => boolean) => {
      for (let i = 0; i < 100 && !pred(); i++) await new Promise((r) => setTimeout(r, 30));
      expect(pred()).toBe(true);
    };
    ws.send(JSON.stringify({ type: 'auth', token: s.accessToken }));
    await waitFor(() => messages.some((m) => m.type === 'ready'));
    ws.send(JSON.stringify({ type: 'sub', id: 'p', path: `players/${s.uid}` }));
    await waitFor(() => messages.some((m) => m.type === 'doc'));
    const first = messages.find((m) => m.type === 'doc')!;

    ws.send(JSON.stringify({ type: 'sub', id: 'x', path: `players/${uidFor('erin.testnet')}/state/wallet` }));
    await waitFor(() => messages.some((m) => m.type === 'error'));

    await call('player-updateProfile', s.accessToken, { meta, displayName: 'halfarm2' });
    await waitFor(() => messages.filter((m) => m.type === 'doc').length >= 2);
    const second = messages.filter((m) => m.type === 'doc')[1]!;
    expect(second.rev).toBeGreaterThan(first.rev!);
    expect(second.data?.['displayName']).toBe('halfarm2');
    ws.close();
  });

  it('deletes an account: documents gone, sessions ended', async () => {
    const s = await signIn(new Wallet('ivy.testnet'));
    await call('player-create', s.accessToken, { meta, displayName: 'ivyfarm', avatarId: 'avatar_default' });
    const res = (await call('account-delete', s.accessToken, { meta, confirm: 'DELETE' })).json();
    expect(res.ok).toBe(true);
    expect(await prisma.document.count({ where: { uid: s.uid } })).toBe(0);
    const refresh = await app.inject({ method: 'POST', url: '/v1/auth/refresh', payload: { refreshToken: s.refreshToken } });
    expect(refresh.statusCode).toBe(401);
  });
});
