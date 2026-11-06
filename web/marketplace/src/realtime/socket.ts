/**
 * Realtime market updates (docs 14 §5). With VITE_WS_URL: a reconnecting WebSocket to the game API.
 * Without: a simulator that produces the same messages from the mock. Either way each message
 * invalidates the queries it affects, and notes for the signed-in account raise a toast.
 */
import type { QueryClient } from '@tanstack/react-query';
import { useEffect, useSyncExternalStore } from 'react';
import { keys } from '../api/queries';
import { config } from '../config';
import { simulateMarketEvent, type MarketMessage } from '../state/store';

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
      qc.invalidateQueries({ queryKey: keys.token(m.token_id) });
      break;
    case 'offer':
      qc.invalidateQueries({ queryKey: keys.offers(m.token_id) });
      break;
    case 'note':
      if (m.to === account) {
        qc.invalidateQueries({ queryKey: keys.notes(account) });
        onNote(m.text, m.icon);
      }
      break;
  }
}

/** Connects while mounted; resubscribes when the signed-in account changes. */
export function useRealtime(qc: QueryClient, account: string | null, onNote: (text: string, icon?: string) => void) {
  useEffect(() => {
    if (!config.wsUrl) {
      setStatus('live');
      const t = setInterval(() => simulateMarketEvent().forEach((m) => handle(qc, account, m, onNote)), 45_000);
      return () => clearInterval(t);
    }

    let socket: { close: () => void } | null = null;
    let closed = false;
    setStatus('connecting');
    import('partysocket/ws').then(({ default: ReconnectingWebSocket }) => {
      if (closed) return;
      const url = new URL(config.wsUrl!);
      if (account) url.searchParams.set('account', account);
      const ws = new ReconnectingWebSocket(url.toString(), [], { maxRetries: Infinity, minReconnectionDelay: 1_000, maxReconnectionDelay: 15_000 });
      ws.addEventListener('open', () => setStatus('live'));
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
