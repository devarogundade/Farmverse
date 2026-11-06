import websocket from '@fastify/websocket';
import Fastify, { type FastifyInstance, type FastifyReply, type FastifyRequest } from 'fastify';
import { z } from 'zod';
import { runCallable, type CallableDeps } from '../../game/src/core/callable';
import { AppError } from '../../game/src/core/errors';
import { canRead, checkUpdate, listLimit } from './access';
import type { Caller, Sessions } from './auth/sessions';
import { callDefinitions, type Runtime } from './calls';
import type { RateLimiter } from './infra/redis';
import { QuerySpec, type DocReader, type LiveHub, type LiveSocket } from './live';
import { marketRoutes, type MarketOptions } from './market/routes';
import { adminRoutes, type AdminOptions } from './admin/routes';
import type { OpsConfig } from './admin/ops';

export interface AppOptions {
  /** The NEAR RPC node, sent to the game in /v1/config (it reads balances there). */
  readonly rpcUrl?: string;
  readonly rt: Runtime;
  readonly sessions: Sessions;
  readonly reader: DocReader;
  readonly hub: LiveHub;
  readonly limiter: RateLimiter;
  readonly callDeps: CallableDeps;
  readonly allowedOrigins: readonly string[];
  readonly minClientVersion: string;
  readonly environment: string;
  /** The marketplace read API and live socket under /v1/market (off when absent). */
  readonly market?: MarketOptions;
  /** Live maintenance and min-version switches (`config/ops`); the env values when absent. */
  readonly ops?: OpsConfig;
  /** The admin web's API under /v1/admin (off when absent). */
  readonly admin?: AdminOptions;
}

const STATUS: Record<string, number> = {
  UNAUTHENTICATED: 401,
  PERMISSION_DENIED: 403,
  NOT_FOUND: 404,
  INVALID_ARGUMENT: 400,
  RATE_LIMITED: 429,
  CONFLICT: 409,
};

function refuse(reply: FastifyReply, code: string, requestId: string): FastifyReply {
  return reply.status(STATUS[code] ?? 500).send({ ok: false, error: { code, params: {} }, serverTimeMs: Date.now(), requestId });
}

const bearer = (req: FastifyRequest) => {
  const h = req.headers.authorization;
  return h?.startsWith('Bearer ') ? h.slice(7) : undefined;
};

const WalletSignIn = z.object({
  accountId: z.string().min(2).max(64),
  publicKey: z.string().max(128),
  signature: z.string().max(256),
  nonce: z.string().max(64),
});
const RefreshBody = z.object({ refreshToken: z.string().min(16).max(256) });
const UpdateBody = z.object({ path: z.string().max(512), fields: z.record(z.string().max(64), z.unknown()) });

/** The HTTP + WebSocket API. Built without listening, so tests use `app.inject`. */
export async function buildApp(o: AppOptions): Promise<FastifyInstance> {
  const app = Fastify({ logger: false, bodyLimit: 4 * 1024 * 1024, trustProxy: true });
  const calls = callDefinitions(o.rt);
  const log = o.rt.log;
  await app.register(websocket, { options: { maxPayload: 64 * 1024 } });

  // CORS for the game and marketplace origins (no cookies: tokens travel in headers).
  app.addHook('onRequest', async (req, reply) => {
    const origin = req.headers.origin;
    if (origin && o.allowedOrigins.includes(origin)) {
      reply.header('access-control-allow-origin', origin);
      reply.header('vary', 'origin');
      reply.header('access-control-allow-headers', 'authorization, content-type');
      reply.header('access-control-allow-methods', 'GET, POST, PUT, PATCH, DELETE, OPTIONS');
      reply.header('access-control-max-age', '600');
    }
    if (req.method === 'OPTIONS') return reply.status(204).send();
  });

  const caller = async (req: FastifyRequest): Promise<Caller | null> => o.sessions.verify(bearer(req));

  const limited = async (key: string, limit: number, windowSec: number) => !(await o.limiter.hit(key, limit, windowSec));

  app.get('/healthz', async () => ({ ok: true }));

  // The client's remote config: maintenance switch, minimum client version and feature values.
  const ops = () => o.ops?.current ?? { maintenance: false, message: '', minClientVersion: o.minClientVersion, mintMinAccountHours: 24, mintDailyValueCap: 5_000 };

  app.get('/v1/config', async () => ({
    minClientVersion: ops().minClientVersion,
    environment: o.environment,
    serverTimeMs: o.rt.clock.nowMs(),
    values: {
      min_client_version: ops().minClientVersion,
      maintenance: ops().maintenance,
      ...(ops().message ? { maintenance_message: ops().message } : {}),
      island_shadow: true,
      ...(o.rt.chain
        ? {
            items_contract: o.rt.chain.itemsContract,
            mint_min_hours: Math.ceil(ops().mintMinAccountHours),
            ...(o.rt.chain.farmContract ? { farm_contract: o.rt.chain.farmContract } : {}),
            ...(o.rt.chain.marketContract ? { market_contract: o.rt.chain.marketContract } : {}),
            faucet: Boolean(o.rt.chain.faucet),
            ...(o.rpcUrl ? { rpc_url: o.rpcUrl } : {}),
          }
        : {}),
    },
  }));

  // ---------------------------------------------------------------- auth

  app.post('/v1/auth/nonce', async (req, reply) => {
    if (await limited(`nonce:${req.ip}`, 30, 60)) return refuse(reply, 'RATE_LIMITED', req.id);
    return o.sessions.issueNonce();
  });

  app.post('/v1/auth/wallet', async (req, reply) => {
    if (await limited(`signin:${req.ip}`, 20, 60)) return refuse(reply, 'RATE_LIMITED', req.id);
    const body = WalletSignIn.safeParse(req.body);
    if (!body.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    try {
      return await o.sessions.signIn({ ...body.data, userAgent: req.headers['user-agent'] });
    } catch (error) {
      if (error instanceof AppError) {
        log.info('sign-in refused', { code: error.code, accountId: body.data.accountId });
        return refuse(reply, error.code, req.id);
      }
      throw error;
    }
  });

  app.post('/v1/auth/refresh', async (req, reply) => {
    const body = RefreshBody.safeParse(req.body);
    if (!body.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    try {
      return await o.sessions.refresh(body.data.refreshToken, req.headers['user-agent']);
    } catch (error) {
      if (error instanceof AppError) return refuse(reply, error.code, req.id);
      throw error;
    }
  });

  // Local development only (FARMVERSE_ENV=dev): the Unity editor signs in without a wallet.
  app.post('/v1/auth/dev', async (req, reply) => {
    const body = z.object({ accountId: z.string().min(2).max(64) }).safeParse(req.body);
    if (!body.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    try {
      return await o.sessions.devSignIn(body.data.accountId, req.headers['user-agent']);
    } catch (error) {
      if (error instanceof AppError) return refuse(reply, error.code, req.id);
      throw error;
    }
  });

  app.post('/v1/auth/logout', async (req, reply) => {
    const body = RefreshBody.safeParse(req.body);
    if (!body.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    await o.sessions.signOut(body.data.refreshToken);
    return { ok: true };
  });

  // ---------------------------------------------------------------- game calls

  app.post<{ Params: { name: string } }>('/v1/call/:name', async (req, reply) => {
    const definition = calls.get(req.params.name);
    if (!definition) return refuse(reply, 'NOT_FOUND', req.id);
    const who = await caller(req);
    if (who && (await limited(`call:${who.uid}`, 600, 60))) return refuse(reply, 'RATE_LIMITED', req.id);
    // Maintenance (admin web → config/ops): the game shows its maintenance screen; admins still play.
    if (ops().maintenance && who?.isAdmin !== true) {
      return { ok: false, error: { code: 'MAINTENANCE', params: {}, retryAfterMs: 60_000 }, serverTimeMs: Date.now(), requestId: req.id };
    }
    // The envelope carries success or refusal, like a callable: always HTTP 200 once routed.
    return runCallable(
      definition,
      req.body ?? {},
      { uid: who?.uid, isAdmin: who?.isAdmin === true, requestId: req.id, signInProvider: 'near' },
      o.callDeps,
    );
  });

  // ---------------------------------------------------------------- reads and the two direct writes

  app.get<{ Querystring: { path?: string } }>('/v1/doc', async (req, reply) => {
    const who = await caller(req);
    if (!who) return refuse(reply, 'UNAUTHENTICATED', req.id);
    const path = req.query.path ?? '';
    if (!canRead(who.uid, path)) return refuse(reply, 'PERMISSION_DENIED', req.id);
    const doc = await o.reader.get(path);
    return { path, exists: doc.exists, rev: doc.rev, data: doc.data ?? null };
  });

  app.post('/v1/query', async (req, reply) => {
    const who = await caller(req);
    if (!who) return refuse(reply, 'UNAUTHENTICATED', req.id);
    const query = QuerySpec.safeParse(req.body);
    if (!query.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    const max = listLimit(who.uid, query.data.collection);
    if (max === 0 || query.data.limit > max) return refuse(reply, 'PERMISSION_DENIED', req.id);
    try {
      return { docs: await o.reader.query(query.data) };
    } catch (error) {
      if (error instanceof Error && /queryable|collection|startAfter|limit/.test(error.message)) return refuse(reply, 'INVALID_ARGUMENT', req.id);
      throw error;
    }
  });

  app.patch('/v1/doc', async (req, reply) => {
    const who = await caller(req);
    if (!who) return refuse(reply, 'UNAUTHENTICATED', req.id);
    const body = UpdateBody.safeParse(req.body);
    if (!body.success) return refuse(reply, 'INVALID_ARGUMENT', req.id);
    const { path, fields } = body.data;
    const problem = await o.rt.store.runTransaction(async (tx) => {
      const current = await tx.get(path);
      const why = checkUpdate(who.uid, path, fields, current.data, o.rt.clock.nowMs());
      if (!why) tx.set(path, fields, { merge: true });
      return why;
    });
    if (problem) {
      log.info('direct update refused', { uid: who.uid, path, problem });
      return refuse(reply, 'PERMISSION_DENIED', req.id);
    }
    return { ok: true };
  });

  // ---------------------------------------------------------------- marketplace (docs 13 §9)

  if (o.market) await app.register(marketRoutes(o.market), { prefix: '/v1/market' });

  // ---------------------------------------------------------------- admin web

  if (o.admin) await app.register(adminRoutes(o.admin, log), { prefix: '/v1/admin' });

  // ---------------------------------------------------------------- live

  app.get('/v1/live', { websocket: true }, (socket) => o.hub.attach(socket as unknown as LiveSocket));

  app.setErrorHandler((error, req, reply) => {
    log.error('unhandled', { url: req.url, error: String(error), stack: (error as Error).stack });
    return refuse(reply, 'INTERNAL', req.id);
  });

  return app;
}
