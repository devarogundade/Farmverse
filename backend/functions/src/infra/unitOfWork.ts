import { assertDocumentPath, type DocData, type DocSnapshot, type TransactionPort } from './store';

type PendingWrite = { readonly kind: 'set'; readonly data: DocData; readonly merge: boolean } | { readonly kind: 'delete' };

/**
 * One transaction attempt: every read, then every write (03 §3.2).
 *
 * Firestore requires reads before writes; this makes that structural: a read
 * after the first write is a programmer error and throws. Writes are buffered,
 * one per path (a later write to the same path replaces an earlier one), and
 * flushed by {@link commit}. Read and write counts are kept for logs and cost
 * budgets.
 */
export class UnitOfWork {
  private readonly writes = new Map<string, PendingWrite>();
  private readonly cache = new Map<string, DocSnapshot>();
  private writing = false;
  private committed = false;
  private readCount = 0;

  constructor(private readonly tx: TransactionPort) {}

  get reads(): number {
    return this.readCount;
  }

  get pendingWrites(): number {
    return this.writes.size;
  }

  /** Read a document once per transaction; repeated reads hit the cache. */
  async read(path: string): Promise<DocSnapshot> {
    assertDocumentPath(path);
    if (this.writing) {
      throw new Error(`Read of '${path}' after a write in the same transaction.`);
    }
    const cached = this.cache.get(path);
    if (cached) return cached;
    const snapshot = await this.tx.get(path);
    this.readCount++;
    this.cache.set(path, snapshot);
    return snapshot;
  }

  /** Read several documents in parallel. */
  readAll(paths: readonly string[]): Promise<DocSnapshot[]> {
    return Promise.all(paths.map((path) => this.read(path)));
  }

  set(path: string, data: DocData, options: { merge?: boolean } = {}): void {
    this.beginWriting(path);
    this.writes.set(path, { kind: 'set', data, merge: options.merge === true });
  }

  delete(path: string): void {
    this.beginWriting(path);
    this.writes.set(path, { kind: 'delete' });
  }

  /** Flush buffered writes to the transaction. Called once, by the pipeline. */
  commit(): number {
    if (this.committed) throw new Error('A unit of work commits once.');
    this.committed = true;
    for (const [path, write] of this.writes) {
      if (write.kind === 'delete') this.tx.delete(path);
      else this.tx.set(path, write.data, { merge: write.merge });
    }
    return this.writes.size;
  }

  private beginWriting(path: string): void {
    assertDocumentPath(path);
    if (this.committed) throw new Error('Write after commit.');
    this.writing = true;
  }
}
