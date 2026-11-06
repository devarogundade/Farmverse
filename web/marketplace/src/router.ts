import { useEffect, useState } from 'react';

export type Route =
  | { name: 'explore'; type?: string; mine?: boolean; item?: string; sort?: string }
  /** `sell`: arrived from the game's "Sell on market" — open the list flow. */
  | { name: 'token'; id: string; sell?: boolean }
  | { name: 'shop' }
  | { name: 'rent' }
  | { name: 'merchants' }
  | { name: 'studio' };

function parse(hash: string): Route {
  const [path, query = ''] = hash.replace(/^#/, '').split('?');
  const parts = path.split('/').filter(Boolean);
  const params = new URLSearchParams(query);
  if (parts[0] === 'token' && parts[1]) return { name: 'token', id: decodeURIComponent(parts[1]), sell: params.get('sell') === '1' };
  if (parts[0] === 'studio') return { name: 'studio' };
  if (parts[0] === 'shop') return { name: 'shop' };
  if (parts[0] === 'rent') return { name: 'rent' };
  if (parts[0] === 'town') return { name: 'merchants' };
  return {
    name: 'explore',
    type: params.get('type') ?? undefined,
    mine: parts[0] === 'mine',
    item: params.get('item') ?? undefined,
    sort: params.get('sort') ?? undefined,
  };
}

export function useRoute(): Route {
  const [route, setRoute] = useState(() => parse(location.hash));
  useEffect(() => {
    const on = () => {
      setRoute(parse(location.hash));
      window.scrollTo({ top: 0 });
    };
    window.addEventListener('hashchange', on);
    return () => window.removeEventListener('hashchange', on);
  }, []);
  return route;
}

export const href = {
  explore: (type?: string) => (type ? `#/?type=${type}` : '#/'),
  /** Every listing of one item, cheapest per unit first (the game's "Buy more"). */
  item: (itemId: string) => `#/?item=${encodeURIComponent(itemId)}&sort=unit_price`,
  mine: (type?: string) => (type ? `#/mine?type=${type}` : '#/mine'),
  token: (id: string) => `#/token/${encodeURIComponent(id)}`,
  shop: () => '#/shop',
  rent: () => '#/rent',
  merchants: () => '#/town',
  studio: () => '#/studio',
};
