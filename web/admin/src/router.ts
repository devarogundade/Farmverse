import { useEffect, useState } from 'react';

/** Hash routes (the site is static files; no server routing needed). */
export type Route =
  | { name: 'dashboard' }
  | { name: 'players'; q: string; status: string; sort: string; page: number }
  | { name: 'player'; uid: string }
  | { name: 'coupons'; tab: 'codes' | 'tasks' | 'types'; q: string; status: string; typeId: string; page: number }
  | { name: 'news' }
  | { name: 'settings' }
  | { name: 'activity'; targetType: string };

const num = (v: string | null) => Math.max(0, Number.parseInt(v ?? '0', 10) || 0);

export function parse(hash: string): Route {
  const [path = '', qs = ''] = hash.replace(/^#\/?/, '').split('?');
  const p = new URLSearchParams(qs);
  const [a, b] = path.split('/');
  switch (a) {
    case 'players':
      return b ? { name: 'player', uid: b } : { name: 'players', q: p.get('q') ?? '', status: p.get('status') ?? 'all', sort: p.get('sort') ?? 'newest', page: num(p.get('page')) };
    case 'coupons': {
      const tab = b === 'tasks' || b === 'types' ? b : 'codes';
      return { name: 'coupons', tab, q: p.get('q') ?? '', status: p.get('status') ?? 'all', typeId: p.get('type') ?? '', page: num(p.get('page')) };
    }
    case 'news':
      return { name: 'news' };
    case 'settings':
      return { name: 'settings' };
    case 'activity':
      return { name: 'activity', targetType: p.get('about') ?? '' };
    default:
      return { name: 'dashboard' };
  }
}

const qs = (o: Record<string, string | number | undefined>) => {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(o)) if (v !== undefined && v !== '' && v !== 0 && v !== 'all' && !(k === 'sort' && v === 'newest')) p.set(k, String(v));
  return p.size ? `?${p}` : '';
};

export const href = {
  dashboard: () => '#/',
  players: (o: { q?: string; status?: string; sort?: string; page?: number } = {}) => `#/players${qs(o)}`,
  player: (uid: string) => `#/players/${uid}`,
  coupons: (tab: 'codes' | 'tasks' | 'types' = 'codes', o: { q?: string; status?: string; type?: string; page?: number } = {}) =>
    `#/coupons${tab === 'codes' ? '' : `/${tab}`}${qs(o)}`,
  news: () => '#/news',
  settings: () => '#/settings',
  activity: (about = '') => `#/activity${qs({ about })}`,
};

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
