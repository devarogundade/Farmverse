import { THEMES, sprite, useGameTheme } from '@farmverse/shared/gameTheme';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ME } from '../data/mock';
import { href, type Route } from '../router';
import { AccountAvatar } from './ui';

const NAV = [
  { key: 'home', icon: 'Icon_Shop', label: 'Explore', to: href.explore() },
  { key: 'token', icon: 'Icon_Crown', label: 'Featured', to: href.token('w-1245') },
  { key: 'mine', icon: 'Icon_MenuIcon02_Inventory', label: 'My items', to: href.mine() },
  { key: 'studio', icon: 'Icon_PictoIcon_Cloth', label: 'Dressing room', to: href.studio() },
] as const;

function activeKey(route: Route) {
  if (route.name === 'token') return 'token';
  if (route.name === 'studio') return 'studio';
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
      <button className="fv-chip rail-btn" aria-label="Settings" aria-expanded={open} title="Settings" onClick={() => setOpen((o) => !o)}>
        <img className="fv-icon" src={sprite('Icon_PictoIcon_Setting')} alt="" />
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
  const mine = route.name === 'explore' && route.mine;
  return (
    <div className="frame">
      <aside className="rail">
        <a className="rail-logo" href={href.explore()} aria-label="Farmverse Market home">
          <img src={sprite('Paint_crop_wheat')} alt="" />
        </a>
        <nav className="rail-nav" aria-label="Main">
          {NAV.map(({ key, icon, label, to }) => (
            <a
              key={key}
              href={to}
              className={`rail-btn${active === key ? ' active' : ' fv-chip'}`}
              aria-label={label}
              aria-current={active === key ? 'page' : undefined}
              title={label}
            >
              <img className="fv-icon" src={sprite(icon)} alt="" />
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
          <span className="fv-title">Market</span>
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
        <div className="seg" role="tablist" aria-label="View">
          <a role="tab" aria-selected={!mine} className={!mine ? 'on' : ''} href={href.explore()}>
            Market
          </a>
          <a role="tab" aria-selected={mine} className={mine ? 'on' : ''} href={href.mine()}>
            My items
          </a>
        </div>
        <div className="top-actions">
          <button className="fv-chip has-dot" aria-label="Notifications">
            <img className="fv-icon" src={sprite('Icon_PictoIcon_Bell')} alt="" />
          </button>
          <button className="wallet-btn" title="Your wallet">
            <img className="fv-icon" src={sprite('ResourceBar_Icon_Gold')} alt="" />
            <span>{ME}</span>
            <AccountAvatar account={ME} size={30} />
          </button>
        </div>
      </header>

      <main className="content">{children}</main>
    </div>
  );
}
