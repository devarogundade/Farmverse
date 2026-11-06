import { assertDocumentPath, type DocData, type DocSnapshot, type Query, type QueryRow, type Store, type TransactionPort } from './store';

/**
 * An in-memory {@link Store} with Firestore's transaction semantics: reads see the
 * committed state, writes apply atomically when the work resolves, and a throw
 * discards them. Transactions are serialised, which makes concurrency tests
 * deterministic. Used by unit and pipeline tests; the emulator covers the real SDK.
 */
export class MemoryStore implements Store {
  private readonly docs = new Map<string, DocData>();
  private queue: Promise<unknown> = Promise.resolve();
  transactions = 0;

  runTransaction<T>(work: (tx: TransactionPort) => Promise<T>): Promise<T> {
    const run = async (): Promise<T> => {
      this.transactions++;
      const buffered: { path: string; data?: DocData; merge?: boolean; remove?: boolean }[] = [];
      const tx: TransactionPort = {
        get: async (path) => this.snapshot(path),
        set: (path, data, options) => {
          assertDocumentPath(path);
          buffered.push({ path, data: clone(data), merge: options?.merge === true });
        },
        delete: (path) => {
          assertDocumentPath(path);
          buffered.push({ path, remove: true });
        },
      };
      const result = await work(tx);
      for (const write of buffered) {
        if (write.remove) this.docs.delete(write.path);
        else if (write.merge) this.docs.set(write.path, deepMerge(this.docs.get(write.path) ?? {}, write.data ?? {}));
        else this.docs.set(write.path, write.data ?? {});
      }
      return result;
    };
    const next = this.queue.then(run, run);
    this.queue = next.catch(() => undefined);
    return next;
  }

  async read(path: string): Promise<DocSnapshot> {
    return this.snapshot(path);
  }

  async writeBatch(writes: readonly { path: string; data: DocData; merge?: boolean }[]): Promise<void> {
    await this.runTransaction(async (tx) => {
      for (const write of writes) tx.set(write.path, write.data, { merge: write.merge === true });
    });
  }

  async query(query: Query): Promise<QueryRow[]> {
    const depth = query.collection.split('/').length + 1;
    const rows: QueryRow[] = [];
    for (const [path, data] of this.docs) {
      if (!path.startsWith(`${query.collection}/`) || path.split('/').length !== depth) continue;
      if (query.where.every(([field, op, value]) => compare(data[field], op, value))) rows.push({ path, data: clone(data) });
    }
    if (query.orderBy) {
      const [field, direction] = query.orderBy;
      const sign = direction === 'asc' ? 1 : -1;
      rows.sort((a, b) => sign * (Number(a.data[field]) - Number(b.data[field])) || a.path.localeCompare(b.path));
    } else {
      rows.sort((a, b) => a.path.localeCompare(b.path));
    }
    return rows.slice(0, query.limit);
  }

  /** Seed or inspect documents directly (tests only). */
  put(path: string, data: DocData): void {
    assertDocumentPath(path);
    this.docs.set(path, clone(data));
  }

  get(path: string): DocData | undefined {
    const data = this.docs.get(path);
    return data === undefined ? undefined : clone(data);
  }

  paths(prefix = ''): string[] {
    return [...this.docs.keys()].filter((path) => path.startsWith(prefix)).sort();
  }

  private snapshot(path: string): DocSnapshot {
    assertDocumentPath(path);
    const data = this.docs.get(path);
    return { exists: data !== undefined, data: data === undefined ? undefined : clone(data) };
  }
}

function compare(actual: unknown, op: Query['where'][number][1], expected: string | number | boolean): boolean {
  switch (op) {
    case '==': return actual === expected;
    case '<': return (actual as number) < (expected as number);
    case '<=': return (actual as number) <= (expected as number);
    case '>': return (actual as number) > (expected as number);
    case '>=': return (actual as number) >= (expected as number);
  }
}

function clone<T>(value: T): T {
  return structuredClone(value);
}

function isPlainObject(value: unknown): value is DocData {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function deepMerge(target: DocData, source: DocData): DocData {
  const out: DocData = { ...target };
  for (const [key, value] of Object.entries(source)) {
    const existing = out[key];
    out[key] = isPlainObject(existing) && isPlainObject(value) ? deepMerge(existing, value) : value;
  }
  return out;
}
