import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { admin } from './http';

/* ---------- types (backend/server/src/admin) ---------- */

export interface Me {
  accountId: string;
  boosts: string[];
}

export interface DayPoint {
  day: string;
  signups: number;
  codesIssued: number;
  codesRedeemed: number;
  sales: number;
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
    indexer: { blockHeight: number; updatedAt: number } | null;
  } | null;
  days: DayPoint[];
  /** The studio's drops: editions sold and FARM taken. */
  drops?: { id: string; title: string; sold: number; cap: number | null; price: number; takings: number; paused: boolean }[];
  /** The last nightly game ⇄ chain check, or null before it first ran. */
  reconcile?: {
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
  } | null;
}

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

export interface AuditEntry {
  id: string;
  actor: string;
  action: string;
  targetType: string;
  targetId: string;
  reason: string;
  details: Record<string, unknown>;
  at: number;
}

export interface GrantOp {
  op: string;
  item?: string;
  n?: number;
  id?: string;
}

export interface PlayerDetail {
  player: PlayerRow;
  banReason: string | null;
  avatarId: string | null;
  gems: number | null;
  xp: number | null;
  islandUpdatedAt: number | null;
  boosts: Record<string, number>;
  coupons: (Record<string, unknown> & { id: string; code?: string; typeId?: string; task?: string; amount?: number; issuedAt?: number; expiresAt?: number })[];
  rewards: (Record<string, unknown> & { id: string; reason?: string; coins?: number; gems?: number; claimedAt?: number; createdAt?: number })[];
  pendingGrants: { id: string; ops: GrantOp[]; at: number }[];
  sessions: { active: number; lastUserAgent: string | null };
  nfts: { tokenId: string; title: string; kind: string; quantity: number; mintedAt: number }[];
  audit: AuditEntry[];
}

export interface CouponType {
  name: string;
  logoArt: string;
  logoGlyph: string;
  logoBg: string;
  accentA: string;
  accentB: string;
  ink: string;
  unit: string;
  codePrefix: string;
  redeemUrl: string;
  redeemLabel: string;
  steps: string[];
  expiryDays: number;
  linkedOnly: boolean;
}

export interface CouponTask {
  name: string;
  art: string;
  typeId: string;
  daily: boolean;
  progress: string;
  of: number;
  lockLevel: number;
  order: number;
  amount: number;
}

export interface CouponsConfig {
  source: 'published' | 'built-in';
  version: number;
  publishedAt: number;
  types: Record<string, CouponType>;
  tasks: Record<string, CouponTask>;
}

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

export interface Ops {
  maintenance: boolean;
  message: string;
  minClientVersion: string;
  fallbackMinClientVersion: string;
  mintMinAccountHours: number;
  mintDailyValueCap: number;
  /** The server's own limits, used while the fields below are empty. */
  /** Missing on servers older than the mint limits. */
  mintDefaults?: { minAccountHours: number; dailyValueCap: number };
  stored: {
    maintenance: boolean;
    message: string;
    minClientVersion: string;
    mintMinAccountHours?: number | null;
    mintDailyValueCap?: number | null;
    updatedAt: number;
    updatedBy: string;
  } | null;
}

export interface NewsItem {
  id: string;
  title: string;
  body: string;
  art?: string;
  url?: string;
  push?: boolean;
  publishedAt: number;
}

/* ---------- reads ---------- */

export const useMe = (enabled: boolean) => useQuery({ queryKey: ['me'], queryFn: () => admin<Me>('/me'), enabled, retry: false, staleTime: 5 * 60_000 });

export const useStats = () => useQuery({ queryKey: ['stats'], queryFn: () => admin<Stats>('/stats'), refetchInterval: 60_000 });

export const usePlayers = (q: { q: string; status: string; sort: string; page: number }) =>
  useQuery({ queryKey: ['players', q], queryFn: () => admin<{ rows: PlayerRow[]; more: boolean }>('/players', { query: q }), placeholderData: keepPreviousData });

export const usePlayer = (uid: string) => useQuery({ queryKey: ['player', uid], queryFn: () => admin<PlayerDetail>(`/players/${uid}`) });

export const useCoupons = () => useQuery({ queryKey: ['coupons'], queryFn: () => admin<CouponsConfig>('/coupons') });

export const useCodes = (q: { q: string; status: string; typeId: string; page: number }) =>
  useQuery({ queryKey: ['codes', q], queryFn: () => admin<{ rows: CodeRow[]; more: boolean }>('/coupons/codes', { query: q }), placeholderData: keepPreviousData });

export const useOps = () => useQuery({ queryKey: ['ops'], queryFn: () => admin<Ops>('/ops') });

export const useNews = () => useQuery({ queryKey: ['news'], queryFn: () => admin<{ items: NewsItem[] }>('/news') });

export const useAudit = (q: { targetType?: string; before?: number }) =>
  useQuery({ queryKey: ['audit', q], queryFn: () => admin<{ entries: AuditEntry[] }>('/audit', { query: { ...q, limit: 50 } }), placeholderData: keepPreviousData });

/* ---------- changes (each refreshes what it touched, and the activity log) ---------- */

function useChange<I, O>(fn: (input: I) => Promise<O>, touches: (input: I) => unknown[][]) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (_o, input) => {
      for (const key of [...touches(input), ['audit'], ['stats']]) void qc.invalidateQueries({ queryKey: key });
    },
  });
}

export interface GrantInput {
  coins: number;
  gems: number;
  goods: Record<string, number>;
  boosts: Record<string, number>;
  title: string;
  message: string;
  reason: string;
}

export const useGrant = (uid: string) =>
  useChange((g: GrantInput) => admin<{ ok: true }>(`/players/${uid}/grant`, { method: 'POST', body: g }), () => [['player', uid]]);

export const usePlayerAction = (uid: string) =>
  useChange(
    ({ action, reason }: { action: 'ban' | 'unban' | 'signout'; reason: string }) => admin<{ ok: true }>(`/players/${uid}/${action}`, { method: 'POST', body: { reason } }),
    () => [['player', uid], ['players']],
  );

export const useRedeemCode = () =>
  useChange(({ code, reason }: { code: string; reason: string }) => admin<CodeRow>(`/coupons/codes/${encodeURIComponent(code)}/redeem`, { method: 'POST', body: { reason } }), () => [['codes'], ['player']]);

export const useSaveCoupons = () =>
  useChange(
    (input: { types: Record<string, CouponType>; tasks: Record<string, CouponTask>; reason: string }) => admin<CouponsConfig>('/coupons', { method: 'PUT', body: input }),
    () => [['coupons']],
  );

export const useSaveOps = () =>
  useChange(
    (input: { maintenance: boolean; message: string; minClientVersion: string; mintMinAccountHours: number | null; mintDailyValueCap: number | null; reason: string }) =>
      admin<Ops>('/ops', { method: 'PUT', body: input }),
    () => [['ops']],
  );

export const usePublishNews = () =>
  useChange((input: { title: string; body: string; url?: string; push: boolean; reason: string }) => admin<NewsItem>('/news', { method: 'POST', body: input }), () => [['news']]);

export const useDeleteNews = () =>
  useChange(({ id, reason }: { id: string; reason: string }) => admin<{ ok: true }>(`/news/${id}`, { method: 'DELETE', body: { reason } }), () => [['news']]);
