import { useState } from 'react';
import { Shell } from './components/Shell';
import { Explore } from './pages/Explore';
import { Studio } from './pages/Studio';
import { TokenPage } from './pages/TokenPage';
import { useRoute } from './router';
import { SharedCanvas } from '@farmverse/shared/three/NftStage';

export function App() {
  const route = useRoute();
  const [search, setSearch] = useState('');
  return (
    <>
      <Shell route={route} search={search} onSearch={setSearch}>
        {route.name === 'token' && <TokenPage key={route.id} id={route.id} />}
        {route.name === 'studio' && <Studio />}
        {route.name === 'explore' && <Explore type={route.type} mine={route.mine} search={search} />}
      </Shell>
      <SharedCanvas />
    </>
  );
}
