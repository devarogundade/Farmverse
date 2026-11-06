import { CatalogueDoc, CouponTask, CouponType, DEFAULT_CATALOGUE } from '@farmverse/contracts';
import { z } from 'zod';
import { AppError } from '../../../game/src/core/errors';
import { resetCatalogueCache } from '../../../game/src/game/catalogue';
import type { Env } from '../../../game/src/game/services';
import { paths } from '../../../game/src/infra/paths';
import { Prisma, type PrismaClient } from '../generated/prisma/client';

const Id = z.string().regex(/^[a-z0-9_]{1,24}$/, 'lowercase letters, digits and _');

export const CouponsInput = z.object({
  types: z.record(Id, CouponType),
  tasks: z.record(Id, CouponTask),
  reason: z.string().trim().min(3).max(300),
});

export interface CouponsConfig {
  /** Where the live catalogue comes from: an admin publish, or the copy built into the server. */
  source: 'published' | 'built-in';
  version: number;
  publishedAt: number;
  types: CatalogueDoc['couponTypes'];
  tasks: CatalogueDoc['couponTasks'];
}

/** The catalogue in force, exactly as game/catalogue.ts picks it (but never cached). */
async function liveCatalogue(env: Env): Promise<{ doc: CatalogueDoc; source: CouponsConfig['source'] }> {
  const snap = await env.store.read(paths.catalogue());
  if (snap.exists) {
    const parsed = CatalogueDoc.safeParse(snap.data);
    if (parsed.success && parsed.data.version >= DEFAULT_CATALOGUE.version) return { doc: parsed.data, source: 'published' };
  }
  return { doc: DEFAULT_CATALOGUE, source: 'built-in' };
}

export async function couponsConfig(env: Env): Promise<CouponsConfig> {
  const { doc, source } = await liveCatalogue(env);
  return { source, version: doc.version, publishedAt: doc.publishedAt, types: doc.couponTypes, tasks: doc.couponTasks };
}

/**
 * Publishes new coupon types and tasks: the live catalogue with only those two parts replaced.
 * Every task must pay a type that exists. Other servers pick it up within a minute.
 */
export async function saveCoupons(env: Env, input: z.infer<typeof CouponsInput>): Promise<{ before: CouponsConfig; after: CouponsConfig }> {
  for (const [id, task] of Object.entries(input.tasks)) {
    if (!input.types[task.typeId]) throw new AppError('INVALID_ARGUMENT', { field: `tasks.${id}.typeId` });
  }
  const { doc, source } = await liveCatalogue(env);
  const nowMs = env.clock.nowMs();
  const next = CatalogueDoc.safeParse({ ...doc, couponTypes: input.types, couponTasks: input.tasks, publishedAt: nowMs });
  if (!next.success) throw new AppError('INVALID_ARGUMENT', { field: next.error.issues[0]?.path.join('.') ?? '' });
  await env.store.runTransaction(async (tx) => {
    tx.set(paths.catalogue(), next.data as unknown as Record<string, unknown>);
  });
  resetCatalogueCache();
  return {
    before: { source, version: doc.version, publishedAt: doc.publishedAt, types: doc.couponTypes, tasks: doc.couponTasks },
    after: { source: 'published', version: next.data.version, publishedAt: nowMs, types: next.data.couponTypes, tasks: next.data.couponTasks },
  };
}

export const CodeSearch = z.object({
  q: z.string().trim().max(64).default(''),
  status: z.enum(['all', 'open', 'redeemed', 'expired']).default('all'),
  typeId: z.string().max(24).default(''),
  page: z.coerce.number().int().min(0).max(1_000).default(0),
});
const PAGE = 50;

export interface CodeRow {
  code: string;
  typeId: string;
  taskId: string;
  task: string;
  amount: number;
  unit: string;
  uid: string;
  accountId: string | null;
  issuedAt: number;
  expiresAt: number;
  redeemedAt: number;
}

interface RawCode {
  path: string;
  data: Record<string, unknown>;
  account_id: string | null;
}

const toCode = (r: RawCode): CodeRow => ({
  code: r.path.slice('couponCodes/'.length),
  typeId: String(r.data['typeId'] ?? ''),
  taskId: String(r.data['taskId'] ?? ''),
  task: String(r.data['task'] ?? ''),
  amount: Number(r.data['amount'] ?? 0),
  unit: String(r.data['unit'] ?? ''),
  uid: String(r.data['uid'] ?? ''),
  accountId: r.account_id,
  issuedAt: Number(r.data['issuedAt'] ?? 0),
  expiresAt: Number(r.data['expiresAt'] ?? 0),
  redeemedAt: Number(r.data['redeemedAt'] ?? 0),
});

const CODE_SELECT = Prisma.sql`
  SELECT d.path, d.data, a.account_id
  FROM documents d
  LEFT JOIN accounts a ON a.uid = d.data->>'uid'
  WHERE d.collection = 'couponCodes'`;

/** Issued codes, newest first. `q` matches a code (start), a wallet or a uid. */
export async function searchCodes(prisma: PrismaClient, s: z.infer<typeof CodeSearch>, nowMs: number): Promise<{ rows: CodeRow[]; more: boolean }> {
  const where: Prisma.Sql[] = [];
  if (s.q) {
    const prefix = `couponCodes/${s.q.toUpperCase().replace(/[\\%_]/g, (c) => `\\${c}`)}%`;
    where.push(Prisma.sql`(d.path LIKE ${prefix} OR a.account_id = ${s.q} OR d.data->>'uid' = ${s.q})`);
  }
  if (s.typeId) where.push(Prisma.sql`d.data->>'typeId' = ${s.typeId}`);
  const redeemed = Prisma.sql`COALESCE((d.data->>'redeemedAt')::bigint, 0)`;
  const expires = Prisma.sql`COALESCE((d.data->>'expiresAt')::bigint, 0)`;
  if (s.status === 'open') where.push(Prisma.sql`${redeemed} = 0 AND ${expires} >= ${nowMs}`);
  if (s.status === 'redeemed') where.push(Prisma.sql`${redeemed} > 0`);
  if (s.status === 'expired') where.push(Prisma.sql`${redeemed} = 0 AND ${expires} < ${nowMs}`);
  const rows = await prisma.$queryRaw<RawCode[]>`
    ${CODE_SELECT} ${where.length ? Prisma.sql`AND ${Prisma.join(where, ' AND ')}` : Prisma.empty}
    ORDER BY (d.data->>'issuedAt')::bigint DESC, d.path
    LIMIT ${PAGE + 1} OFFSET ${s.page * PAGE}`;
  return { rows: rows.slice(0, PAGE).map(toCode), more: rows.length > PAGE };
}

export async function codeDetail(prisma: PrismaClient, code: string): Promise<CodeRow> {
  const rows = await prisma.$queryRaw<RawCode[]>`${CODE_SELECT} AND d.path = ${paths.couponCode(code.trim().toUpperCase())}`;
  if (!rows[0]) throw new AppError('COUPON_UNKNOWN');
  return toCode(rows[0]);
}
