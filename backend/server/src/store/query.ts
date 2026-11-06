import type { DocData, QueryRow } from '../../../functions/src/infra/store';
import { Prisma, type PrismaClient } from '../generated/prisma/client';

export type FilterOp = '==' | '<' | '<=' | '>' | '>=' | 'in';
export type Scalar = string | number | boolean;

export interface DocQuery {
  readonly collection: string;
  /** `in` takes a list of up to 30 values; every other op one value. */
  readonly filters: readonly { field: string; op: FilterOp; value: Scalar | readonly Scalar[] }[];
  /** Orders, most significant first. Ties break on the document path. */
  readonly orders: readonly { field: string; direction: 'asc' | 'desc' }[];
  readonly limit: number;
  /**
   * Firestore `startAfter`: one value per order, optionally followed by the last document's id
   * (the client's keyset cursor). Rows at or before it are skipped.
   */
  readonly startAfter?: readonly Scalar[];
  /**
   * Direction of the path tie-break. `asc` always (default) is what the game code and its
   * in-memory store expect; `last` follows the last order, as Firestore orders by document id.
   */
  readonly tieBreak?: 'asc' | 'last';
}

const FIELD = /^[A-Za-z_][A-Za-z0-9_]{0,63}$/;
const COLLECTION = /^[A-Za-z0-9_-]{1,128}(\/[A-Za-z0-9_-]{1,128}\/[A-Za-z0-9_-]{1,128})*$/;
const OPS: Record<Exclude<FilterOp, 'in'>, string> = { '==': '=', '<': '<', '<=': '<=', '>': '>', '>=': '>=' };

export function assertQuery(q: DocQuery): void {
  if (!COLLECTION.test(q.collection)) throw new Error(`Not a collection path: '${q.collection}'`);
  for (const f of [...q.filters.map((f) => f.field), ...q.orders.map((o) => o.field)]) {
    if (!FIELD.test(f)) throw new Error(`Not a queryable field: '${f}'`);
  }
  if (!Number.isInteger(q.limit) || q.limit < 1 || q.limit > 1000) throw new Error('limit must be 1-1000');
  if (q.startAfter && q.startAfter.length !== q.orders.length && q.startAfter.length !== q.orders.length + 1) {
    throw new Error('startAfter needs one value per order (and optionally the last id)');
  }
  for (const f of q.filters) {
    const v = f.value;
    const bad = f.op === 'in' ? !Array.isArray(v) || v.length === 0 || v.length > 30 : Array.isArray(v);
    if (bad) throw new Error(`bad value for ${f.op} on ${f.field}`);
  }
}

const tieDirection = (q: DocQuery): 'asc' | 'desc' => (q.tieBreak === 'last' ? (q.orders.at(-1)?.direction ?? 'asc') : 'asc');

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
  if (f.op === 'in') {
    const values = (f.value as readonly Scalar[]).map((v) => Prisma.sql`${JSON.stringify(v)}::jsonb`);
    return Prisma.sql`(data -> ${f.field}) IN (${Prisma.join(values)})`;
  }
  const v = f.value as Scalar;
  if (f.op === '==') return Prisma.sql`(data -> ${f.field}) = ${JSON.stringify(v)}::jsonb`;
  return Prisma.sql`${value(f.field, v)} ${Prisma.raw(OPS[f.op])} ${literal(v)}`;
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
  if (after.length === q.orders.length + 1) {
    // the last document's id: equal on every order, then past it on the path
    const parts = q.orders.map((o, j) => Prisma.sql`${orderExpr(o.field)} = ${literal(after[j]!)}`);
    parts.push(Prisma.sql`path ${Prisma.raw(tieDirection(q) === 'asc' ? '>' : '<')} ${`${q.collection}/${String(after.at(-1))}`}`);
    branches.push(Prisma.sql`(${Prisma.join(parts, ' AND ')})`);
  }
  return Prisma.sql`(${Prisma.join(branches, ' OR ')})`;
}

export async function queryDocuments(db: Pick<PrismaClient, '$queryRaw'>, q: DocQuery): Promise<QueryRow[]> {
  assertQuery(q);
  const where: Prisma.Sql[] = [Prisma.sql`collection = ${q.collection}`, ...q.filters.map(filterSql)];
  if (q.startAfter && (q.orders.length > 0 || q.startAfter.length === 1)) where.push(cursorSql(q));
  const order: Prisma.Sql[] = q.orders.map(
    (o) => Prisma.sql`${orderExpr(o.field)} ${Prisma.raw(o.direction === 'asc' ? 'ASC' : 'DESC')} NULLS LAST`,
  );
  order.push(Prisma.sql`path ${Prisma.raw(tieDirection(q) === 'asc' ? 'ASC' : 'DESC')}`);
  const rows = await db.$queryRaw<{ path: string; data: DocData }[]>`
    SELECT path, data FROM documents
    WHERE ${Prisma.join(where, ' AND ')}
    ORDER BY ${Prisma.join(order, ', ')}
    LIMIT ${q.limit}`;
  return rows.map((r) => ({ path: r.path, data: r.data }));
}
