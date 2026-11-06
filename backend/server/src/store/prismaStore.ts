import { AppError } from '../../../functions/src/core/errors';
import {
  assertDocumentPath,
  type DocData,
  type DocSnapshot,
  type Query,
  type QueryRow,
  type Store,
  type TransactionPort,
} from '../../../functions/src/infra/store';
import { Prisma, type PrismaClient } from '../generated/prisma/client';
import { queryDocuments } from './query';

/** One committed document change, for live updates and triggers. */
export interface DocChange {
  readonly path: string;
  readonly collection: string;
  readonly uid: string | null;
  /** The new revision; 0 when deleted. */
  readonly rev: number;
  readonly created: boolean;
  readonly deleted: boolean;
}

export interface PrismaStoreOptions {
  /** Called after each commit with what changed (never inside the transaction). */
  readonly onCommit?: (changes: readonly DocChange[]) => void;
  /** Attempts before a contended transaction is reported as CONFLICT (Firestore retries similarly). */
  readonly maxAttempts?: number;
  readonly random?: () => number;
}

type Write = { path: string; data?: DocData; merge?: boolean; remove?: boolean };
type Tx = Prisma.TransactionClient;

/**
 * The production {@link Store} on Postgres.
 *
 * Transactions run at SERIALIZABLE isolation: reads take no locks (everyone can read the shared
 * catalogue at once) and Postgres aborts a transaction whose reads another commit invalidated.
 * That is Firestore's optimistic model, so the game code's rule — the work is a pure function of
 * what it reads, and may run more than once — carries over unchanged. Writes are buffered and
 * applied at the end, also as in Firestore (and the in-memory store the game tests use).
 */
export function prismaStore(prisma: PrismaClient, options: PrismaStoreOptions = {}): Store {
  const maxAttempts = options.maxAttempts ?? 12;
  const random = options.random ?? Math.random;

  async function attempt<T>(work: (tx: TransactionPort) => Promise<T>): Promise<{ result: T; changes: DocChange[] }> {
    return prisma.$transaction(
      async (tx) => {
        const writes: Write[] = [];
        const port: TransactionPort = {
          get: async (path) => readIn(tx, path),
          set: (path, data, opts) => {
            assertDocumentPath(path);
            writes.push({ path, data: structuredClone(data), merge: opts?.merge === true });
          },
          delete: (path) => {
            assertDocumentPath(path);
            writes.push({ path, remove: true });
          },
        };
        const result = await work(port);
        const changes = await applyWrites(tx, writes);
        return { result, changes };
      },
      { isolationLevel: Prisma.TransactionIsolationLevel.Serializable, maxWait: 5_000, timeout: 20_000 },
    );
  }

  async function run<T>(work: (tx: TransactionPort) => Promise<T>): Promise<T> {
    for (let n = 1; ; n++) {
      try {
        const { result, changes } = await attempt(work);
        if (changes.length > 0) options.onCommit?.(changes);
        return result;
      } catch (error) {
        if (!isContention(error)) throw error;
        if (n >= maxAttempts) {
          throw new AppError('CONFLICT', {}, { retryAfterMs: 250 + Math.floor(random() * 750), cause: error });
        }
        // Exponential backoff with full jitter: racers spread out instead of colliding again.
        await sleep(Math.floor(random() * Math.min(1_000, 10 * 2 ** n)));
      }
    }
  }

  return {
    runTransaction: run,
    read: (path) => readIn(prisma, path),
    writeBatch: async (writes) => {
      await run(async (tx) => {
        for (const w of writes) tx.set(w.path, w.data, { merge: w.merge === true });
      });
    },
    query: (q: Query): Promise<QueryRow[]> =>
      queryDocuments(prisma, {
        collection: q.collection,
        filters: q.where.map(([field, op, value]) => ({ field, op, value })),
        orders: q.orderBy ? [{ field: q.orderBy[0], direction: q.orderBy[1] }] : [],
        limit: q.limit,
      }),
  };
}

async function readIn(db: Tx | PrismaClient, path: string): Promise<DocSnapshot> {
  assertDocumentPath(path);
  const row = await db.document.findUnique({ where: { path }, select: { data: true } });
  return row ? { exists: true, data: row.data as DocData } : { exists: false, data: undefined };
}

async function applyWrites(tx: Tx, writes: readonly Write[]): Promise<DocChange[]> {
  const changes = new Map<string, DocChange>();
  for (const w of writes) {
    const collection = collectionOf(w.path);
    const uid = ownerOf(w.path);
    if (w.remove) {
      await tx.document.deleteMany({ where: { path: w.path } });
      changes.set(w.path, { path: w.path, collection, uid, rev: 0, created: false, deleted: true });
      continue;
    }
    const existing = await tx.document.findUnique({ where: { path: w.path }, select: { data: true } });
    const data = w.merge && existing ? deepMerge(existing.data as DocData, w.data ?? {}) : (w.data ?? {});
    const json = toJson(data);
    const row = await tx.document.upsert({
      where: { path: w.path },
      create: { path: w.path, collection, uid, data: json },
      update: { data: json, rev: { increment: 1 } },
      select: { rev: true },
    });
    const before = changes.get(w.path);
    changes.set(w.path, {
      path: w.path,
      collection,
      uid,
      rev: Number(row.rev),
      created: before ? before.created || before.deleted : existing === null,
      deleted: false,
    });
  }
  return [...changes.values()];
}

/** `players/abc/state/island` → `players/abc/state`. */
export function collectionOf(path: string): string {
  return path.slice(0, path.lastIndexOf('/'));
}

/** The player a document belongs to (anything under `players/{uid}`), else null. */
export function ownerOf(path: string): string | null {
  const [root, uid] = path.split('/');
  return root === 'players' && uid ? uid : null;
}

/** Firestore drops `undefined` fields (ignoreUndefinedProperties); JSON does the same. */
function toJson(data: DocData): Prisma.InputJsonValue {
  return JSON.parse(JSON.stringify(data)) as Prisma.InputJsonValue;
}

function isPlainObject(value: unknown): value is DocData {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Firestore `merge: true`: nested maps merge, everything else replaces (same as MemoryStore). */
export function deepMerge(target: DocData, source: DocData): DocData {
  const out: DocData = { ...target };
  for (const [key, value] of Object.entries(source)) {
    const existing = out[key];
    out[key] = isPlainObject(existing) && isPlainObject(value) ? deepMerge(existing, value) : value;
  }
  return out;
}

const CONTENTION = new Set(['40001', '40P01']);

/** Serialization failures and deadlocks: the transaction lost a race and may simply run again. */
export function isContention(error: unknown): boolean {
  const e = error as { code?: unknown; cause?: { originalCode?: unknown; code?: unknown }; message?: unknown } | null;
  if (!e) return false;
  if (e.code === 'P2034') return true;
  if (CONTENTION.has(String(e.cause?.originalCode)) || CONTENTION.has(String(e.cause?.code))) return true;
  return typeof e.message === 'string' && /could not serialize access|deadlock detected|40001|40P01/.test(e.message);
}

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
