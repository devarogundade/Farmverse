import { useStats } from '../api/admin';
import { BarChart } from '../components/BarChart';
import { Failed, fmt, Loading, PageHead, Stat, timeAgo } from '../components/ui';
import { href } from '../router';

export function Dashboard() {
  const { data: s, error, refetch, isLoading } = useStats();
  const head = (
    <PageHead icon="Icon_Trophy" title="Dashboard">
      {s ? `Updated ${timeAgo(s.at)}. Refreshes every minute.` : 'Refreshes every minute.'}
    </PageHead>
  );
  if (isLoading) return <>{head}<Loading /></>;
  if (error || !s) return <>{head}<Failed error={error} retry={() => void refetch()} /></>;

  const indexerLate = s.chain?.indexer ? Date.now() - s.chain.indexer.updatedAt > 10 * 60_000 : true;

  return (
    <>
      {head}

      <h2 className="section-title">Players</h2>
      <div className="stats">
        <a href={href.players()}>
          <Stat icon="Icon_Friends" label="Players" value={fmt(s.players.total)} sub={`+${fmt(s.players.new24h)} today · +${fmt(s.players.new7d)} this week`} />
        </a>
        <Stat icon="Icon_Heart" label="Played today" value={fmt(s.players.active24h)} sub={`${fmt(s.players.active7d)} this week · ${fmt(s.players.active30d)} this month`} />
        <a href={href.players({ status: 'banned' })}>
          <Stat icon="Icon_ImageIcon_Lock01_s" label="Banned" value={fmt(s.players.banned)} tone={s.players.banned ? 'pink' : undefined} />
        </a>
      </div>

      <h2 className="section-title">Coupons</h2>
      <div className="stats">
        <a href={href.coupons()}>
          <Stat icon="Icon_Gift" label="Codes given out" value={fmt(s.coupons.issued)} sub={`+${fmt(s.coupons.issued24h)} today`} />
        </a>
        <a href={href.coupons('codes', { status: 'open' })}>
          <Stat icon="Icon_Hourglass" label="Waiting to be redeemed" value={fmt(s.coupons.open)} tone="gold" />
        </a>
        <a href={href.coupons('codes', { status: 'redeemed' })}>
          <Stat icon="Icon_ImageIcon_ClearStamp_l" label="Redeemed" value={fmt(s.coupons.redeemed)} />
        </a>
        {s.coupons.byType.map((t) => (
          <a key={t.typeId} href={href.coupons('codes', { type: t.typeId })}>
            <Stat icon="Icon_Golds" label={`${t.unit} paid out`} value={fmt(t.amountRedeemed)} sub={`${fmt(t.amountIssued)} ${t.unit} given out in ${fmt(t.issued)} codes`} />
          </a>
        ))}
      </div>

      <h2 className="section-title">Marketplace</h2>
      {s.chain ? (
        <div className="stats">
          <Stat icon="Icon_Star" label="Items minted" value={fmt(s.chain.minted)} sub={`${fmt(s.chain.live)} still out · ${fmt(s.chain.redeemed)} redeemed`} />
          <Stat icon="Icon_Shop" label="For sale now" value={fmt(s.chain.listed)} />
          <Stat icon="Icon_Golds" label="Sales" value={fmt(s.chain.sales)} sub={`${fmt(s.chain.volume)} FARM in total · ${fmt(s.chain.volume7d)} this week`} />
          <Stat
            icon="Icon_Compass"
            label="Market data"
            value={s.chain.indexer ? timeAgo(s.chain.indexer.updatedAt) : 'not started'}
            sub={indexerLate ? 'Running late: let a developer know' : 'Up to date'}
            tone={indexerLate ? 'pink' : 'green'}
          />
        </div>
      ) : (
        <p className="muted">Marketplace numbers aren’t available on this server.</p>
      )}

      <h2 className="section-title">Shop</h2>
      {s.drops && s.drops.length > 0 ? (
        <div className="stats">
          {s.drops.map((d) => (
            <Stat
              key={d.id}
              icon={d.id.startsWith('farm_pass') ? 'Icon_Crown' : d.id === 'land_deed' ? 'Icon_Map' : d.id.startsWith('gems') ? 'Icon_Gems' : 'Icon_Gift'}
              label={d.title}
              value={d.cap != null ? `${fmt(d.sold)} / ${fmt(d.cap)}` : fmt(d.sold)}
              sub={`${fmt(d.takings)} FARM taken · ${fmt(d.price)} each${d.paused ? ' · paused' : ''}`}
              tone={d.paused ? 'pink' : d.cap != null && d.sold >= d.cap ? 'gold' : undefined}
            />
          ))}
        </div>
      ) : (
        <p className="muted">Nothing in the shop yet.</p>
      )}

      <h2 className="section-title">Game and chain agree</h2>
      {s.reconcile ? (
        <div className="stats">
          <Stat
            icon={s.reconcile.ok ? 'Icon_ImageIcon_ClearStamp_l' : 'Icon_ImageIcon_Lock01_s'}
            label="Last nightly check"
            value={s.reconcile.ok ? 'All good' : 'Needs a look'}
            sub={`${timeAgo(s.reconcile.at)} · let a developer know if this isn't "All good"`}
            tone={s.reconcile.ok ? 'green' : 'pink'}
          />
          <Stat
            icon="Icon_Star"
            label="Mints"
            value={fmt(s.reconcile.voucherTokensOnChain)}
            sub={`${fmt(s.reconcile.vouchersMinted)} done · ${fmt(s.reconcile.vouchersIssued)} waiting · ${fmt(s.reconcile.vouchersRefunded)} given back`}
          />
          <Stat
            icon="Icon_Gift"
            label="Redeems"
            value={`${fmt(s.reconcile.redeemsCredited)} / ${fmt(s.reconcile.redeemsOnChain)}`}
            sub={s.reconcile.redeemsUncredited ? `${fmt(s.reconcile.redeemsUncredited)} not reached an island yet` : 'Every redeem reached its island'}
            tone={s.reconcile.redeemsUncredited ? 'pink' : undefined}
          />
          {(s.reconcile.refundedButMinted > 0 || s.reconcile.mintedNotRecorded > 0) && (
            <Stat
              icon="Icon_ImageIcon_Lock01_s"
              label="Items in two places"
              value={fmt(s.reconcile.refundedButMinted + s.reconcile.mintedNotRecorded)}
              sub={`${fmt(s.reconcile.refundedButMinted)} given back but minted · ${fmt(s.reconcile.mintedNotRecorded)} minted without a record`}
              tone="pink"
            />
          )}
        </div>
      ) : (
        <p className="muted">The nightly check hasn't run yet.</p>
      )}

      <h2 className="section-title">Last 30 days</h2>
      <div className="charts">
        <BarChart title="New players" points={s.days.map((d) => ({ day: d.day, value: d.signups }))} />
        <BarChart title="Coupon codes given out" tone="gold" points={s.days.map((d) => ({ day: d.day, value: d.codesIssued }))} />
        {s.chain && <BarChart title="Market sales" unit="FARM" points={s.days.map((d) => ({ day: d.day, value: d.volume }))} />}
      </div>
    </>
  );
}
