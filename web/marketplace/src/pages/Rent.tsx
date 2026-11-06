import { NftStage } from '@farmverse/shared/three/NftStage';
import { useState } from 'react';
import { useMyRentals, useRentals } from '../api/queries';
import type { Rental } from '../api/types';
import { useWallet } from '../chain/wallet';
import { useFlow } from '../components/Flows';
import { EmptyState, ErrorState, SkeletonGrid } from '../components/states';
import { fmt, frameOf, itemName } from '../components/ui';
import { href } from '../router';

function RentalCard({ rental: r, mode }: { rental: Rental; mode: 'hire' | 'lent' | 'renting' }) {
  const open = useFlow();
  const { accountId } = useWallet();
  const [live, setLive] = useState(false);
  const [now] = useState(() => Date.now());
  const m = r.token.metadata;
  const busy = r.rented_until > now;
  const days = busy ? Math.ceil((r.rented_until - now) / 86_400_000) : 0;
  const props = m.props ?? {};
  const level = typeof props.level === 'number' ? props.level : undefined;
  const mineToLend = r.lender_id === accountId;
  return (
    <article
      className={`nft-card rental-card fv-frame ${frameOf(m)}`}
      onPointerEnter={() => setLive(true)}
      onPointerLeave={() => setLive(false)}
      onFocus={() => setLive(true)}
      onBlur={() => setLive(false)}
    >
      <a href={href.token(r.token.token_id)} className="rental-stage-link" aria-label={r.token.title}>
        <NftStage meta={m} className="nft-card-stage" animate={live} />
      </a>
      {typeof props.rarity === 'string' && <span className={`card-ribbon fv-title rarity-${props.rarity}`}>{props.rarity}</span>}
      <div className="nft-card-foot rental-foot">
        <div className="nft-card-text">
          <strong className="fv-title">{itemName(r.token)}</strong>
          <small className="muted">
            {typeof props.specialty === 'string' ? `Best at ${props.specialty}` : 'Farmhand'}
            {level ? ` · level ${level}` : ''}
          </small>
          <small>
            <b>{fmt(r.price_per_day)} FARM</b> a day · up to {r.max_days} days
          </small>
        </div>
        {mode === 'hire' &&
          (mineToLend ? (
            <button className="fv-btn blue sm" onClick={() => open({ kind: 'rentManage', rental: r })}>
              Manage
            </button>
          ) : (
            <button className="fv-btn green sm" disabled={busy} onClick={() => open({ kind: 'rent', rental: r })}>
              {busy ? `Busy · ${days}d` : 'Hire'}
            </button>
          ))}
        {mode === 'lent' && (
          <button className="fv-btn blue sm" onClick={() => open({ kind: 'rentManage', rental: r })}>
            {r.earned > 0 ? `Collect ${fmt(r.earned)}` : 'Manage'}
          </button>
        )}
        {mode === 'renting' && <span className="pill">On your farm · {days}d left</span>}
      </div>
    </article>
  );
}

/** Hire a Farmhand for a few days, or see the ones you lent out (plan 15 §12.1). */
export function Rent() {
  const all = useRentals();
  const mine = useMyRentals();
  const { accountId } = useWallet();
  const [tab, setTab] = useState<'hire' | 'mine'>('hire');
  const lent = mine.data?.lent ?? [];
  const renting = mine.data?.renting ?? [];
  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">Hire a Farmhand</h1>
          <p className="muted">Farmhands work on your farm for the days you hire them. Their owners earn the FARM.</p>
        </div>
        {accountId && (
          <div className="seg shelf" role="tablist" aria-label="Show">
            <button role="tab" aria-selected={tab === 'hire'} className={tab === 'hire' ? 'on' : ''} onClick={() => setTab('hire')}>
              For hire
            </button>
            <button role="tab" aria-selected={tab === 'mine'} className={tab === 'mine' ? 'on' : ''} onClick={() => setTab('mine')}>
              Mine{lent.length + renting.length ? ` · ${lent.length + renting.length}` : ''}
            </button>
          </div>
        )}
      </div>

      {tab === 'hire' ? (
        all.isPending ? (
          <SkeletonGrid />
        ) : all.error ? (
          <ErrorState title="Couldn’t load Farmhands for hire" error={all.error} onRetry={() => all.refetch()} />
        ) : !all.data?.length ? (
          <EmptyState art="Isl_p_worker" title="Nobody for hire right now">
            <p className="muted">Own a Farmhand? Open it and choose Rent out.</p>
            <a className="fv-btn green" href={href.explore('worker')}>
              See Farmhands for sale
            </a>
          </EmptyState>
        ) : (
          <div className="grid">
            {all.data.map((r) => (
              <RentalCard key={r.token.token_id} rental={r} mode="hire" />
            ))}
          </div>
        )
      ) : mine.isPending ? (
        <SkeletonGrid />
      ) : mine.error ? (
        <ErrorState title="Couldn’t load your Farmhands" error={mine.error} onRetry={() => mine.refetch()} />
      ) : lent.length + renting.length === 0 ? (
        <EmptyState art="Isl_p_worker" title="No hires yet">
          <p className="muted">Workers you rent out, and the ones you hire, show up here.</p>
        </EmptyState>
      ) : (
        <>
          {renting.length > 0 && <h2 className="fv-title section-title">Working for you</h2>}
          <div className="grid">
            {renting.map((r) => (
              <RentalCard key={r.token.token_id} rental={r} mode="renting" />
            ))}
          </div>
          {lent.length > 0 && <h2 className="fv-title section-title">Yours, for hire</h2>}
          <div className="grid">
            {lent.map((r) => (
              <RentalCard key={r.token.token_id} rental={r} mode="lent" />
            ))}
          </div>
        </>
      )}
    </>
  );
}
