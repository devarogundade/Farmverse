/**
 * Live market updates (docs 14 §5): a reconnecting WebSocket to the game API's /v1/market/live,
 * fed by the chain indexer. Each message invalidates the queries it affects; notes for the
 * signed-in account raise a toast.
 */
import type { QueryClient } from '@tanstack/react-query';
import { useEffect, useSyncExternalStore } from 'react';
import { keys } from '../api/queries';
import { config } from '../config';

/** What the server pushes (backend/server/src/chain/project.ts `MarketMessage`). */
export type MarketMessage =
  | { type: 'listing' | 'delist' | 'sale' | 'redeem'; token_id: string }
  | { type: 'offer'; token_id: string; to: string }
  | { type: 'note'; to: string; text: string; icon?: string; href?: string };

export type LiveStatus = 'connecting' | 'live' | 'offline';

let status: LiveStatus = 'connecting';
const listeners = new Set<() => void>();
const setStatus = (s: LiveStatus) => {
  status = s;
  listeners.forEach((l) => l());
};
export const useLiveStatus = () =>
  useSyncExternalStore(
    (l) => {
      listeners.add(l);
      return () => listeners.delete(l);
    },
    () => status,
  );

function handle(qc: QueryClient, account: string | null, m: MarketMessage, onNote: (text: string, icon?: string) => void) {
  switch (m.type) {
    case 'listing':
    case 'delist':
    case 'sale':
    case 'redeem':
      qc.invalidateQueries({ queryKey: ['market', 'listings'] });
      qc.invalidateQueries({ queryKey: ['market', 'tokens'] });
      qc.invalidateQueries({ queryKey: keys.token(m.token_id) });
      break;
    case 'offer':
      qc.invalidateQueries({ queryKey: keys.offers(m.token_id) });
      break;
    case 'note':
      if (m.to === account) {
        qc.invalidateQueries({ queryKey: keys.notes(account) });
        qc.invalidateQueries({ queryKey: keys.balance(account) });
        onNote(m.text, m.icon);
      }
      break;
  }
}

/** Connects while mounted; reconnects when the signed-in account changes. */
export function useRealtime(qc: QueryClient, account: string | null, onNote: (text: string, icon?: string) => void) {
  useEffect(() => {
    if (!config.wsUrl) {
      setStatus('offline');
      return;
    }
    let socket: { close: () => void } | null = null;
    let closed = false;
    setStatus('connecting');
    import('partysocket/ws').then(({ default: ReconnectingWebSocket }) => {
      if (closed) return;
      const url = new URL(config.wsUrl!);
      if (account) url.searchParams.set('account', account);
      const ws = new ReconnectingWebSocket(url.toString(), [], { maxRetries: Infinity, minReconnectionDelay: 1_000, maxReconnectionDelay: 15_000 });
      ws.addEventListener('open', () => {
        setStatus('live');
        // Anything that changed while we were away.
        qc.invalidateQueries({ queryKey: keys.market });
      });
      ws.addEventListener('close', () => setStatus('offline'));
      ws.addEventListener('message', (e: MessageEvent) => {
        try {
          handle(qc, account, JSON.parse(String(e.data)) as MarketMessage, onNote);
        } catch {
          // ignore malformed messages
        }
      });
      socket = ws;
    });
    return () => {
      closed = true;
      socket?.close();
    };
  }, [qc, account, onNote]);
}
