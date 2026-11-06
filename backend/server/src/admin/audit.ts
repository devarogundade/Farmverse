import { z } from 'zod';
import type { PrismaClient } from '../generated/prisma/client';

export interface AuditInput {
  readonly actor: string;
  readonly action: string;
  readonly targetType: 'player' | 'coupon' | 'catalogue' | 'ops' | 'news';
  readonly targetId: string;
  readonly reason: string;
  readonly details?: Record<string, unknown>;
}

export interface AuditEntry {
  id: string;
  actor: string;
  action: string;
  targetType: string;
  targetId: string;
  reason: string;
  details: unknown;
  at: number;
}

/** Records one admin change. Called after the change succeeded. */
export async function audit(prisma: PrismaClient, e: AuditInput): Promise<void> {
  await prisma.adminAudit.create({
    data: { actor: e.actor, action: e.action, targetType: e.targetType, targetId: e.targetId, reason: e.reason, details: (e.details ?? {}) as object },
  });
}

export const AuditQuery = z.object({
  targetType: z.enum(['player', 'coupon', 'catalogue', 'ops', 'news']).optional(),
  targetId: z.string().max(128).optional(),
  actor: z.string().max(64).optional(),
  /** Entries before this time (epoch ms): the next page. */
  before: z.coerce.number().int().positive().optional(),
  limit: z.coerce.number().int().min(1).max(100).default(50),
});

export async function recentAudit(prisma: PrismaClient, q: Partial<z.infer<typeof AuditQuery>>): Promise<AuditEntry[]> {
  const rows = await prisma.adminAudit.findMany({
    where: {
      ...(q.targetType ? { targetType: q.targetType } : {}),
      ...(q.targetId ? { targetId: q.targetId } : {}),
      ...(q.actor ? { actor: q.actor } : {}),
      ...(q.before ? { at: { lt: new Date(q.before) } } : {}),
    },
    orderBy: { at: 'desc' },
    take: q.limit ?? 50,
  });
  return rows.map((r) => ({ ...r, details: r.details, at: r.at.getTime() }));
}
