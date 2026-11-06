import { useStats } from '../api/admin';
import { BarChart } from '../components/BarChart';
import { Failed, fmt, Loading, PageHead, Stat, timeAgo } from '../components/ui';
import { href } from '../router';

export function Dashboard() {
  const { data: s, error, refetch, isLoading } = useStats();
  if (isLoading) return <Loading />;
  if (error || !s) return <Failed error={error} retry={() => void refetch()} />;

  const indexerLate = s.chain?.indexer ? Date.now() - s.chain.indexer.updatedAt > 10 * 60_000 : true;

  return (
    <>
      <PageHead icon="Icon_Trophy" title="Dashboard">
        Updated {timeAgo(s.at)}. Refreshes every minute.
      </PageHead>

      <h2 className="section-title">Players</h2>
      <div className="stats">
        <a href={href.players()}>
          <Stat icon="Icon_Friends" label="Players" value={fmt(s.players.total)} sub={`+${fmt(s.players.new24h)} today · +${fmt(s.players.new7d)} this week`} />
        </a>
        <Stat icon="Icon_Heart" label="Played today" value={fmt(s.players.active24h)} sub={`${fmt(s.players.active7d)} this week · ${fmt(s.players.active30d)} this month`} tone="green" />
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
          <Stat icon="Icon_ImageIcon_ClearStamp_l" label="Redeemed" value={fmt(s.coupons.redeemed)} tone="green" />
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
          <Stat icon="Icon_Golds" label="Sales" value={fmt(s.chain.sales)} sub={`${fmt(s.chain.volume)} FARM in total · ${fmt(s.chain.volume7d)} this week`} tone="gold" />
          <Stat
            icon="Icon_Compass"
            label="Market data"
            value={s.chain.indexer ? timeAgo(s.chain.indexer.updatedAt) : 'not started'}
            sub={indexerLate ? 'Behind: check the indexer is running' : `Up to block ${fmt(s.chain.indexer?.blockHeight ?? 0)}`}
            tone={indexerLate ? 'pink' : 'green'}
          />
        </div>
      ) : (
        <p className="muted">Marketplace numbers aren’t available on this server.</p>
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
