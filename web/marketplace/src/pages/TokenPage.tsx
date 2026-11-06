import { sprite } from '@farmverse/shared/gameTheme';
import { CHARACTER_CLIPS, OUTFIT_SLOTS, SLOT_LABEL, TYPE_LABEL, displayNo, modelLabel, partLabel, traitLabel } from '@farmverse/shared/nft/metadata';
import { NftStage } from '@farmverse/shared/three/NftStage';
import * as Tabs from '@radix-ui/react-tabs';
import { ChevronRight, Rotate3d } from 'lucide-react';
import { useState } from 'react';
import { useOffers, useToken } from '../api/queries';
import type { Offer, Token } from '../api/types';
import { useWallet } from '../chain/wallet';
import { toast } from '../components/Dialog';
import { useFlow } from '../components/Flows';
import { NftCard } from '../components/NftCard';
import { EmptyState, ErrorState, SkeletonDetail } from '../components/states';
import { AccountAvatar, EventPill, FRAME, FarmIcon, Sparkline, fmt, timeAgo } from '../components/ui';
import { toggleSaved, useSaved } from '../lib/saved';
import { href } from '../router';

const CLIP_LABEL: Record<string, string> = {
  Idle_Relaxed: 'Idle', Idle_Look_Around: 'Look', Walk_Forward: 'Walk', Run_Forward: 'Run', Attack_Punch: 'Punch', Jump_Start: 'Jump',
};

export function TokenPage({ id }: { id: string }) {
  const q = useToken(id);
  if (q.isPending) return <SkeletonDetail />;
  if (q.error) return <ErrorState title="Couldn’t load this item" error={q.error} onRetry={() => q.refetch()} />;
  if (!q.data) {
    return (
      <EmptyState title="Item not found">
        <p className="muted">It may have been used in the game.</p>
        <a className="fv-btn green" href={href.explore()}>
          Back to the market
        </a>
      </EmptyState>
    );
  }
  return <TokenView token={q.data.token} floor={q.data.floor} related={q.data.related} />;
}

function TokenView({ token, floor, related }: { token: Token; floor?: number; related: Token[] }) {
  const id = token.token_id;
  const [clip, setClip] = useState('Idle_Relaxed');
  const open = useFlow();
  const { accountId } = useWallet();
  const saved = useSaved().includes(id);
  const offersQ = useOffers(id);
  const offers = (offersQ.data ?? []).filter((o) => o.status === 'open');

  const m = token.metadata;
  const hist = token.priceHistory;
  // 7-day change of the daily average price.
  const weekAgo = hist[Math.max(0, hist.length - 8)];
  const change = weekAgo ? ((hist[hist.length - 1] - weekAgo) / weekAgo) * 100 : 0;
  const current = token.price ?? hist[hist.length - 1] ?? 0;
  const barMax = Math.max(current, floor ?? 0) * 1.15 || 1;
  const traits = Object.entries(m.props ?? {});
  const mine = !!accountId && token.owner_id === accountId;
  const listed = token.price != null;
  const worker = m.type === 'worker';
  const myOffer = offers.find((o) => o.from === accountId);
  const visibleOffers = mine ? offers : offers.filter((o) => o.from === accountId);

  return (
    <>
      <nav className="crumbs" aria-label="Breadcrumb">
        <img className="fv-icon crumb-icon" src={sprite('Icon_Shop')} alt="" />
        <a href={href.explore()}>Market</a>
        <span>—</span>
        <a href={href.explore(m.type)}>{TYPE_LABEL[m.type]}</a>
        <span>—</span>
        <b>#{displayNo(id)}</b>
      </nav>

      <div className="detail">
        <section className={`hero fv-frame lg ${FRAME[m.type]}`} aria-label={`${token.title} 3D model`}>
          <NftStage meta={m} className="hero-stage" framing="hero" interactive clip={clip} />
          <header className="hero-head">
            <h1 className="fv-title">{token.title}</h1>
            <span className="fv-title">#{displayNo(id)}</span>
          </header>
          <div className="hero-side">
            {m.quantity != null && <span className="qty-badge static">×{fmt(m.quantity)}</span>}
            <span className="hint">
              <Rotate3d size={14} /> Drag to rotate
            </span>
          </div>
          {worker && (
            <div className="clip-row" role="group" aria-label="Animation">
              {CHARACTER_CLIPS.map((c) => (
                <button key={c} className={c === clip ? 'on' : ''} onClick={() => setClip(c)}>
                  {CLIP_LABEL[c] ?? c}
                </button>
              ))}
            </div>
          )}
          <div className="hero-actions">
            {mine && listed && (
              <>
                <button className="fv-btn green" onClick={() => open({ kind: 'list', token })}>
                  Change price
                </button>
                <button className="fv-btn yellow" onClick={() => open({ kind: 'delist', token })}>
                  Take down
                </button>
              </>
            )}
            {mine && !listed && (
              <>
                <button className="fv-btn green" onClick={() => open({ kind: 'list', token })}>
                  Sell
                </button>
                {/* Workers are held, not redeemed (items contract): they work on the holder's island. */}
                {!worker && (
                  <button className="fv-btn yellow" onClick={() => open({ kind: 'redeem', token })}>
                    Use in game
                  </button>
                )}
                <button className="fv-chip save" aria-label="Send to a friend" title="Send to a friend" onClick={() => open({ kind: 'send', token })}>
                  <img className="fv-icon" src={sprite('Icon_Gift')} alt="" />
                </button>
              </>
            )}
            {!mine && (
              <>
                {listed && (
                  <button className="fv-btn green" onClick={() => open({ kind: 'buy', token })}>
                    Buy now
                  </button>
                )}
                <button className="fv-btn blue" onClick={() => open({ kind: 'offer', token })}>
                  {myOffer ? 'New offer' : 'Make offer'}
                </button>
                <button
                  className={`fv-chip save${saved ? ' on' : ''}`}
                  aria-pressed={saved}
                  aria-label="Save"
                  onClick={() => toast(toggleSaved(id) ? 'Saved' : 'Removed from saved', 'Icon_Heart')}
                >
                  <img className="fv-icon" src={sprite('Icon_Heart')} alt="" />
                </button>
              </>
            )}
          </div>
          {mine && worker && !listed && <p className="hero-note">Working on your island while you hold them.</p>}
        </section>

        <div className="side">
          <section className="card fv-panel price-card">
            <h2 className="fv-title">{listed ? 'Current price' : 'Last price'}</h2>
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
            {hist.length > 1 && <Sparkline values={hist} />}
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
                <span>{listed ? 'Listed at' : 'Last sale'}</span>
                <b>{fmt(current)}</b>
              </div>
              <div className="bar">
                <i className="purple" style={{ width: `${(current / barMax) * 100}%` }} />
              </div>
            </div>
          </section>

          <Tabs.Root className="card fv-panel history-card" defaultValue="history">
            <Tabs.List className="card-head tabs" aria-label="About this item">
              <Tabs.Trigger value="history" className="tab fv-title">
                History
              </Tabs.Trigger>
              <Tabs.Trigger value="offers" className="tab fv-title">
                Offers
                {visibleOffers.length > 0 && <i className="count">{visibleOffers.length}</i>}
              </Tabs.Trigger>
              <Tabs.Trigger value="details" className="tab fv-title">
                Details
              </Tabs.Trigger>
            </Tabs.List>

            <Tabs.Content value="history">
              {token.events.length === 0 ? (
                <p className="muted tab-empty">No history yet.</p>
              ) : (
                <ul className="history">
                  {token.events.slice(0, 6).map((e, i) => (
                    <li key={i} className="fv-row">
                      <Who account={e.account} sub={timeAgo(e.at)} />
                      <EventPill kind={e.kind} />
                      <span className="amt">{e.price != null ? `${fmt(e.price)} FARM` : '—'}</span>
                      <span className="ago">{timeAgo(e.at)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </Tabs.Content>

            <Tabs.Content value="offers">
              <OffersList offers={visibleOffers} mine={mine} me={accountId} loading={offersQ.isPending} error={offersQ.error} onRetry={() => offersQ.refetch()} token={token} />
            </Tabs.Content>

            <Tabs.Content value="details">
              <dl className="traits">
                <div>
                  <dt>Owner</dt>
                  <dd>{mine ? 'You' : token.owner_id}</dd>
                </div>
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
                {worker &&
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
            </Tabs.Content>
          </Tabs.Root>

          {related.length > 0 && (
            <section className="related-wrap" aria-labelledby="similar-title">
              <div className="card-head">
                <h2 id="similar-title" className="fv-title related-title">
                  Similar items
                </h2>
                <a href={href.explore(m.type)} className="more">
                  See all <ChevronRight size={14} />
                </a>
              </div>
              <div className="related">
                {related.map((t) => (
                  <NftCard key={t.token_id} token={t} />
                ))}
              </div>
            </section>
          )}
        </div>
      </div>
    </>
  );
}

function Who({ account, sub, label }: { account: string; sub?: string; label?: string }) {
  return (
    <span className="who">
      <span className="fv-row-ava">
        <AccountAvatar account={account} />
      </span>
      <span className="who-text">
        <span className="who-name">{label ?? account}</span>
        {/* Phones: the time moves under the name. */}
        {sub && <small className="ago-sub">{sub}</small>}
      </span>
    </span>
  );
}

function OffersList(props: { offers: Offer[]; mine: boolean; me: string | null; loading: boolean; error: unknown; onRetry: () => void; token: Token }) {
  const open = useFlow();
  const { offers, mine, me, token } = props;
  const daysLeft = (at: number) => Math.max(1, Math.ceil((at - Date.now()) / 86_400_000));
  if (props.loading) return <p className="muted tab-empty">Loading offers…</p>;
  if (props.error)
    return (
      <div className="tab-empty">
        <p className="dlg-err">{(props.error as Error).message}</p>
        <button className="fv-btn gray sm" onClick={props.onRetry}>
          Try again
        </button>
      </div>
    );
  if (offers.length === 0)
    return (
      <div className="tab-empty">
        <img src={sprite('Icon_Mail')} alt="" />
        <p>{mine ? 'No offers yet. You’ll get a notification when one comes in.' : 'You haven’t made an offer on this item.'}</p>
        {!mine && (
          <button className="fv-btn blue sm" onClick={() => open({ kind: 'offer', token })}>
            Make offer
          </button>
        )}
      </div>
    );
  return (
    <ul className="history">
      {offers.map((o) => (
        <li key={o.id} className="fv-row offer-row">
          <span className="who">
            <span className="fv-row-ava">
              <AccountAvatar account={o.from} />
            </span>
            <span className="who-text">
              <span className="who-name">{o.from === me ? 'Your offer' : o.from}</span>
              <small className="ends">ends in {daysLeft(o.expires_at)}d</small>
            </span>
          </span>
          <span className="amt">{fmt(o.amount)} FARM</span>
          <span className="offer-btns">
            {mine ? (
              <>
                <button className="fv-btn green sm" onClick={() => open({ kind: 'accept', offer: o, token })}>
                  Accept
                </button>
                <button className="fv-btn gray sm" onClick={() => open({ kind: 'decline', offer: o, token })}>
                  Decline
                </button>
              </>
            ) : (
              <button className="fv-btn gray sm" onClick={() => open({ kind: 'cancelOffer', offer: o, token })}>
                Cancel
              </button>
            )}
          </span>
        </li>
      ))}
    </ul>
  );
}
