import { THEMES, sprite, useGameTheme } from '@farmverse/shared/gameTheme';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useBalance, useNotes } from '../api/queries';
import { useWallet } from '../chain/wallet';
import { config } from '../config';
import { useLiveStatus } from '../realtime/socket';
import { useFlow } from './Flows';
import { href, type Route } from '../router';
import { AccountAvatar, fmt } from './ui';

const NAV = [
  { key: 'home', icon: 'Icon_Shop', label: 'Explore', to: href.explore() },
  // "Featured" is a token the studio picks (VITE_FEATURED_TOKEN); hidden without one.
  ...(config.featuredToken ? [{ key: 'token', icon: 'Icon_Crown', label: 'Featured', to: href.token(config.featuredToken) }] : []),
  { key: 'shop', icon: 'Icon_Gift', label: 'Shop', to: href.shop() },
  { key: 'rent', icon: 'Isl_p_worker', label: 'Hire', to: href.rent() },
  { key: 'town', icon: 'Icon_Shop', label: 'Town', to: href.merchants() },
  { key: 'mine', icon: 'Icon_MenuIcon02_Inventory', label: 'My items', to: href.mine() },
  { key: 'studio', icon: 'Icon_PictoIcon_Cloth', label: 'Wardrobe', to: href.studio() },
] as const;

function activeKey(route: Route) {
  if (route.name === 'token') return 'token';
  if (route.name === 'studio') return 'studio';
  if (route.name === 'shop') return 'shop';
  if (route.name === 'rent') return 'rent';
  if (route.name === 'merchants') return 'town';
  if (route.name === 'buyfarm') return 'shop';
  return route.mine ? 'mine' : 'home';
}

/** Settings: the game's four colour themes. */
function ThemePicker() {
  const [theme, setTheme] = useGameTheme();
  const [open, setOpen] = useState(false);
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => !box.current?.contains(e.target as Node) && setOpen(false);
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', esc);
    };
  }, [open]);
  return (
    <div className="theme-picker" ref={box}>
      <button className="rail-item" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <span className="fv-chip rail-btn">
          <img className="fv-icon" src={sprite('Icon_PictoIcon_Setting')} alt="" />
        </span>
        <span className="rail-label">Theme</span>
      </button>
      {open && (
        <div className="theme-pop fv-panel" role="dialog" aria-label="Theme">
          <h3 className="fv-title">Theme</h3>
          {THEMES.map((t) => (
            <button key={t.id} className={`theme-opt${t.id === theme ? ' on' : ''}`} aria-pressed={t.id === theme} onClick={() => setTheme(t.id)}>
              <i style={{ background: t.swatch }} />
              {t.name}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

export function Shell({ route, children, search, onSearch }: { route: Route; children: ReactNode; search: string; onSearch: (q: string) => void }) {
  const active = activeKey(route);
  const open = useFlow();
  const { accountId: account, ready } = useWallet();
  const { data: balance } = useBalance();
  const unread = (useNotes().data ?? []).filter((n) => !n.read).length;
  const live = useLiveStatus();
  return (
    <div className="frame">
      <aside className="rail">
        <nav className="rail-nav" aria-label="Main">
          {NAV.map(({ key, icon, label, to }) => (
            <a key={key} href={to} className={`rail-item${active === key ? ' on' : ''}`} aria-current={active === key ? 'page' : undefined}>
              <span className={`rail-btn${active === key ? ' active' : ' fv-chip'}`}>
                <img className="fv-icon" src={sprite(icon)} alt="" />
              </span>
              <span className="rail-label">{label}</span>
            </a>
          ))}
        </nav>
        <div className="rail-bottom">
          <ThemePicker />
        </div>
      </aside>

      <header className="topbar">
        <a className="wordmark" href={href.explore()} aria-label="Farmverse Market">
          <img src={sprite('Home_Logo')} alt="Farmverse" />
        </a>
        <form
          className="search"
          role="search"
          onSubmit={(e) => {
            e.preventDefault();
            if (route.name !== 'explore') location.hash = href.explore();
          }}
        >
          <input value={search} onChange={(e) => onSearch(e.target.value)} placeholder="Search workers, tractors, goods…" aria-label="Search" />
          <button type="submit" className="fv-chip" aria-label="Search">
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Search')} alt="" />
          </button>
        </form>
        <div className="top-actions">
          <button
            className={`fv-chip${unread && account ? ' has-dot' : ''}`}
            aria-label={unread && account ? `Notifications, ${unread} new` : 'Notifications'}
            onClick={() => open({ kind: 'notes' })}
          >
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Bell')} alt="" />
          </button>
          {!ready ? (
            <span className="wallet-btn sk-wallet" aria-label="Checking wallet" />
          ) : account ? (
            <button className="wallet-btn" title={account} onClick={() => open({ kind: 'wallet' })}>
              <img className="fv-icon" src={sprite('ResourceBar_Icon_Gold')} alt="" />
              <b className="wallet-bal-sm">{balance == null ? '…' : fmt(balance)}</b>
              <AccountAvatar account={account} size={30} />
              <i className={`live-dot ${live}`} title={live === 'live' ? 'Live updates on' : live === 'connecting' ? 'Connecting…' : 'Live updates paused'} />
            </button>
          ) : (
            <button className="fv-btn green sm" onClick={() => open({ kind: 'wallet' })}>
              Sign in
            </button>
          )}
        </div>
      </header>

      <main className="content">{children}</main>
    </div>
  );
}
