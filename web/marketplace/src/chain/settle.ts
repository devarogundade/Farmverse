/**
 * After a transaction: wait for the indexer (backend/server/src/chain/indexer.ts) to read past it,
 * so the queries refetched next already show the change.
 *
 * When the wallet returns, every receipt of the transaction has run, so the chain's newest block
 * is at or after them. The indexer follows final blocks and tells how far it got at
 * /v1/market/head; once that passes the newest block plus a few confirmations, the read API has
 * the change. A server that can't say how far it got gets a plain pause instead.
 */
import { useSyncExternalStore } from 'react';
import { http } from '../api/http';
import { getRpc } from './near';

/** Blocks past the chain's tip the indexer must reach (about a second each). */
export const CONFIRMATIONS = 2;
/** The longest a dialog waits for the indexer; the refreshes after it still catch a late one. */
const TIMEOUT_MS = 20_000;
/** The pause when the server can't say how far it read. */
const FALLBACK_MS = 4_000;
const POLL_MS = 800;

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/* ---------- "saving" state, for the dialog's waiting screen ---------- */

let settling = 0;
const listeners = new Set<() => void>();
const emit = () => listeners.forEach((l) => l());

/** True while a transaction is confirmed but the market hasn't caught up yet. */
export const useSettling = () =>
  useSyncExternalStore(
    (l) => (listeners.add(l), () => listeners.delete(l)),
    () => settling > 0,
  );

async function chainTip(): Promise<number> {
  const block = await (await getRpc()).viewBlock({ finality: 'optimistic' });
  return Number(block.header.height);
}

async function indexedTo(): Promise<number | null> {
  const r = await http<never>('/head', { timeoutMs: 5_000 });
  return (r as { block_height: number | null }).block_height;
}

/**
 * Resolves once the indexer has read `confirmations` blocks past the chain's tip as it is now.
 * Never throws: true when it caught up, false when it timed out or couldn't tell (after a pause).
 */
export async function waitForIndexer({ confirmations = CONFIRMATIONS, timeoutMs = TIMEOUT_MS } = {}): Promise<boolean> {
  settling++;
  emit();
  try {
    let target: number;
    try {
      target = (await chainTip()) + confirmations;
    } catch {
      await sleep(FALLBACK_MS);
      return false;
    }
    const until = Date.now() + timeoutMs;
    while (Date.now() < until) {
      let head: number | null;
      try {
        head = await indexedTo();
      } catch {
        await sleep(FALLBACK_MS); // an older server without /head, or it's unreachable
        return false;
      }
      if (head != null && head >= target) return true;
      await sleep(POLL_MS);
    }
    return false;
  } finally {
    settling--;
    emit();
  }
}
