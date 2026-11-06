import { z } from 'zod';
import { AppError } from '../../../functions/src/core/errors';
import { writeMail } from '../../../functions/src/game/inbox';
import { addBoosts, readInventory } from '../../../functions/src/game/shop';
import { catalogue } from '../../../functions/src/game/catalogue';
import type { Env } from '../../../functions/src/game/services';
import type { ExchangeOp } from '../../../functions/src/infra/islandEngine';
import { paths } from '../../../functions/src/infra/paths';
import { UnitOfWork } from '../../../functions/src/infra/unitOfWork';
import type { Sessions } from '../auth/sessions';
import { Prisma, type PrismaClient } from '../generated/prisma/client';
import { recentAudit, type AuditEntry } from './audit';

const ItemId = z.string().regex(/^[a-z][a-z0-9_]{1,39}$/, 'an item id like wheat');

/** One admin grant. Coins and gems arrive as a gift in the player's inbox; goods and boosts go straight to the island. */
export const GrantInput = z
  .object({
    coins: z.number().int().min(0).max(1_000_000).default(0),
    gems: z.number().int().min(0).max(10_000).default(0),
    /** Goods (item id → how many), stored in the island's farmhouses on its next sync. */
    goods: z.record(ItemId, z.number().int().min(1).max(100_000)).default({}),
    /** Boosts (boost id → how many), added to the inventory now. */
    boosts: z.record(ItemId, z.number().int().min(1).max(1_000)).default({}),
    /** The inbox message's title and text. */
    title: z.string().trim().min(1).max(60).default('A gift from the Farmverse team'),
    message: z.string().trim().max(64).default(''),
    reason: z.string().trim().min(3).max(300),
  })
  .refine((g) => g.coins > 0 || g.gems > 0 || Object.keys(g.goods).length > 0 || Object.keys(g.boosts).length > 0, 'grant something')
  .refine((g) => Object.keys(g.goods).length <= 8, 'at most 8 kinds of goods per grant');
export type GrantInput = z.infer<typeof GrantInput>;

export interface PlayerRow {
  accountId: string;
  uid: string;
  name: string | null;
  shortId: string | null;
  level: number | null;
  netWorth: number | null;
  createdAt: number;
  lastLoginAt: number;
  lastSeenAt: number | null;
  bannedAt: number | null;
}

interface RawRow {
  account_id: string;
  uid: string;
  name: string | null;
  short_id: string | null;
  level: number | null;
  net_worth: number | null;
  created_at: Date;
  last_login_at: Date;
  last_seen: bigint | null;
  banned_at: Date | null;
}

const toRow = (r: RawRow): PlayerRow => ({
  accountId: r.account_id,
  uid: r.uid,
  name: r.name,
  shortId: r.short_id,
  level: r.level,
  netWorth: r.net_worth,
  createdAt: r.created_at.getTime(),
  lastLoginAt: r.last_login_at.getTime(),
  lastSeenAt: r.last_seen === null ? null : Number(r.last_seen),
  bannedAt: r.banned_at?.getTime() ?? null,
});

const likeEscape = (s: string) => s.replace(/[\\%_]/g, (c) => `\\${c}`);

const ROW_SELECT = Prisma.sql`
  SELECT a.account_id, a.uid, a.created_at, a.last_login_at, a.banned_at,
         p.data->>'displayName' AS name,
         p.data->>'shortId' AS short_id,
         (p.data->>'lastSeenAt')::bigint AS last_seen,
         (i.data->'summary'->>'level')::int AS level,
         (i.data->'summary'->>'netWorth')::float8 AS net_worth
  FROM accounts a
  LEFT JOIN documents p ON p.path = 'players/' || a.uid
  LEFT JOIN documents i ON i.path = 'players/' || a.uid || '/state/island'`;

export const PlayerSearch = z.object({
  q: z.string().trim().max(64).default(''),
  status: z.enum(['all', 'banned']).default('all'),
  sort: z.enum(['newest', 'last_seen', 'level']).default('newest'),
  page: z.coerce.number().int().min(0).max(1_000).default(0),
});
const PAGE = 50;

/** Players by wallet, name, uid or player number. */
export async function searchPlayers(prisma: PrismaClient, s: z.infer<typeof PlayerSearch>): Promise<{ rows: PlayerRow[]; more: boolean }> {
  const like = `%${likeEscape(s.q)}%`;
  const where: Prisma.Sql[] = [];
  if (s.q) {
    where.push(Prisma.sql`(a.account_id ILIKE ${like} OR p.data->>'displayName' ILIKE ${like} OR a.uid = ${s.q} OR p.data->>'shortId' = ${s.q})`);
  }
  if (s.status === 'banned') where.push(Prisma.sql`a.banned_at IS NOT NULL`);
  const order =
    s.sort === 'last_seen'
      ? Prisma.sql`ORDER BY last_seen DESC NULLS LAST, a.account_id`
      : s.sort === 'level'
        ? Prisma.sql`ORDER BY level DESC NULLS LAST, a.account_id`
        : Prisma.sql`ORDER BY a.created_at DESC, a.account_id`;
  const rows = await prisma.$queryRaw<RawRow[]>`
    ${ROW_SELECT}
    ${where.length ? Prisma.sql`WHERE ${Prisma.join(where, ' AND ')}` : Prisma.empty}
    ${order}
    LIMIT ${PAGE + 1} OFFSET ${s.page * PAGE}`;
  return { rows: rows.slice(0, PAGE).map(toRow), more: rows.length > PAGE };
}

async function rowOf(prisma: PrismaClient, uid: string): Promise<PlayerRow | null> {
  const rows = await prisma.$queryRaw<RawRow[]>`${ROW_SELECT} WHERE a.uid = ${uid}`;
  return rows[0] ? toRow(rows[0]) : null;
}

const docsIn = async (prisma: PrismaClient, collection: string, take = 50) =>
  (await prisma.document.findMany({ where: { collection }, orderBy: { updatedAt: 'desc' }, take, select: { path: true, data: true } })).map((d) => ({
    id: d.path.slice(collection.length + 1),
    ...(d.data as Record<string, unknown>),
  }));

const dataOf = async (prisma: PrismaClient, path: string) =>
  ((await prisma.document.findUnique({ where: { path }, select: { data: true } }))?.data as Record<string, unknown> | undefined) ?? null;

export interface PlayerDetail {
  player: PlayerRow;
  banReason: string | null;
  avatarId: string | null;
  gems: number | null;
  xp: number | null;
  islandUpdatedAt: number | null;
  boosts: Record<string, number>;
  coupons: Record<string, unknown>[];
  rewards: Record<string, unknown>[];
  pendingGrants: { id: string; ops: ExchangeOp[]; at: number }[];
  sessions: { active: number; lastUserAgent: string | null };
  nfts: { tokenId: string; title: string; kind: string; quantity: number; mintedAt: number }[];
  audit: AuditEntry[];
}

/** Everything the player page shows. */
export async function playerDetail(prisma: PrismaClient, uid: string, itemsContract: string, nowMs: number): Promise<PlayerDetail> {
  const player = await rowOf(prisma, uid);
  if (!player) throw new AppError('NOT_FOUND');
  const [account, profile, wallet, island, inventory, grants, coupons, rewards, active, last, audit] = await Promise.all([
    prisma.account.findUnique({ where: { uid }, select: { banReason: true } }),
    dataOf(prisma, paths.player(uid)),
    dataOf(prisma, paths.wallet(uid)),
    prisma.document.findUnique({ where: { path: paths.island(uid) }, select: { data: true, updatedAt: true } }),
    dataOf(prisma, paths.inventory(uid)),
    dataOf(prisma, paths.grants(uid)),
    docsIn(prisma, paths.coupons(uid)),
    docsIn(prisma, paths.rewards(uid), 30),
    prisma.refreshToken.count({ where: { account: { uid }, revokedAt: null, expiresAt: { gt: new Date(nowMs) } } }),
    prisma.refreshToken.findFirst({ where: { account: { uid } }, orderBy: { createdAt: 'desc' }, select: { userAgent: true } }),
    recentAudit(prisma, { targetType: 'player', targetId: uid, limit: 30 }),
  ]);
  const nfts = await prisma.$queryRaw<{ token_id: string; title: string; kind: string; quantity: bigint; minted_at: Date }[]>`
    SELECT token_id, title, kind, quantity, minted_at FROM chain.tokens
    WHERE contract_id = ${itemsContract} AND owner_id = ${player.accountId} AND burned_at IS NULL
    ORDER BY minted_at DESC LIMIT 50`.catch(() => []);
  const islandData = island?.data as { summary?: { xp?: number } } | undefined;
  return {
    player,
    banReason: account?.banReason ?? null,
    avatarId: typeof profile?.['avatarId'] === 'string' ? (profile['avatarId'] as string) : null,
    gems: typeof wallet?.['gems'] === 'number' ? (wallet['gems'] as number) : null,
    xp: islandData?.summary?.xp ?? null,
    islandUpdatedAt: island?.updatedAt.getTime() ?? null,
    boosts: (inventory?.['boosts'] as Record<string, number> | undefined) ?? {},
    coupons,
    rewards,
    pendingGrants: (grants?.['items'] as PlayerDetail['pendingGrants'] | undefined) ?? [],
    sessions: { active, lastUserAgent: last?.userAgent ?? null },
    nfts: nfts.map((n) => ({ tokenId: n.token_id, title: n.title, kind: n.kind, quantity: Number(n.quantity), mintedAt: n.minted_at.getTime() })),
    audit,
  };
}

/**
 * Gives a player coins, gems, goods or boosts in one transaction. Coins and gems become a gift in
 * the inbox that the game collects (rewards-claim), so they land through the normal ledger; goods
 * join the island's owed grants (applied on its next sync); boosts go straight to the inventory.
 */
export async function grant(env: Env, uid: string, g: GrantInput): Promise<{ rewardMail: boolean; grantId: string | null }> {
  const cat = await catalogue(env);
  for (const id of Object.keys(g.boosts)) if (!cat.boosts[id]) throw new AppError('INVALID_ARGUMENT', { field: `boosts.${id}` });
  const goods = Object.entries(g.goods);
  const boosts = Object.keys(g.boosts).length > 0;
  return env.store.runTransaction(async (tx) => {
    const uow = new UnitOfWork(tx);
    const nowMs = env.clock.nowMs();
    const [profile, owed, inv] = await uow.readAll([
      paths.player(uid),
      goods.length > 0 ? paths.grants(uid) : paths.player(uid),
      boosts ? paths.inventory(uid) : paths.player(uid),
    ]);
    if (!profile?.exists) throw new AppError('NOT_FOUND');
    let grantId: string | null = null;
    if (goods.length > 0) {
      const items = ((owed?.data?.['items'] as { id: string; ops: ExchangeOp[]; at: number }[] | undefined) ?? []).slice();
      grantId = `admin_${nowMs.toString(36)}`;
      items.push({ id: grantId, ops: goods.map(([item, n]) => ({ op: 'give', item, n })), at: nowMs });
      uow.set(paths.grants(uid), { items, updatedAt: nowMs });
    }
    if (boosts) uow.set(paths.inventory(uid), addBoosts(readInventory(inv?.data, nowMs), g.boosts, nowMs) as unknown as Record<string, unknown>);
    writeMail(uow, uid, { title: g.title, body: g.message, coins: g.coins, gems: g.gems, route: '', expireDays: 30 }, nowMs);
    uow.commit();
    return { rewardMail: g.coins > 0 || g.gems > 0, grantId };
  });
}

/** Bans or unbans an account. A ban also ends every session (an access token already issued lasts at most 15 minutes). */
export async function setBanned(prisma: PrismaClient, sessions: Sessions, uid: string, banned: boolean, reason: string, nowMs: number): Promise<void> {
  const updated = await prisma.account.updateMany({
    where: { uid },
    data: banned ? { bannedAt: new Date(nowMs), banReason: reason } : { bannedAt: null, banReason: null },
  });
  if (updated.count === 0) throw new AppError('NOT_FOUND');
  if (banned) await sessions.signOutEverywhere(uid);
}

export async function accountOf(prisma: PrismaClient, uid: string): Promise<string> {
  const a = await prisma.account.findUnique({ where: { uid }, select: { accountId: true } });
  if (!a) throw new AppError('NOT_FOUND');
  return a.accountId;
}
