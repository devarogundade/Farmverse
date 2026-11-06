import { beforeEach, describe, expect, it } from 'vitest';
import type { Store } from '../../functions/src/infra/store';

/**
 * What every {@link Store} must do, run against the in-memory store (that the game tests use) and
 * the Postgres store (that production uses). If both pass, the game tests' results carry over.
 */
export function storeConformance(name: string, make: () => Promise<Store>): void {
  describe(`Store conformance: ${name}`, () => {
    let store: Store;
    beforeEach(async () => {
      store = await make();
    });

    it('reads what a transaction wrote, and reports missing documents', async () => {
      await store.runTransaction(async (tx) => tx.set('players/a/state/wallet', { gems: 5, nested: { x: 1 } }));
      expect(await store.read('players/a/state/wallet')).toEqual({ exists: true, data: { gems: 5, nested: { x: 1 } } });
      expect(await store.read('players/a/state/missing')).toEqual({ exists: false, data: undefined });
    });

    it('a throw discards every write of the transaction', async () => {
      await expect(
        store.runTransaction(async (tx) => {
          tx.set('players/a/state/one', { n: 1 });
          tx.set('players/a/state/two', { n: 2 });
          throw new Error('refused');
        }),
      ).rejects.toThrow('refused');
      expect((await store.read('players/a/state/one')).exists).toBe(false);
      expect((await store.read('players/a/state/two')).exists).toBe(false);
    });

    it('returns the work result', async () => {
      const out = await store.runTransaction(async (tx) => {
        const s = await tx.get('players/a/state/wallet');
        return s.exists ? 'old' : 'new';
      });
      expect(out).toBe('new');
    });

    it('merge: nested maps merge, arrays and scalars replace', async () => {
      await store.writeBatch([{ path: 'players/a/state/x', data: { a: 1, m: { p: 1, q: 1 }, list: [1, 2] } }]);
      await store.runTransaction(async (tx) => tx.set('players/a/state/x', { m: { q: 2, r: 3 }, list: [9] }, { merge: true }));
      expect((await store.read('players/a/state/x')).data).toEqual({ a: 1, m: { p: 1, q: 2, r: 3 }, list: [9] });
    });

    it('set without merge replaces the whole document', async () => {
      await store.writeBatch([{ path: 'players/a/state/x', data: { a: 1, b: 2 } }]);
      await store.runTransaction(async (tx) => tx.set('players/a/state/x', { c: 3 }));
      expect((await store.read('players/a/state/x')).data).toEqual({ c: 3 });
    });

    it('later writes to the same path win, in order', async () => {
      await store.runTransaction(async (tx) => {
        tx.set('players/a/state/x', { v: 1, keep: true });
        tx.set('players/a/state/x', { v: 2 }, { merge: true });
      });
      expect((await store.read('players/a/state/x')).data).toEqual({ v: 2, keep: true });
    });

    it('deletes', async () => {
      await store.writeBatch([{ path: 'players/a/state/x', data: { a: 1 } }]);
      await store.runTransaction(async (tx) => tx.delete('players/a/state/x'));
      expect((await store.read('players/a/state/x')).exists).toBe(false);
    });

    it('rejects paths that are not documents', async () => {
      await expect(store.read('players/a/state')).rejects.toThrow('Not a valid document path');
      await expect(store.runTransaction(async (tx) => tx.set('players/../x', {}))).rejects.toThrow('Not a valid document path');
    });

    describe('query', () => {
      beforeEach(async () => {
        await store.writeBatch([
          { path: 'news/n1', data: { at: 30, kind: 'event', live: true } },
          { path: 'news/n2', data: { at: 10, kind: 'patch', live: false } },
          { path: 'news/n3', data: { at: 20, kind: 'event', live: true } },
          { path: 'news/n4', data: { at: 20, kind: 'event', live: true } },
          { path: 'news/n1/comments/c1', data: { at: 99 } },
          { path: 'newsletter/x', data: { at: 50 } },
        ]);
      });

      it('scans one collection only (no subcollections, no prefix lookalikes)', async () => {
        const rows = await store.query({ collection: 'news', where: [], limit: 50 });
        expect(rows.map((r) => r.path)).toEqual(['news/n1', 'news/n2', 'news/n3', 'news/n4']);
      });

      it('filters by equality on strings and booleans', async () => {
        const events = await store.query({ collection: 'news', where: [['kind', '==', 'event'], ['live', '==', true]], limit: 50 });
        expect(events.map((r) => r.path)).toEqual(['news/n1', 'news/n3', 'news/n4']);
      });

      it('filters numeric ranges', async () => {
        const rows = await store.query({ collection: 'news', where: [['at', '>=', 20], ['at', '<', 30]], limit: 50 });
        expect(rows.map((r) => r.path)).toEqual(['news/n3', 'news/n4']);
      });

      it('orders numerically, breaks ties by path, and limits', async () => {
        const desc = await store.query({ collection: 'news', where: [], orderBy: ['at', 'desc'], limit: 3 });
        expect(desc.map((r) => r.path)).toEqual(['news/n1', 'news/n3', 'news/n4']);
        const asc = await store.query({ collection: 'news', where: [], orderBy: ['at', 'asc'], limit: 2 });
        expect(asc.map((r) => r.path)).toEqual(['news/n2', 'news/n3']);
      });

      it('returns the data with each row', async () => {
        const [row] = await store.query({ collection: 'news', where: [['kind', '==', 'patch']], limit: 1 });
        expect(row).toEqual({ path: 'news/n2', data: { at: 10, kind: 'patch', live: false } });
      });
    });

    it('concurrent read-modify-write transactions never lose an update', async () => {
      const path = 'counters/c';
      await store.writeBatch([{ path, data: { n: 0 } }]);
      await Promise.all(
        Array.from({ length: 15 }, () =>
          store.runTransaction(async (tx) => {
            const s = await tx.get(path);
            tx.set(path, { n: ((s.data?.['n'] as number) ?? 0) + 1 });
          }),
        ),
      );
      expect((await store.read(path)).data).toEqual({ n: 15 });
    });
  });
}
