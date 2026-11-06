import { farmNumber } from '../chain/items';
import type { PrismaClient } from '../generated/prisma/client';

const DAY = 86_400_000;

export interface DayPoint {
  /** UTC day, YYYY-MM-DD. */
  day: string;
  signups: number;
  codesIssued: number;
  codesRedeemed: number;
  sales: number;
  /** FARM traded that day. */
  volume: number;
}

export interface Stats {
  at: number;
  players: { total: number; new24h: number; new7d: number; active24h: number; active7d: number; active30d: number; banned: number };
  coupons: {
    issued: number;
    issued24h: number;
    redeemed: number;
    open: number;
    byType: { typeId: string; unit: string; issued: number; redeemed: number; amountIssued: number; amountRedeemed: number }[];
  };
  chain: {
    minted: number;
    redeemed: number;
    live: number;
    listed: number;
    sales: number;
    volume: number;
    volume7d: number;
    /** When the indexer last moved, and the block it reached (null before its first run). */
    indexer: { blockHeight: number; updatedAt: number } | null;
  } | null;
  days: DayPoint[];
  /** The studio's drops (plan 15 §5): editions sold and FARM taken. */
  drops: DropStat[];
  /** The last nightly game ⇄ chain check (`ops/reconcile`, written by the chain-reconcile job), or null before its first run. */
  reconcile: ReconcileStat | null;
}

export interface DropStat {
  id: string;
  title: string;
  sold: number;
  cap: number | null;
  /** FARM per edition. */
  price: number;
  /** FARM taken (sold × price). */
  takings: number;
  paused: boolean;
}

export interface ReconcileStat {
  at: number;
  ok: boolean;
  vouchersIssued: number;
  vouchersMinted: number;
  vouchersRefunded: number;
  voucherTokensOnChain: number;
  refundedButMinted: number;
  mintedNotRecorded: number;
  redeemsOnChain: number;
  redeemsCredited: number;
  redeemsUncredited: number;
}

const n = (v: unknown) => Number(v ?? 0);
const dayKey = (ms: number) => new Date(ms).toISOString().slice(0, 10);

/** The dashboard's numbers. `days` is the last 30 UTC days, oldest first. */
export async function stats(prisma: PrismaClient, itemsContract: string, nowMs: number): Promise<Stats> {
  const since30 = nowMs - 30 * DAY;
  const start = Date.parse(`${dayKey(since30 + DAY)}T00:00:00Z`);

  const [players, activity, coupons, byType, signupDays, codeDays] = await Promise.all([
    prisma.$queryRaw<{ total: bigint; new24h: bigint; new7d: bigint; banned: bigint }[]>`
      SELECT count(*) AS total,
             count(*) FILTER (WHERE created_at > ${new Date(nowMs - DAY)}) AS new24h,
             count(*) FILTER (WHERE created_at > ${new Date(nowMs - 7 * DAY)}) AS new7d,
             count(*) FILTER (WHERE banned_at IS NOT NULL) AS banned
      FROM accounts`,
    prisma.$queryRaw<{ d1: bigint; d7: bigint; d30: bigint }[]>`
      SELECT count(*) FILTER (WHERE (data->>'lastSeenAt')::bigint > ${nowMs - DAY}) AS d1,
             count(*) FILTER (WHERE (data->>'lastSeenAt')::bigint > ${nowMs - 7 * DAY}) AS d7,
             count(*) FILTER (WHERE (data->>'lastSeenAt')::bigint > ${nowMs - 30 * DAY}) AS d30
      FROM documents WHERE collection = 'players'`,
    prisma.$queryRaw<{ issued: bigint; issued24h: bigint; redeemed: bigint; open: bigint }[]>`
      SELECT count(*) AS issued,
             count(*) FILTER (WHERE (data->>'issuedAt')::bigint > ${nowMs - DAY}) AS issued24h,
             count(*) FILTER (WHERE COALESCE((data->>'redeemedAt')::bigint, 0) > 0) AS redeemed,
             count(*) FILTER (WHERE COALESCE((data->>'redeemedAt')::bigint, 0) = 0 AND (data->>'expiresAt')::bigint >= ${nowMs}) AS open
      FROM documents WHERE collection = 'couponCodes'`,
    prisma.$queryRaw<{ type_id: string; unit: string; issued: bigint; redeemed: bigint; amount_issued: number; amount_redeemed: number }[]>`
      SELECT data->>'typeId' AS type_id, max(data->>'unit') AS unit, count(*) AS issued,
             count(*) FILTER (WHERE COALESCE((data->>'redeemedAt')::bigint, 0) > 0) AS redeemed,
             COALESCE(sum((data->>'amount')::float8), 0) AS amount_issued,
             COALESCE(sum((data->>'amount')::float8) FILTER (WHERE COALESCE((data->>'redeemedAt')::bigint, 0) > 0), 0) AS amount_redeemed
      FROM documents WHERE collection = 'couponCodes'
      GROUP BY 1 ORDER BY 3 DESC`,
    prisma.$queryRaw<{ day: string; c: bigint }[]>`
      SELECT to_char(created_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') AS day, count(*) AS c
      FROM accounts WHERE created_at >= ${new Date(start)} GROUP BY 1`,
    prisma.$queryRaw<{ day: string; issued: bigint; redeemed: bigint }[]>`
      SELECT day, sum(issued) AS issued, sum(redeemed) AS redeemed FROM (
        SELECT to_char(to_timestamp((data->>'issuedAt')::bigint / 1000.0) AT TIME ZONE 'UTC', 'YYYY-MM-DD') AS day, 1 AS issued, 0 AS redeemed
        FROM documents WHERE collection = 'couponCodes' AND (data->>'issuedAt')::bigint >= ${start}
        UNION ALL
        SELECT to_char(to_timestamp((data->>'redeemedAt')::bigint / 1000.0) AT TIME ZONE 'UTC', 'YYYY-MM-DD'), 0, 1
        FROM documents WHERE collection = 'couponCodes' AND COALESCE((data->>'redeemedAt')::bigint, 0) >= ${start}
      ) t GROUP BY 1`,
  ]);

  // Chain numbers come from the indexer's tables; a server without them still shows the rest.
  let chain: Stats['chain'] = null;
  let saleDays: { day: string; c: bigint; v: string }[] = [];
  try {
    const [tokens, market, cursor, days] = await Promise.all([
      prisma.$queryRaw<{ minted: bigint; redeemed: bigint }[]>`
        SELECT count(*) AS minted, count(*) FILTER (WHERE burned_at IS NOT NULL) AS redeemed
        FROM chain.tokens WHERE contract_id = ${itemsContract}`,
      prisma.$queryRaw<{ listed: bigint; sales: bigint; volume: string; volume7d: string }[]>`
        SELECT (SELECT count(*) FROM chain.listings WHERE contract_id = ${itemsContract}) AS listed,
               count(*) AS sales,
               COALESCE(sum(price), 0)::text AS volume,
               COALESCE(sum(price) FILTER (WHERE sold_at > ${new Date(nowMs - 7 * DAY)}), 0)::text AS volume7d
        FROM chain.sales WHERE contract_id = ${itemsContract}`,
      prisma.$queryRaw<{ block_height: bigint; updated_at: Date }[]>`SELECT block_height, updated_at FROM chain.cursors ORDER BY updated_at DESC LIMIT 1`,
      prisma.$queryRaw<{ day: string; c: bigint; v: string }[]>`
        SELECT to_char(sold_at AT TIME ZONE 'UTC', 'YYYY-MM-DD') AS day, count(*) AS c, sum(price)::text AS v
        FROM chain.sales WHERE contract_id = ${itemsContract} AND sold_at >= ${new Date(start)} GROUP BY 1`,
    ]);
    const t = tokens[0]!;
    const m = market[0]!;
    chain = {
      minted: n(t.minted),
      redeemed: n(t.redeemed),
      live: n(t.minted) - n(t.redeemed),
      listed: n(m.listed),
      sales: n(m.sales),
      volume: farmNumber(m.volume.split('.')[0]!),
      volume7d: farmNumber(m.volume7d.split('.')[0]!),
      indexer: cursor[0] ? { blockHeight: n(cursor[0].block_height), updatedAt: cursor[0].updated_at.getTime() } : null,
    };
    saleDays = days;
  } catch {
    chain = null;
  }

  const days: DayPoint[] = [];
  for (let t = start; t <= nowMs; t += DAY) {
    const day = dayKey(t);
    const code = codeDays.find((r) => r.day === day);
    const sale = saleDays.find((r) => r.day === day);
    days.push({
      day,
      signups: n(signupDays.find((r) => r.day === day)?.c),
      codesIssued: n(code?.issued),
      codesRedeemed: n(code?.redeemed),
      sales: n(sale?.c),
      volume: sale ? farmNumber(sale.v.split('.')[0]!) : 0,
    });
  }

  // Drops and the reconcile come from newer tables/jobs; a server without them still shows the rest.
  let drops: DropStat[] = [];
  try {
    const rows = await prisma.$queryRaw<{ drop_id: string; title: string; sold: bigint; cap: bigint | null; price: string; paused: boolean }[]>`
      SELECT drop_id, title, sold, cap, price::text AS price, paused FROM chain.drops WHERE contract_id = ${itemsContract} ORDER BY price ASC`;
    drops = rows.map((r) => {
      const price = farmNumber(r.price.split('.')[0]!);
      return { id: r.drop_id, title: r.title, sold: n(r.sold), cap: r.cap === null ? null : n(r.cap), price, takings: price * n(r.sold), paused: r.paused };
    });
  } catch {
    drops = [];
  }
  let reconcile: ReconcileStat | null = null;
  try {
    const doc = await prisma.document.findUnique({ where: { path: 'ops/reconcile' }, select: { data: true } });
    if (doc?.data && typeof doc.data === 'object') reconcile = doc.data as unknown as ReconcileStat;
  } catch {
    reconcile = null;
  }

  const p = players[0]!;
  const a = activity[0]!;
  const c = coupons[0]!;
  return {
    at: nowMs,
    players: { total: n(p.total), new24h: n(p.new24h), new7d: n(p.new7d), active24h: n(a.d1), active7d: n(a.d7), active30d: n(a.d30), banned: n(p.banned) },
    coupons: {
      issued: n(c.issued),
      issued24h: n(c.issued24h),
      redeemed: n(c.redeemed),
      open: n(c.open),
      byType: byType.map((r) => ({
        typeId: r.type_id,
        unit: r.unit,
        issued: n(r.issued),
        redeemed: n(r.redeemed),
        amountIssued: n(r.amount_issued),
        amountRedeemed: n(r.amount_redeemed),
      })),
    },
    chain,
    days,
    drops,
    reconcile,
  };
}
