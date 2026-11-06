import { sprite } from '@farmverse/shared/gameTheme';
import { useQuery } from '@tanstack/react-query';
import { config } from '../config';
import { EmptyState, ErrorState, SkeletonGrid } from '../components/states';
import { fmt } from '../components/ui';
import { href } from '../router';

/** A merchant's last look at the market, as the server's merchant-tick writes it (plan 15 §13.1). */
interface MerchantGood {
  item: string;
  lot: number;
  stock: number;
  sales24h: number;
  buyPerUnit: string;
  sellPerUnit: string;
  why: string;
  wantsMore: boolean;
}
interface Merchant {
  name: string;
  display: string;
  role: string;
  account: string;
  farm: string;
  goods: MerchantGood[];
  history: Record<string, { at: number; buy: string }[]>;
  at: number;
}

const FACE: Record<string, string> = { bea: 'Isl_p_baker', ruth: 'Isl_p_farmer', tomas: 'Icon_Shop', ivy: 'Icon_Crown' };
const farm = (yocto: string) => Number(BigInt(yocto) / 10n ** 12n) / 1e6;
const label = (item: string) => {
  const golden = item.startsWith('golden_');
  const base = (golden ? item.slice(7) : item).replace(/_/g, ' ');
  return (golden ? 'Golden ' : '') + base.charAt(0).toUpperCase() + base.slice(1);
};

/** A tiny line of what the merchant paid over its last ticks. */
function Spark({ points }: { points: { at: number; buy: string }[] }) {
  if (points.length < 2) return null;
  const ys = points.map((p) => farm(p.buy));
  const lo = Math.min(...ys);
  const hi = Math.max(...ys);
  const d = ys.map((y, i) => `${(i / (ys.length - 1)) * 100},${hi === lo ? 12 : 22 - ((y - lo) / (hi - lo)) * 20}`).join(' ');
  return (
    <svg className="spark" viewBox="0 0 100 24" preserveAspectRatio="none" aria-hidden>
      <polyline points={d} fill="none" stroke="currentColor" strokeWidth="2" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}

function MerchantCard({ m }: { m: Merchant }) {
  return (
    <article className="merchant fv-panel">
      <header className="merchant-head">
        <img className="fv-icon merchant-face" src={sprite(FACE[m.name] ?? 'Icon_Shop')} alt="" />
        <div>
          <h2 className="fv-title">{m.display}</h2>
          <p className="muted">
            {m.role} · {fmt(farm(m.farm))} FARM in the till
          </p>
        </div>
      </header>
      <table className="merchant-goods">
        <thead>
          <tr>
            <th>Goods</th>
            <th>In stock</th>
            <th>Pays each</th>
            <th>Sells each</th>
            <th>Why</th>
            <th aria-label="Price lately" />
          </tr>
        </thead>
        <tbody>
          {m.goods.map((g) => (
            <tr key={g.item}>
              <td>
                <a href={`${href.explore()}?item=${g.item}&sort=unit_price`}>{label(g.item)}</a>
                <small className="muted"> · buys ×{g.lot}</small>
              </td>
              <td>{fmt(g.stock)}</td>
              <td>{g.wantsMore ? fmt(farm(g.buyPerUnit)) : '—'}</td>
              <td>{fmt(farm(g.sellPerUnit))}</td>
              <td className="muted">{g.why}</td>
              <td>
                <Spark points={m.history?.[g.item] ?? []} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </article>
  );
}

/** Market Town: the merchants are NPCs with their own wallets; their prices follow their stock and the market. */
export function Merchants() {
  const q = useQuery({
    queryKey: ['merchants'],
    queryFn: async ({ signal }) => {
      const res = await fetch(`${config.apiUrl}/merchants`, { signal });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      return (await res.json()) as Merchant[];
    },
    refetchInterval: 60_000,
    enabled: Boolean(config.apiUrl),
  });
  return (
    <>
      <div className="explore-head">
        <div>
          <h1 className="fv-title">Market Town</h1>
          <p className="muted">
            Bea, Ruth, Tomas and Ivy trade with their own FARM. They pay more for what they're short of and what sells,
            and less when their shelves are full. Sell to them from your farm, or buy what they hold.
          </p>
        </div>
      </div>
      {q.isLoading ? (
        <SkeletonGrid />
      ) : q.isError ? (
        <ErrorState onRetry={() => q.refetch()} />
      ) : !q.data?.length ? (
        <EmptyState title="The merchants are setting up">They open their stalls soon.</EmptyState>
      ) : (
        <div className="merchants">
          {q.data.map((m) => (
            <MerchantCard key={m.name} m={m} />
          ))}
        </div>
      )}
    </>
  );
}
