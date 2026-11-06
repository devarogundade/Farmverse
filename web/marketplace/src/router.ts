import { useEffect, useState } from 'react';

export type Route =
  | { name: 'explore'; type?: string; mine?: boolean }
  | { name: 'token'; id: string }
  | { name: 'studio' };

function parse(hash: string): Route {
  const [path, query = ''] = hash.replace(/^#/, '').split('?');
  const parts = path.split('/').filter(Boolean);
  const params = new URLSearchParams(query);
  if (parts[0] === 'token' && parts[1]) return { name: 'token', id: decodeURIComponent(parts[1]) };
  if (parts[0] === 'studio') return { name: 'studio' };
  return { name: 'explore', type: params.get('type') ?? undefined, mine: parts[0] === 'mine' };
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
  mine: (type?: string) => (type ? `#/mine?type=${type}` : '#/mine'),
  token: (id: string) => `#/token/${encodeURIComponent(id)}`,
  studio: () => '#/studio',
};
