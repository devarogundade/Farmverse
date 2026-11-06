import { SharedCanvas } from '@farmverse/shared/three/NftStage';
import { QueryClient, QueryClientProvider, useQueryClient } from '@tanstack/react-query';
import { lazy, Suspense, useCallback, useEffect, useState } from 'react';
import { WalletProvider, useWallet } from './chain/wallet';
import { ToastHost, toast } from './components/Dialog';
import { FlowHost } from './components/Flows';
import { Shell } from './components/Shell';
import { Explore } from './pages/Explore';
import { BuyFarm } from './pages/BuyFarm';
import { Merchants } from './pages/Merchants';
import { Rent } from './pages/Rent';
import { Shop } from './pages/Shop';
import { Studio } from './pages/Studio';
import { TokenPage } from './pages/TokenPage';
import { useRealtime } from './realtime/socket';
import { useRoute } from './router';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000, // realtime messages invalidate what changes; no need to refetch on every focus
      refetchOnWindowFocus: false,
      retry: (count, err) => count < 2 && (err as { status?: number }).status !== 404,
    },
  },
});

const Devtools = import.meta.env.DEV
  ? lazy(() => import('@tanstack/react-query-devtools').then((m) => ({ default: m.ReactQueryDevtools })))
  : () => null;

function useDebounced<T>(value: T, ms = 250) {
  const [v, setV] = useState(value);
  useEffect(() => {
    const t = setTimeout(() => setV(value), ms);
    return () => clearTimeout(t);
  }, [value, ms]);
  return v;
}

/** Credits for items used in game that didn't reach the server last time (api/game.ts). */
function PendingRedeems() {
  const { accountId } = useWallet();
  useEffect(() => {
    if (!accountId) return;
    import('./api/game')
      .then((g) => g.retryPendingRedeems(accountId))
      .then((n) => n > 0 && toast(n === 1 ? 'An item you used reached your island' : `${n} items you used reached your island`, 'Paint_fac_farmhouse_l1'))
      .catch(() => {});
  }, [accountId]);
  return null;
}

function Realtime() {
  const qc = useQueryClient();
  const { accountId } = useWallet();
  const onNote = useCallback((text: string, icon?: string) => toast(text, icon ?? 'Icon_Mail'), []);
  useRealtime(qc, accountId, onNote);
  return null;
}

function Routes() {
  const route = useRoute();
  const [search, setSearch] = useState('');
  const q = useDebounced(search.trim());
  return (
    <Shell route={route} search={search} onSearch={setSearch}>
      {route.name === 'token' && <TokenPage key={route.id} id={route.id} sell={route.sell} />}
      {route.name === 'studio' && <Studio />}
      {route.name === 'shop' && <Shop />}
      {route.name === 'rent' && <Rent />}
      {route.name === 'merchants' && <Merchants />}
      {route.name === 'buyfarm' && <BuyFarm />}
      {route.name === 'explore' && <Explore type={route.type} mine={route.mine} search={q} item={route.item} sort={route.sort} />}
    </Shell>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <WalletProvider>
          <FlowHost>
            <Routes />
            <Realtime />
            <PendingRedeems />
            <SharedCanvas />
            <ToastHost />
          </FlowHost>
      </WalletProvider>
      {/* Dev builds, wide screens only: on phones the button covers the bottom bar. */}
      {matchMedia('(min-width: 900px)').matches && (
        <Suspense fallback={null}>
          <Devtools buttonPosition="bottom-right" />
        </Suspense>
      )}
    </QueryClientProvider>
  );
}
