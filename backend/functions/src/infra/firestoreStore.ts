import type { Firestore } from 'firebase-admin/firestore';
import { AppError } from '../core/errors';
import { assertDocumentPath, type Store, type TransactionPort } from './store';
import type { Query as FirestoreQuery } from 'firebase-admin/firestore';

/**
 * The production {@link Store}: Firestore transactions through the Admin SDK.
 * Firestore retries the work on contention (optimistic concurrency), which is
 * why the work must be a pure function of what it reads — the action pipeline is.
 */
/** gRPC codes Firestore uses when a transaction lost to contention: ABORTED, and lock timeouts surfacing as DEADLINE_EXCEEDED. */
const CONTENTION_CODES = new Set([10, 4, 'aborted', 'deadline-exceeded']);

/**
 * A transaction that still loses after the SDK's own retries is not a defect:
 * it is a hot document (many buyers on one listing). Report it as CONFLICT
 * with a jittered retry hint, which the client's ActionQueue retries with the
 * same batchId (idempotent), instead of INTERNAL.
 */
export function asContention(error: unknown, random: () => number = Math.random): unknown {
  const code = (error as { code?: unknown } | null)?.code;
  if (!CONTENTION_CODES.has(code as never)) return error;
  return new AppError('CONFLICT', {}, { retryAfterMs: 250 + Math.floor(random() * 750), cause: error });
}

export function firestoreStore(db: Firestore, maxAttempts = 5): Store {
  return {
    runTransaction: (work) =>
      db.runTransaction(
        async (transaction) => {
          const port: TransactionPort = {
            get: async (path) => {
              assertDocumentPath(path);
              const snapshot = await transaction.get(db.doc(path));
              return { exists: snapshot.exists, data: snapshot.data() };
            },
            set: (path, data, options) => {
              assertDocumentPath(path);
              transaction.set(db.doc(path), data, { merge: options?.merge === true });
            },
            delete: (path) => {
              assertDocumentPath(path);
              transaction.delete(db.doc(path));
            },
          };
          return work(port);
        },
        { maxAttempts },
      ).catch((error: unknown) => {
        throw asContention(error);
      }),

    read: async (path) => {
      assertDocumentPath(path);
      const snapshot = await db.doc(path).get();
      return { exists: snapshot.exists, data: snapshot.data() };
    },

    writeBatch: async (writes) => {
      const batch = db.batch();
      for (const write of writes) {
        assertDocumentPath(write.path);
        batch.set(db.doc(write.path), write.data, { merge: write.merge === true });
      }
      await batch.commit();
    },

    query: async (query) => {
      let q: FirestoreQuery = db.collection(query.collection);
      for (const [field, op, value] of query.where) q = q.where(field, op, value);
      if (query.orderBy) q = q.orderBy(query.orderBy[0], query.orderBy[1]);
      const snapshot = await q.limit(query.limit).get();
      return snapshot.docs.map((doc) => ({ path: doc.ref.path, data: doc.data() }));
    },
  };
}
