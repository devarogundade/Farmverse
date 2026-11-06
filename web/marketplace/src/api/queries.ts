/**
 * TanStack Query keys and hooks. Every read in the UI goes through here, so caching, loading/error
 * states, pagination and realtime invalidation work the same way everywhere.
 */
import { keepPreviousData, useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { config, features } from '../config';
import { useWallet } from '../chain/wallet';
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
  marketConfig: ['chain', 'market-config'] as const,
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

export const useOffers = (tokenId: string) =>
  useQuery({ queryKey: keys.offers(tokenId), queryFn: ({ signal }) => api.offers(tokenId, signal), enabled: features.offers });

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

/** FARM balance of the connected account: ft_balance_of on the FARM contract. */
export function useBalance() {
  const { accountId } = useWallet();
  return useQuery({
    queryKey: keys.balance(accountId),
    queryFn: () => import('../chain/market').then((c) => c.farmBalance(accountId!)),
    enabled: !!accountId,
    staleTime: 15_000,
  });
}

/**
 * The market's fee as a fraction (fee_bps / 10 000), read from the market contract. Undefined
 * until loaded or if the contract can't be reached; the fee can only change by an owner call.
 */
export function useFeeRate(): number | undefined {
  const q = useQuery({
    queryKey: keys.marketConfig,
    queryFn: () => import('../chain/market').then((c) => c.marketConfig()),
    staleTime: 10 * 60_000,
  });
  return q.data ? q.data.fee_bps / 10_000 : undefined;
}
