import { randomBytes } from 'node:crypto';
import { NewsDoc } from '@farmverse/contracts';
import type { FastifyInstance, FastifyReply, FastifyRequest } from 'fastify';
import { z } from 'zod';
import { AppError } from '../../../game/src/core/errors';
import type { Logger } from '../../../game/src/core/logger';
import { catalogue } from '../../../game/src/game/catalogue';
import { redeemCoupon } from '../../../game/src/game/coupons';
import type { Env } from '../../../game/src/game/services';
import { paths } from '../../../game/src/infra/paths';
import type { Caller, Sessions } from '../auth/sessions';
import type { PrismaClient } from '../generated/prisma/client';
import type { RateLimiter } from '../infra/redis';
import { audit, AuditQuery, recentAudit } from './audit';
import { codeDetail, CodeSearch, couponsConfig, CouponsInput, saveCoupons, searchCodes } from './coupons';
import { OPS_PATH, OpsConfig, Version } from './ops';
import { accountOf, grant, GrantInput, playerDetail, PlayerSearch, searchPlayers, setBanned } from './players';
import { stats } from './stats';

export interface AdminOptions {
  readonly prisma: PrismaClient;
  readonly env: Env;
  readonly sessions: Sessions;
  readonly ops: OpsConfig;
  readonly limiter: RateLimiter;
  readonly itemsContract: string;
  /** Accounts that can't be banned from here (the admins themselves). */
  readonly adminAccounts: readonly string[];
}

const Reason = z.object({ reason: z.string().trim().min(3).max(300) });
const Uid = z.string().regex(/^u[0-9a-f]{39}$/);
const OpsInput = z.object({
  maintenance: z.boolean(),
  message: z.string().trim().max(200),
  minClientVersion: z.union([Version, z.literal('')]),
  /** null: the server default. */
  mintMinAccountHours: z.number().min(0).max(24 * 365).nullable().optional(),
  mintDailyValueCap: z.number().int().min(0).max(100_000_000).nullable().optional(),
  reason: z.string().trim().min(3).max(300),
});
const NewsInput = NewsDoc.omit({ publishedAt: true }).extend({ reason: z.string().trim().min(3).max(300) });

const STATUS: Record<string, number> = {
  UNAUTHENTICATED: 401,
  PERMISSION_DENIED: 403,
  NOT_FOUND: 404,
  INVALID_ARGUMENT: 400,
  RATE_LIMITED: 429,
  CONFLICT: 409,
  COUPON_UNKNOWN: 404,
  COUPON_USED: 409,
};

/** Plain words for the admin web (no codes on screen). */
const MESSAGES: Record<string, string> = {
  UNAUTHENTICATED: 'Please sign in again.',
  PERMISSION_DENIED: 'This wallet is not an admin.',
  NOT_FOUND: 'Not found.',
  INVALID_ARGUMENT: 'Some values are not valid.',
  RATE_LIMITED: 'Too many requests. Wait a minute and try again.',
  CONFLICT: 'Someone changed this at the same time. Try again.',
  COUPON_UNKNOWN: 'No coupon has that code.',
  COUPON_USED: 'That code was already redeemed or has expired.',
  ADMIN_PROTECTED: 'Admin accounts can’t be banned.',
  INTERNAL: 'Something went wrong on the server.',
};

function send(reply: FastifyReply, code: string, field?: string) {
  return reply.status(STATUS[code] ?? (code === 'ADMIN_PROTECTED' ? 409 : 500)).send({ code, message: MESSAGES[code] ?? MESSAGES['INTERNAL'], ...(field ? { field } : {}) });
}

function parse<T extends z.ZodType>(schema: T, value: unknown): z.output<T> {
  const r = schema.safeParse(value ?? {});
  if (!r.success) throw new AppError('INVALID_ARGUMENT', { field: r.error.issues[0]?.path.join('.') ?? '' });
  return r.data;
}

const bearer = (req: FastifyRequest) => {
  const h = req.headers.authorization;
  return h?.startsWith('Bearer ') ? h.slice(7) : undefined;
};

/**
 * The admin web's API (admin.farmverse.world), mounted under /v1/admin. Only wallets listed in
 * ADMIN_ACCOUNTS get past the first hook. Every change is checked, done, then written to the audit log.
 */
export function adminRoutes(o: AdminOptions, log: Logger) {
  const now = () => o.env.clock.nowMs();
  return async (app: FastifyInstance) => {
    app.decorateRequest('admin', null);

    app.addHook('onRequest', async (req, reply) => {
      if (req.method === 'OPTIONS') return;
      const who = await o.sessions.verify(bearer(req));
      if (!who) return send(reply, 'UNAUTHENTICATED');
      if (!who.isAdmin) return send(reply, 'PERMISSION_DENIED');
      if (!(await o.limiter.hit(`admin:${who.accountId}`, 300, 60))) return send(reply, 'RATE_LIMITED');
      (req as unknown as { admin: Caller }).admin = who;
    });

    app.setErrorHandler((error, req, reply) => {
      if (error instanceof AppError) return send(reply, error.code, (error.params as Record<string, string> | undefined)?.['field']);
      log.error('admin route failed', { url: req.url, error: String(error), stack: (error as Error).stack });
      return send(reply, 'INTERNAL');
    });

    const actor = (req: FastifyRequest) => (req as unknown as { admin: Caller }).admin.accountId;

    // Who is signed in, and the ids the forms offer (boosts come from the live catalogue).
    app.get('/me', async (req) => ({ accountId: actor(req), boosts: Object.keys((await catalogue(o.env)).boosts) }));

    app.get('/stats', async () => stats(o.prisma, o.itemsContract, now()));

    // ------------------------------------------------------------------ players

    app.get('/players', async (req) => searchPlayers(o.prisma, parse(PlayerSearch, req.query)));

    app.get<{ Params: { uid: string } }>('/players/:uid', async (req) => playerDetail(o.prisma, parse(Uid, req.params.uid), o.itemsContract, now()));

    app.post<{ Params: { uid: string } }>('/players/:uid/grant', async (req) => {
      const uid = parse(Uid, req.params.uid);
      const g = parse(GrantInput, req.body);
      const done = await grant(o.env, uid, g);
      const { reason, ...what } = g;
      await audit(o.prisma, { actor: actor(req), action: 'player.grant', targetType: 'player', targetId: uid, reason, details: { ...what, ...done } });
      return { ok: true, ...done };
    });

    app.post<{ Params: { uid: string } }>('/players/:uid/ban', async (req, reply) => {
      const uid = parse(Uid, req.params.uid);
      const { reason } = parse(Reason, req.body);
      if (o.adminAccounts.includes(await accountOf(o.prisma, uid))) return send(reply, 'ADMIN_PROTECTED');
      await setBanned(o.prisma, o.sessions, uid, true, reason, now());
      await audit(o.prisma, { actor: actor(req), action: 'player.ban', targetType: 'player', targetId: uid, reason });
      return { ok: true };
    });

    app.post<{ Params: { uid: string } }>('/players/:uid/unban', async (req) => {
      const uid = parse(Uid, req.params.uid);
      const { reason } = parse(Reason, req.body);
      await setBanned(o.prisma, o.sessions, uid, false, reason, now());
      await audit(o.prisma, { actor: actor(req), action: 'player.unban', targetType: 'player', targetId: uid, reason });
      return { ok: true };
    });

    app.post<{ Params: { uid: string } }>('/players/:uid/signout', async (req) => {
      const uid = parse(Uid, req.params.uid);
      const { reason } = parse(Reason, req.body);
      await accountOf(o.prisma, uid);
      await o.sessions.signOutEverywhere(uid);
      await audit(o.prisma, { actor: actor(req), action: 'player.signout', targetType: 'player', targetId: uid, reason });
      return { ok: true };
    });

    // ------------------------------------------------------------------ coupons

    app.get('/coupons', async () => couponsConfig(o.env));

    app.put('/coupons', async (req) => {
      const input = parse(CouponsInput, req.body);
      const { before, after } = await saveCoupons(o.env, input);
      await audit(o.prisma, {
        actor: actor(req),
        action: 'coupons.publish',
        targetType: 'catalogue',
        targetId: 'coupons',
        reason: input.reason,
        details: { before: { types: before.types, tasks: before.tasks }, after: { types: after.types, tasks: after.tasks } },
      });
      return after;
    });

    app.get('/coupons/codes', async (req) => searchCodes(o.prisma, parse(CodeSearch, req.query), now()));

    app.get<{ Params: { code: string } }>('/coupons/codes/:code', async (req) => codeDetail(o.prisma, req.params.code.slice(0, 40)));

    app.post<{ Params: { code: string } }>('/coupons/codes/:code/redeem', async (req) => {
      const code = req.params.code.slice(0, 40).trim().toUpperCase();
      const { reason } = parse(Reason, req.body);
      const done = await redeemCoupon(o.env, code);
      await audit(o.prisma, { actor: actor(req), action: 'coupon.redeem', targetType: 'coupon', targetId: code, reason, details: done });
      return codeDetail(o.prisma, code);
    });

    // ------------------------------------------------------------------ ops and news

    const opsView = () => ({ ...o.ops.current, fallbackMinClientVersion: o.ops.fallbackMinClientVersion, mintDefaults: o.ops.mintDefaults, stored: o.ops.stored ?? null });
    app.get('/ops', async () => opsView());

    app.put('/ops', async (req) => {
      const input = parse(OpsInput, req.body);
      const before = o.ops.current;
      const { reason, ...fields } = input;
      // an older admin page that doesn't send the mint fields keeps what is stored
      const next = {
        ...fields,
        mintMinAccountHours: fields.mintMinAccountHours !== undefined ? fields.mintMinAccountHours : (o.ops.stored?.mintMinAccountHours ?? null),
        mintDailyValueCap: fields.mintDailyValueCap !== undefined ? fields.mintDailyValueCap : (o.ops.stored?.mintDailyValueCap ?? null),
      };
      await o.env.store.runTransaction(async (tx) => {
        tx.set(OPS_PATH, { ...next, updatedAt: now(), updatedBy: actor(req) });
      });
      await o.ops.reload();
      await audit(o.prisma, { actor: actor(req), action: 'ops.update', targetType: 'ops', targetId: 'ops', reason, details: { before, after: next } });
      return opsView();
    });

    app.get('/news', async () => {
      const rows = await o.prisma.document.findMany({ where: { collection: 'news' }, orderBy: { createdAt: 'desc' }, take: 100, select: { path: true, data: true } });
      return { items: rows.map((r) => ({ id: r.path.slice('news/'.length), ...(r.data as Record<string, unknown>) })) };
    });

    app.post('/news', async (req) => {
      const { reason, ...item } = parse(NewsInput, req.body);
      const id = `n${now().toString(36)}${randomBytes(4).toString('hex')}`;
      const doc = { ...item, publishedAt: now() };
      await o.env.store.runTransaction(async (tx) => {
        tx.set(paths.newsItem(id), doc as unknown as Record<string, unknown>);
      });
      await audit(o.prisma, { actor: actor(req), action: 'news.publish', targetType: 'news', targetId: id, reason, details: { title: item.title, push: item.push === true } });
      return { id, ...doc };
    });

    app.delete<{ Params: { id: string } }>('/news/:id', async (req) => {
      const id = parse(z.string().regex(/^[A-Za-z0-9_-]{1,64}$/), req.params.id);
      const { reason } = parse(Reason, req.body);
      const existing = await o.env.store.read(paths.newsItem(id));
      if (!existing.exists) throw new AppError('NOT_FOUND');
      await o.env.store.runTransaction(async (tx) => {
        tx.delete(paths.newsItem(id));
      });
      await audit(o.prisma, { actor: actor(req), action: 'news.delete', targetType: 'news', targetId: id, reason, details: { title: existing.data?.['title'] } });
      return { ok: true };
    });

    // ------------------------------------------------------------------ audit

    app.get('/audit', async (req) => ({ entries: await recentAudit(o.prisma, parse(AuditQuery, req.query)) }));
  };
}
