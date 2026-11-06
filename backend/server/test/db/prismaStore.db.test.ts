import { afterAll, describe, expect, it } from 'vitest';
import { prismaStore, type DocChange } from '../../src/store/prismaStore';
import { storeConformance } from '../storeConformance';
import { resetDatabase, testPrisma } from './setup';

const prisma = testPrisma();
afterAll(() => prisma.$disconnect());

storeConformance('PrismaStore (Postgres)', async () => {
  await resetDatabase(prisma);
  return prismaStore(prisma);
});

describe('PrismaStore specifics', () => {
  it('reports each commit: created, updated (rev bumped) and deleted documents', async () => {
    await resetDatabase(prisma);
    const seen: DocChange[][] = [];
    const store = prismaStore(prisma, { onCommit: (c) => seen.push([...c]) });
    await store.runTransaction(async (tx) => tx.set('players/u1/state/wallet', { gems: 1 }));
    await store.runTransaction(async (tx) => tx.set('players/u1/state/wallet', { gems: 2 }));
    await store.runTransaction(async (tx) => tx.delete('players/u1/state/wallet'));
    expect(seen.map((c) => c.map(({ path, rev, created, deleted, uid }) => ({ path, rev, created, deleted, uid })))).toEqual([
      [{ path: 'players/u1/state/wallet', rev: 1, created: true, deleted: false, uid: 'u1' }],
      [{ path: 'players/u1/state/wallet', rev: 2, created: false, deleted: false, uid: 'u1' }],
      [{ path: 'players/u1/state/wallet', rev: 0, created: false, deleted: true, uid: 'u1' }],
    ]);
  });

  it('a read-only transaction reports nothing', async () => {
    await resetDatabase(prisma);
    let commits = 0;
    const store = prismaStore(prisma, { onCommit: () => commits++ });
    await store.runTransaction(async (tx) => tx.get('players/u1/state/wallet'));
    expect(commits).toBe(0);
  });

  it('pages with startAfter over several orders', async () => {
    await resetDatabase(prisma);
    const store = prismaStore(prisma);
    await store.writeBatch(
      [5, 3, 5, 1, 3].map((score, i) => ({ path: `leaderboards/w1/players/p${i}`, data: { score, at: 100 - i } })),
    );
    const { queryDocuments } = await import('../../src/store/query');
    const order = [{ field: 'score', direction: 'desc' as const }, { field: 'at', direction: 'asc' as const }];
    const first = await queryDocuments(prisma, { collection: 'leaderboards/w1/players', filters: [], orders: order, limit: 2 });
    expect(first.map((r) => r.path)).toEqual(['leaderboards/w1/players/p2', 'leaderboards/w1/players/p0']);
    const last = first[1]!.data;
    const next = await queryDocuments(prisma, {
      collection: 'leaderboards/w1/players',
      filters: [],
      orders: order,
      limit: 5,
      startAfter: [last['score'] as number, last['at'] as number],
    });
    expect(next.map((r) => r.path)).toEqual(['leaderboards/w1/players/p4', 'leaderboards/w1/players/p1', 'leaderboards/w1/players/p3']);
  });
});

describe('client queries (Firestore semantics)', () => {
  it('pages with the last id after the order values, ties following the last order', async () => {
    await resetDatabase(prisma);
    const store = prismaStore(prisma);
    await store.writeBatch(['a', 'b', 'c', 'd'].map((id, i) => ({ path: `news/${id}`, data: { at: i < 2 ? 5 : 9, kind: i % 2 ? 'x' : 'y' } })));
    const { queryDocuments } = await import('../../src/store/query');
    const q = { collection: 'news', filters: [], orders: [{ field: 'at', direction: 'desc' as const }], limit: 2, tieBreak: 'last' as const };
    const first = await queryDocuments(prisma, q);
    expect(first.map((r) => r.path)).toEqual(['news/d', 'news/c']);
    const next = await queryDocuments(prisma, { ...q, startAfter: [9, 'c'] });
    expect(next.map((r) => r.path)).toEqual(['news/b', 'news/a']);
  });

  it('filters with in', async () => {
    await resetDatabase(prisma);
    const store = prismaStore(prisma);
    await store.writeBatch(['a', 'b', 'c'].map((id, i) => ({ path: `news/${id}`, data: { kind: ['x', 'y', 'z'][i] } })));
    const { queryDocuments } = await import('../../src/store/query');
    const rows = await queryDocuments(prisma, { collection: 'news', filters: [{ field: 'kind', op: 'in', value: ['x', 'z'] }], orders: [], limit: 10 });
    expect(rows.map((r) => r.path)).toEqual(['news/a', 'news/c']);
  });
});
