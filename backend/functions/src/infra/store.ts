/**
 * The persistence port. Application code talks to this, never to the Firestore
 * SDK, so the whole action pipeline runs against an in-memory store in tests and
 * against Firestore in production with the same semantics:
 *
 *   - a transaction reads first, then writes; writes are applied atomically on success
 *   - a throw discards every write
 */
export type DocData = Record<string, unknown>;

export interface DocSnapshot {
  readonly exists: boolean;
  readonly data: DocData | undefined;
}

export interface TransactionPort {
  get(path: string): Promise<DocSnapshot>;
  set(path: string, data: DocData, options?: { merge?: boolean }): void;
  delete(path: string): void;
}

export type QueryOp = '==' | '<' | '<=' | '>' | '>=';

/** A single-collection query: equality and range filters, one order, a page size (06 §4). */
export interface Query {
  readonly collection: string;
  readonly where: readonly (readonly [field: string, op: QueryOp, value: string | number | boolean])[];
  readonly orderBy?: readonly [field: string, direction: 'asc' | 'desc'];
  readonly limit: number;
}

export interface QueryRow {
  readonly path: string;
  readonly data: DocData;
}

export interface Store {
  runTransaction<T>(work: (tx: TransactionPort) => Promise<T>): Promise<T>;
  /** A read outside any transaction (catalogue, jobs scanning for work). */
  read(path: string): Promise<DocSnapshot>;
  /** Plain writes outside a transaction, applied as one atomic batch. */
  writeBatch(writes: readonly { path: string; data: DocData; merge?: boolean }[]): Promise<void>;
  query(query: Query): Promise<QueryRow[]>;
}

const SEGMENT = /^[A-Za-z0-9_-]{1,128}$/;

/** Reject any path that is not an even number of safe segments (a document path). */
export function assertDocumentPath(path: string): void {
  const segments = path.split('/');
  if (segments.length < 2 || segments.length % 2 !== 0 || !segments.every((s) => SEGMENT.test(s))) {
    throw new Error(`Not a valid document path: '${path}'`);
  }
}
