import type { DocData, QueryRow } from '../../../functions/src/infra/store';
import { Prisma, type PrismaClient } from '../generated/prisma/client';

export type FilterOp = '==' | '<' | '<=' | '>' | '>=';
export type Scalar = string | number | boolean;

export interface DocQuery {
  readonly collection: string;
  readonly filters: readonly { field: string; op: FilterOp; value: Scalar }[];
  /** Orders, most significant first. Ties always break on the document path. */
  readonly orders: readonly { field: string; direction: 'asc' | 'desc' }[];
  readonly limit: number;
  /** Firestore `startAfter`: one value per order; rows at or before it are skipped. */
  readonly startAfter?: readonly Scalar[];
}

const FIELD = /^[A-Za-z_][A-Za-z0-9_]{0,63}$/;
const COLLECTION = /^[A-Za-z0-9_-]{1,128}(\/[A-Za-z0-9_-]{1,128}\/[A-Za-z0-9_-]{1,128})*$/;
const OPS: Record<FilterOp, string> = { '==': '=', '<': '<', '<=': '<=', '>': '>', '>=': '>=' };

export function assertQuery(q: DocQuery): void {
  if (!COLLECTION.test(q.collection)) throw new Error(`Not a collection path: '${q.collection}'`);
  for (const f of [...q.filters.map((f) => f.field), ...q.orders.map((o) => o.field)]) {
    if (!FIELD.test(f)) throw new Error(`Not a queryable field: '${f}'`);
  }
  if (!Number.isInteger(q.limit) || q.limit < 1 || q.limit > 1000) throw new Error('limit must be 1-1000');
  if (q.startAfter && q.startAfter.length !== q.orders.length) throw new Error('startAfter needs one value per order');
}

/** The value of `field` as SQL: numbers compare as numbers, strings as text. */
function value(field: string, sample: Scalar): Prisma.Sql {
  return typeof sample === 'number'
    ? Prisma.sql`(CASE WHEN jsonb_typeof(data -> ${field}) = 'number' THEN (data ->> ${field})::numeric END)`
    : Prisma.sql`(data ->> ${field})`;
}

function literal(v: Scalar): Prisma.Sql {
  return typeof v === 'number' ? Prisma.sql`${v}::numeric` : Prisma.sql`${String(v)}`;
}

function filterSql(f: DocQuery['filters'][number]): Prisma.Sql {
  if (f.op === '==') return Prisma.sql`(data -> ${f.field}) = ${JSON.stringify(f.value)}::jsonb`;
  return Prisma.sql`${value(f.field, f.value)} ${Prisma.raw(OPS[f.op])} ${literal(f.value)}`;
}

/** Orders sort numerically (as the in-memory store does); documents missing the field sort last. */
function orderExpr(field: string): Prisma.Sql {
  return Prisma.sql`(CASE WHEN jsonb_typeof(data -> ${field}) = 'number' THEN (data ->> ${field})::numeric END)`;
}

/** `(o1 > c1) OR (o1 = c1 AND o2 > c2) …`, each `>` flipped for descending orders. */
function cursorSql(q: DocQuery): Prisma.Sql {
  const after = q.startAfter!;
  const branches: Prisma.Sql[] = [];
  for (let i = 0; i < q.orders.length; i++) {
    const parts: Prisma.Sql[] = [];
    for (let j = 0; j < i; j++) parts.push(Prisma.sql`${orderExpr(q.orders[j]!.field)} = ${literal(after[j]!)}`);
    const o = q.orders[i]!;
    parts.push(Prisma.sql`${orderExpr(o.field)} ${Prisma.raw(o.direction === 'asc' ? '>' : '<')} ${literal(after[i]!)}`);
    branches.push(Prisma.sql`(${Prisma.join(parts, ' AND ')})`);
  }
  return Prisma.sql`(${Prisma.join(branches, ' OR ')})`;
}

export async function queryDocuments(db: Pick<PrismaClient, '$queryRaw'>, q: DocQuery): Promise<QueryRow[]> {
  assertQuery(q);
  const where: Prisma.Sql[] = [Prisma.sql`collection = ${q.collection}`, ...q.filters.map(filterSql)];
  if (q.startAfter && q.orders.length > 0) where.push(cursorSql(q));
  const order: Prisma.Sql[] = q.orders.map(
    (o) => Prisma.sql`${orderExpr(o.field)} ${Prisma.raw(o.direction === 'asc' ? 'ASC' : 'DESC')} NULLS LAST`,
  );
  order.push(Prisma.sql`path ASC`);
  const rows = await db.$queryRaw<{ path: string; data: DocData }[]>`
    SELECT path, data FROM documents
    WHERE ${Prisma.join(where, ' AND ')}
    ORDER BY ${Prisma.join(order, ', ')}
    LIMIT ${q.limit}`;
  return rows.map((r) => ({ path: r.path, data: r.data }));
}
