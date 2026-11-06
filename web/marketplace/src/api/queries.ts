/**
 * TanStack Query keys and hooks. Every read in the UI goes through here, so caching, loading/error
 * states, pagination and realtime invalidation work the same way everywhere.
 */
import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { config, DATA_MODE } from '../config';
import { useWallet } from '../chain/wallet';
import { getState, useStore } from '../state/store';
import { api } from '.';
import type { ListingQuery } from './types';

export const keys = {
  market: ['market'] as const,
  listings: (q: Omit<ListingQuery, 'cursor'>) => ['market', 'listings', q] as const,
  token: (id: string) => ['market', 'token', id] as const,
  tokens: (ids: string[]) => ['market', 'tokens', ids] as const,
  offers: (tokenId: string) => ['market', 'offers', tokenId] as const,
  notes: (account: string | null) => ['account', 'notes', account] as const,
  balance: (account: string | null) => ['account', 'balance', account] as const,
};

/** Listings (or one owner's tokens), a page at a time. */
export function useListings(q: Omit<ListingQuery, 'cursor' | 'limit'>, enabled = true) {
  return useInfiniteQuery({
    queryKey: keys.listings(q),
    queryFn: ({ pageParam, signal }) => api.listings({ ...q, cursor: pageParam, limit: config.pageSize }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.next_cursor,
    placeholderData: keepPreviousData,
    enabled,
  });
}

export const useToken = (id: string) => useQuery({ queryKey: keys.token(id), queryFn: ({ signal }) => api.token(id, signal) });

export const useTokensByIds = (ids: string[]) =>
  useQuery({ queryKey: keys.tokens(ids), queryFn: ({ signal }) => api.tokens(ids, signal), enabled: ids.length > 0 });

export const useOffers = (tokenId: string) => useQuery({ queryKey: keys.offers(tokenId), queryFn: ({ signal }) => api.offers(tokenId, signal) });

export function useNotes() {
  const { accountId } = useWallet();
  return useQuery({
    queryKey: keys.notes(accountId),
    queryFn: ({ signal }) => api.notifications(accountId!, signal),
    enabled: !!accountId,
  });
}

export function useMarkNotesRead() {
  const qc = useQueryClient();
  const { accountId } = useWallet();
  return useMutation({
    mutationFn: () => api.markNotificationsRead(accountId!),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.notes(accountId) }),
  });
}

/** FARM balance: ft_balance_of on chain, or the mock wallet. */
export function useBalance() {
  const { accountId } = useWallet();
  const mockBalance = useStore((s) => s.balance);
  const q = useQuery({
    queryKey: keys.balance(accountId),
    queryFn: () => (DATA_MODE === 'api' ? import('../chain/market').then((c) => c.farmBalance(accountId!)) : Promise.resolve(getState().balance)),
    enabled: !!accountId,
    staleTime: 15_000,
  });
  return DATA_MODE === 'api' ? q : { ...q, data: accountId ? mockBalance : undefined };
}
