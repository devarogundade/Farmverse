import { firestoreStore } from '../infra/firestoreStore';
import type { Store } from '../infra/store';
import { db } from './runtime';

let store: Store | undefined;

/** The Firestore store, created on first use (cold start pays once). */
export function services(): { readonly store: Store } {
  store ??= firestoreStore(db());
  return { store };
}
