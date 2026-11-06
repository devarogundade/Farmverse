import { sprite, useGameTheme } from '@farmverse/shared/gameTheme';
import { SharedCanvas } from '@farmverse/shared/three/NftStage';
import { useEffect, useState } from 'react';
import { Home } from './Home';
import { LEGAL_PAGES, Legal } from './Legal';
import { MARKET_URL, PLAY_URL } from './links';

/** "#/terms" -> "terms"; section anchors ("#how") stay on the home page. */
function usePage() {
  const read = () => {
    const m = location.hash.match(/^#\/(\w+)/);
    return m && LEGAL_PAGES.includes(m[1]) ? m[1] : 'home';
  };
  const [page, setPage] = useState(read);
  useEffect(() => {
    const on = () => {
      const next = read();
      setPage((prev) => {
        if (prev !== next) window.scrollTo({ top: 0 });
        return next;
      });
    };
    window.addEventListener('hashchange', on);
    return () => window.removeEventListener('hashchange', on);
  }, []);
  return page;
}

export function App() {
  useGameTheme();
  const page = usePage();
  const anchor = (id: string) => (page === 'home' ? `#${id}` : `#/`);
  return (
    <>
      <header className="nav">
        <a href="#/" className="nav-logo" aria-label="Farmverse home">
          <img src={sprite('Home_Logo')} alt="Farmverse" />
        </a>
        <nav className="nav-links" aria-label="Sections">
          <a href={anchor('how')}>How it works</a>
          <a href={anchor('workers')}>Workers</a>
          <a href={anchor('farm')}>FARM</a>
          <a href={anchor('faq')}>FAQ</a>
        </nav>
        <div className="nav-ctas">
          <a className="fv-btn blue sm" href={MARKET_URL}>
            Market
          </a>
          <a className="fv-btn green sm" href={PLAY_URL}>
            Play
          </a>
        </div>
      </header>

      <main>{page === 'home' ? <Home /> : <Legal page={page} />}</main>

      <footer className="footer">
        <img src={sprite('Home_Logo')} alt="Farmverse" />
        <nav aria-label="Footer">
          <a href={PLAY_URL}>Play</a>
          <a href={MARKET_URL}>Market</a>
          <a href="#/terms">Terms</a>
          <a href="#/privacy">Privacy</a>
          <a href="#/farm">FARM disclosure</a>
        </nav>
        <small>© 2026 Farmverse</small>
      </footer>

      <SharedCanvas />
    </>
  );
}
