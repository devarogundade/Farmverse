import { sprite } from '@farmverse/shared/gameTheme';
import { ChevronRight, Rotate3d } from 'lucide-react';
import { useState } from 'react';
import { floorPrice, getToken, related } from '../api/market';
import { NftCard } from '../components/NftCard';
import { AccountAvatar, EventPill, FRAME, FarmIcon, Sparkline, fmt, timeAgo } from '../components/ui';
import { CHARACTER_CLIPS, OUTFIT_SLOTS, SLOT_LABEL, TYPE_LABEL, displayNo, modelLabel, partLabel, traitLabel } from '@farmverse/shared/nft/metadata';
import { href } from '../router';
import { NftStage } from '@farmverse/shared/three/NftStage';

const CLIP_LABEL: Record<string, string> = {
  Idle_Relaxed: 'Idle', Idle_Look_Around: 'Look', Walk_Forward: 'Walk', Run_Forward: 'Run', Attack_Punch: 'Punch', Jump_Start: 'Jump',
};

export function TokenPage({ id }: { id: string }) {
  const token = getToken(id);
  const [clip, setClip] = useState('Idle_Relaxed');
  const [saved, setSaved] = useState(false);
  const [showDetails, setShowDetails] = useState(false);

  if (!token) {
    return (
      <div className="empty">
        <h2 className="fv-title">Item not found</h2>
        <p>
          This item may have been used in the game or taken off the market. <a href={href.explore()}>Back to the market</a>
        </p>
      </div>
    );
  }

  const m = token.metadata;
  const hist = token.priceHistory;
  // 7-day change of the daily average price.
  const weekAgo = hist[Math.max(0, hist.length - 8)];
  const change = weekAgo ? ((hist[hist.length - 1] - weekAgo) / weekAgo) * 100 : 0;
  const floor = floorPrice(token);
  const current = token.price ?? hist[hist.length - 1];
  const barMax = Math.max(current, floor ?? 0) * 1.15 || 1;
  const traits = Object.entries(m.props ?? {});

  return (
    <>
      <nav className="crumbs" aria-label="Breadcrumb">
        <img className="fv-icon crumb-icon" src={sprite('Icon_Shop')} alt="" />
        <a href={href.explore()}>Market</a>
        <span>—</span>
        <a href={href.explore(m.type)}>{TYPE_LABEL[m.type]}</a>
        <span>—</span>
        <b>#{displayNo(token.token_id)}</b>
      </nav>

      <div className="detail">
        <section className={`hero fv-frame lg ${FRAME[m.type]}`} aria-label={`${token.title} 3D model`}>
          <NftStage meta={m} className="hero-stage" framing="hero" interactive clip={clip} />
          <header className="hero-head">
            <h1 className="fv-title">{token.title}</h1>
            <span className="fv-title">#{displayNo(token.token_id)}</span>
          </header>
          <div className="hero-side">
            {m.quantity != null && <span className="qty-badge static">×{fmt(m.quantity)}</span>}
            <span className="hint">
              <Rotate3d size={14} /> Drag to rotate
            </span>
          </div>
          {m.type === 'worker' && (
            <div className="clip-row" role="group" aria-label="Animation">
              {CHARACTER_CLIPS.map((c) => (
                <button key={c} className={c === clip ? 'on' : ''} onClick={() => setClip(c)}>
                  {CLIP_LABEL[c] ?? c}
                </button>
              ))}
            </div>
          )}
          <div className="hero-actions">
            <button className="fv-btn green" disabled={token.price == null}>
              {token.price == null ? 'Not listed' : 'Buy now'}
            </button>
            <button className="fv-btn blue">Make offer</button>
            <button className={`fv-chip save${saved ? ' on' : ''}`} aria-pressed={saved} aria-label="Save" onClick={() => setSaved((s) => !s)}>
              <img className="fv-icon" src={sprite('Icon_Heart')} alt="" />
            </button>
          </div>
        </section>

        <div className="side">
          <section className="card fv-panel price-card">
            <h2 className="fv-title">Current price</h2>
            <div className="price-big">
              <FarmIcon size={34} />
              <strong className="fv-title fv-gold">{fmt(current)}</strong>
              <span className="price-unit">
                <em className={change >= 0 ? 'up' : 'down'}>
                  {Math.abs(change).toFixed(0)}% {change >= 0 ? '↑' : '↓'}
                </em>
                FARM
              </span>
            </div>
            <Sparkline values={hist} />
          </section>

          <section className="card fv-panel floor-card">
            <div className="card-head">
              <h2 className="fv-title">Floor price</h2>
              <a href={href.explore(m.type)} className="more">
                View More <ChevronRight size={14} />
              </a>
            </div>
            <div className="bar-row">
              <div className="bar-label">
                <span>Floor price</span>
                <b>{floor != null ? fmt(floor) : '—'}</b>
              </div>
              <div className="bar">
                <i className="orange" style={{ width: `${((floor ?? 0) / barMax) * 100}%` }} />
              </div>
            </div>
            <div className="bar-row">
              <div className="bar-label">
                <span>{token.price != null ? 'Listed at' : 'Last sale'}</span>
                <b>{fmt(current)}</b>
              </div>
              <div className="bar">
                <i className="purple" style={{ width: `${(current / barMax) * 100}%` }} />
              </div>
            </div>
          </section>

          <section className="card fv-panel history-card">
            <div className="card-head">
              <h2 className="fv-title">{showDetails ? 'Details' : 'History'}</h2>
              <button className="more" onClick={() => setShowDetails((s) => !s)}>
                {showDetails ? 'History' : 'Details'} <ChevronRight size={14} />
              </button>
            </div>
            {showDetails ? (
              <dl className="traits">
                <div>
                  <dt>Kind</dt>
                  <dd>{modelLabel(m)}</dd>
                </div>
                {m.quantity != null && (
                  <div>
                    <dt>Amount</dt>
                    <dd>{fmt(m.quantity)}</dd>
                  </div>
                )}
                {m.type === 'worker' &&
                  OUTFIT_SLOTS.filter((s) => m.outfit?.[s]).map((s) => (
                    <div key={s}>
                      <dt>{SLOT_LABEL[s]}</dt>
                      <dd>{partLabel(m.outfit![s]!)}</dd>
                    </div>
                  ))}
                {traits.map(([k, v]) => (
                  <div key={k}>
                    <dt>{traitLabel(k)}</dt>
                    <dd>{typeof v === 'string' ? traitLabel(v) : String(v)}</dd>
                  </div>
                ))}
                <div>
                  <dt>From the island of</dt>
                  <dd>{m.minted_from}</dd>
                </div>
              </dl>
            ) : (
              <ul className="history">
                {token.events.slice(0, 4).map((e, i) => (
                  <li key={i} className="fv-row">
                    <span className="who">
                      <AccountAvatar account={e.account} />
                      {e.account}
                    </span>
                    <EventPill kind={e.kind} />
                    <span className="amt">{e.price != null ? `${fmt(e.price)} FARM` : '—'}</span>
                    <span className="ago">{timeAgo(e.at)}</span>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <div className="related">
            {related(token).map((t) => (
              <NftCard key={t.token_id} token={t} showPrice={false} />
            ))}
          </div>
        </div>
      </div>
    </>
  );
}
