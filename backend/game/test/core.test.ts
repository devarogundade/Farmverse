import { describe, expect, it } from 'vitest';
import { NotificationId } from '@farmverse/contracts';
import { newId } from '../src/core/ids';
import { compareVersions } from '../src/core/version';
import { MemoryStore } from '../src/infra/memoryStore';
import { UnitOfWork } from '../src/infra/unitOfWork';

describe('ids', () => {
  it('match the contract formats and do not repeat', () => {
    const ids = new Set(Array.from({ length: 2_000 }, () => newId('n')));
    expect(ids.size).toBe(2_000);
    for (const id of [...ids].slice(0, 50)) expect(NotificationId.safeParse(id).success).toBe(true);
  });
});

describe('versions', () => {
  it('compare numerically', () => {
    expect(compareVersions('1.10.0', '1.9.9')).toBeGreaterThan(0);
    expect(compareVersions('1.2.0', '1.2.0')).toBe(0);
    expect(compareVersions('0.9.0', '1.0.0')).toBeLessThan(0);
  });
});

describe('unit of work and memory store', () => {
  it('forbids reads after writes and caches repeated reads', async () => {
    const store = new MemoryStore();
    store.put('a/1', { n: 1 });
    await store.runTransaction(async (tx) => {
      const uow = new UnitOfWork(tx);
      await uow.read('a/1');
      await uow.read('a/1');
      expect(uow.reads).toBe(1);
      uow.set('a/2', { n: 2 });
      await expect(uow.read('a/1')).rejects.toThrow(/after a write/);
      uow.commit();
    });
    expect(store.get('a/2')).toEqual({ n: 2 });
  });

  it('discards every write when the work throws', async () => {
    const store = new MemoryStore();
    await expect(
      store.runTransaction(async (tx) => {
        tx.set('a/1', { n: 1 });
        throw new Error('boom');
      }),
    ).rejects.toThrow('boom');
    expect(store.get('a/1')).toBeUndefined();
  });

  it('merges nested maps and rejects unsafe paths', async () => {
    const store = new MemoryStore();
    store.put('a/1', { m: { x: 1, y: 1 } });
    await store.runTransaction(async (tx) => tx.set('a/1', { m: { y: 2 } }, { merge: true }));
    expect(store.get('a/1')).toEqual({ m: { x: 1, y: 2 } });
    expect(() => store.put('a/../b', {})).toThrow();
    expect(() => store.put('a', {})).toThrow();
  });
});

