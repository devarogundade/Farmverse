import { shortAccount } from '@farmverse/shared/account';
import { THEMES, sprite, useGameTheme } from '@farmverse/shared/gameTheme';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { href, type Route } from '../router';
import { useSession } from '../session';
import { Avatar } from './ui';

const NAV = [
  { key: 'dashboard', icon: 'Icon_Trophy', label: 'Dashboard', to: href.dashboard() },
  { key: 'players', icon: 'Icon_Friends', label: 'Players', to: href.players() },
  { key: 'coupons', icon: 'Icon_Gift', label: 'Coupons', to: href.coupons() },
  { key: 'news', icon: 'Icon_Megaphone', label: 'News', to: href.news() },
  { key: 'settings', icon: 'Icon_Hammer', label: 'Game', to: href.settings() },
  { key: 'activity', icon: 'Icon_Quest', label: 'Activity', to: href.activity() },
] as const;

const activeKey = (r: Route) => (r.name === 'player' ? 'players' : r.name);

/** A small popover anchored to the rail (theme) or the top bar (account). */
function usePopover() {
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
  return { open, setOpen, box };
}

function ThemePicker() {
  const [theme, setTheme] = useGameTheme();
  const { open, setOpen, box } = usePopover();
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

function AccountMenu() {
  const { accountId, signOut } = useSession();
  const { open, setOpen, box } = usePopover();
  if (!accountId) return null;
  return (
    <div className="account" ref={box}>
      <button className="account-btn" aria-expanded={open} onClick={() => setOpen((o) => !o)} title={accountId}>
        <Avatar seed={accountId} size={32} />
        <span className="account-name">{shortAccount(accountId)}</span>
      </button>
      {open && (
        <div className="account-pop fv-panel" role="dialog" aria-label="Account">
          <p className="muted">Signed in as</p>
          <b title={accountId}>{shortAccount(accountId)}</b>
          <button className="fv-btn gray sm" onClick={() => void signOut()}>
            Sign out
          </button>
        </div>
      )}
    </div>
  );
}

export function Shell({ route, children }: { route: Route; children: ReactNode }) {
  const active = activeKey(route);
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
        <a className="wordmark" href={href.dashboard()} aria-label="Farmverse admin">
          <img src={sprite('Home_Logo')} alt="Farmverse" />
          <span className="badge">Admin</span>
        </a>
        <AccountMenu />
      </header>
      <main className="content">{children}</main>
    </div>
  );
}
