import { useGameTheme } from '@farmverse/shared/gameTheme';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ToastHost } from './components/Dialog';
import { Shell } from './components/Shell';
import { Loading } from './components/ui';
import { Activity } from './pages/Activity';
import { Coupons } from './pages/Coupons';
import { Dashboard } from './pages/Dashboard';
import { News } from './pages/News';
import { PlayerPage } from './pages/PlayerPage';
import { Players } from './pages/Players';
import { Settings } from './pages/Settings';
import { SignIn } from './pages/SignIn';
import { useRoute, type Route } from './router';
import { SessionProvider, useSession } from './session';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 15_000, retry: (n, e) => n < 2 && (e as { status?: number }).status !== 403 && (e as { status?: number }).status !== 404, refetchOnWindowFocus: true },
  },
});

function Page({ route }: { route: Route }) {
  switch (route.name) {
    case 'players':
      return <Players {...route} />;
    case 'player':
      return <PlayerPage key={route.uid} uid={route.uid} />;
    case 'coupons':
      return <Coupons {...route} />;
    case 'news':
      return <News />;
    case 'settings':
      return <Settings />;
    case 'activity':
      return <Activity targetType={route.targetType} />;
    default:
      return <Dashboard />;
  }
}

function Gate() {
  useGameTheme(); // applies the saved colour theme
  const { status } = useSession();
  const route = useRoute();
  if (status === 'checking') return <Loading text="Signing you in…" />;
  if (status === 'signed-out') return <SignIn />;
  return (
    <Shell route={route}>
      <Page route={route} />
    </Shell>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <SessionProvider>
        <Gate />
        <ToastHost />
      </SessionProvider>
    </QueryClientProvider>
  );
}
