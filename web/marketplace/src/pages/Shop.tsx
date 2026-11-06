import { sprite } from '@farmverse/shared/gameTheme';
import { flatArt } from '@farmverse/shared/nft/metadata';
import { useState } from 'react';
import { useDrops } from '../api/queries';
import type { Drop } from '../api/types';
import { useFlow } from '../components/Flows';
import { EmptyState, ErrorState, SkeletonGrid } from '../components/states';
import { Price, fmt } from '../components/ui';
import { gameLink } from '../config';
import { href } from '../router';

/** What each drop gives, in plain words. */
function about(d: Drop): string {
  if (d.item_id === 'farm_pass') return 'A season of premium Farm Pass rewards and longer offline work.';
  if (d.item_id === 'builder_pass') return 'One more builder on your farm, for good.';
  if (d.item_id === 'starter_pack') return 'Coins, gems and a shield to get going.';
  if (/^gems_\d+$/.test(d.item_id)) return 'Gems for the in-game shop.';
  if (d.item_id === 'deed') return 'Clears a wild plot on your island for good. Only so many will ever exist.';
  return 'From the Farmverse shop.';
}

function timeLeft(endsAt: number, now: number): string {
  const h = Math.max(0, Math.round((endsAt - now) / 3_600_000));
  return h >= 48 ? `${Math.round(h / 24)} days left` : `${h} hours left`;
}

function DropCard({ drop: d }: { drop: Drop }) {
  const open = useFlow();
  const [now] = useState(() => Date.now());
  const art = flatArt({ type: d.kind === 'land' ? 'deed' : 'pack', item_id: d.item_id }) ?? 'Icon_Gift';
  const left = d.cap != null ? Math.max(0, d.cap - d.sold) : undefined;
  const soldOut = left === 0;
  const notYet = d.starts_at > now;
  const ended = d.ends_at != null && d.ends_at <= now;
  const blocked = soldOut || notYet || ended || d.paused;
  return (
    <article className={`drop-card fv-frame ${d.kind === 'land' ? 'green' : 'blue'}${blocked ? ' off' : ''}`}>
      <span className="flat-art drop-art">
        <img src={sprite(art)} alt="" />
      </span>
      {left != null && <span className="card-ribbon fv-title">{soldOut ? 'Sold out' : `${fmt(left)} left`}</span>}
      <div className="drop-body">
        <h2 className="fv-title">{d.title}</h2>
        <p className="muted">{about(d)}</p>
        {d.cap != null && (
          <div className="drop-meter" aria-label={`${d.sold} of ${d.cap} sold`}>
            <i style={{ width: `${Math.min(100, (d.sold / d.cap) * 100)}%` }} />
          </div>
        )}
        <div className="drop-foot">
          <Price value={d.price} size="lg" />
          {d.ends_at != null && !ended && <small className="muted">{timeLeft(d.ends_at, now)}</small>}
        </div>
        {soldOut ? (
          <a className="fv-btn gray" href={href.explore(d.kind === 'land' ? 'deed' : 'pack')}>
            Find one for sale
          </a>
        ) : (
          <button className="fv-btn green" disabled={blocked} onClick={() => open({ kind: 'drop', drop: d })}>
            {notYet ? 'Coming soon' : ended ? 'Ended' : d.paused ? 'Paused' : `Buy for ${fmt(d.price)}`}
          </button>
        )}
        {d.per_wallet != null && <small className="muted">Up to {d.per_wallet} each</small>}
      </div>
    </article>
  );
}

/** The studio's own shop: passes, packs and land deeds for FARM (plan 15 §5.4). */
export function Shop() {
  const q = useDrops();
  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">Shop</h1>
          <p className="muted">
            Passes, gem packs and land deeds, straight from Farmverse. Use them in the game, or{' '}
            <a href={gameLink.nfts()} target="_blank" rel="noreferrer">
              buy right in the game
            </a>
            .
          </p>
        </div>
      </div>
      {q.isPending ? (
        <SkeletonGrid />
      ) : q.error ? (
        <ErrorState title="Couldn’t load the shop" error={q.error} onRetry={() => q.refetch()} />
      ) : !q.data?.length ? (
        <EmptyState art="Icon_Gift" title="The shop opens soon">
          <p className="muted">Check back shortly.</p>
        </EmptyState>
      ) : (
        <div className="grid drops">
          {q.data.map((d) => (
            <DropCard key={d.id} drop={d} />
          ))}
        </div>
      )}
    </>
  );
}
