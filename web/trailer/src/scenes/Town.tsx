import { sprite } from '@farmverse/shared/gameTheme';
import { TrendingUp } from 'lucide-react';
import { clamp, ease, keys, pop, ramp, rise } from '../anim';
import { useT } from '../clock';
import { Farm, Ground, Title, rollValue } from '../ui';

const MERCHANTS = [
  { name: 'Bea', role: 'Baker', face: 'Isl_p_baker', farm: 18420, buys: ['Paint_crop_wheat', 'Isl_g_flour', 'Paint_good_bread'] },
  { name: 'Ruth', role: 'Rancher', face: 'Isl_p_farmer', farm: 12960, buys: ['Isl_g_egg', 'Isl_g_milk', 'Paint_animal_cow'] },
  { name: 'Tomas', role: 'Trader', face: 'Isl_p_worker', farm: 22150, buys: ['Isl_g_tomato', 'Paint_seed_wheat', 'Isl_g_flour'] },
  { name: 'Ivy', role: 'Collector', face: 'Isl_p_builder', farm: 9880, buys: ['Paint_crop_wheat', 'Icon_Star', 'Icon_Crown'] },
];

// What Bea paid for 10 wheat over the last day: steady, then a run of sales on thin stock.
const PRICE = [5.0, 5.1, 4.9, 5.0, 5.2, 5.0, 4.8, 4.9, 5.0, 5.1, 5.0, 5.2, 5.1, 5.3, 5.4, 5.6, 5.9, 6.1, 6.4, 6.6, 6.9, 7.1, 7.3, 7.4];
const W = 1100;
const H = 230;
const lo = 4.4;
const hi = 7.8;
const pt = (i: number) => [(i / (PRICE.length - 1)) * W, H - ((PRICE[i] - lo) / (hi - lo)) * H] as const;
const LINE = PRICE.map((_, i) => `${i ? 'L' : 'M'}${pt(i)[0].toFixed(1)},${pt(i)[1].toFixed(1)}`).join(' ');

const WHY = 'Low on stock and it sells: paying more';

/** 1:22 — Market Town: four merchants with their own wallets; Bea's wheat price moves, and says why. */
export function Town() {
  const t = useT();
  const focus = ramp(t, 6.0, 0.9, 'inOut');
  const stock = Math.round(keys(t, [[7.6, 180], [11.6, 60]], 'inOut'));
  const sales = t > 11.0 ? 3 : t > 9.8 ? 2 : t > 8.6 ? 1 : 0;
  const price = rollValue(t, 11.6, 5.0, 7.4, 1.8);
  const draw = ease.inOut(clamp((t - 7.4) / 6.2));
  const typed = Math.round(clamp((t - 14.2) / 1.6) * WHY.length);
  const [ex, ey] = pt(Math.round(draw * (PRICE.length - 1)));
  return (
    <div className="sc sc-town">
      <Ground image="Home_LoadingBg" dim={0.9} />
      <div className="sc-head-l">
        <Title at={0.3} size={76} out={15.2} gold={['Town']}>
          Market Town
        </Title>
        <p className="tr-line" style={{ ...rise(t, 1.0, 0.6, 16), opacity: rise(t, 1.0, 0.6, 16).opacity * (1 - ramp(t, 15.2, 0.35)) }}>
          Four merchants, each with their own NEAR wallet
        </p>
        {t > 15.3 && (
          <Title at={15.5} size={76} style={{ position: 'absolute', top: 0 }} gold={['why']}>
            Prices move on their own, and say why
          </Title>
        )}
      </div>

      <div className="town">
        {MERCHANTS.map((m, i) => {
          const away = i === 0 ? 0 : focus;
          return (
            <div
              key={m.name}
              className="merchant fv-panel"
              style={{
                ...pop(t, 0.9 + i * 0.25, 0.55, 0.8),
                left: i * 430,
                opacity: pop(t, 0.9 + i * 0.25, 0.55, 0.8).opacity * (1 - away),
                transform: `${pop(t, 0.9 + i * 0.25, 0.55, 0.8).transform} translateX(${away * (200 + i * 60)}px)`,
              }}
            >
              <span className="merchant__face">
                <img src={sprite(m.face)} alt="" />
              </span>
              <b className="fv-title merchant__name">{m.name}</b>
              <span className="merchant__role">{m.role}</span>
              <span className="merchant__wallet">
                <small>Own NEAR wallet</small>
                <Farm value={Math.round(rollValue(t, 1.4 + i * 0.25, 0, m.farm, 1.4))} size={30} />
              </span>
              <span className="merchant__buys">
                <small>Buys</small>
                {m.buys.map((b) => (
                  <img key={b} src={sprite(b)} alt="" />
                ))}
              </span>
            </div>
          );
        })}

        <div className="desk fv-panel" style={{ opacity: focus, transform: `translateX(${(1 - focus) * 120}px)` }}>
          <header className="desk__head">
            <img src={sprite('Paint_crop_wheat')} alt="" />
            <b className="fv-title">Bea buys Wheat</b>
            <span className="desk__every">Checks every 10 minutes</span>
          </header>
          <div className="desk__stats">
            <div className="stat">
              <small>In stock</small>
              <b className="fv-title">{stock}</b>
              <span className="stat__bar">
                <i style={{ width: `${(stock / 200) * 100}%` }} />
              </span>
            </div>
            <div className="stat">
              <small>Sold today</small>
              <b className="fv-title">{sales}</b>
              <span className="stat__pips">
                {[0, 1, 2].map((p) => (
                  <i key={p} className={p < sales ? 'is-on' : ''} />
                ))}
              </span>
            </div>
            <div className="stat stat--price">
              <small>Pays for 10 wheat</small>
              <span className="stat__price">
                <Farm value={price} size={46} digits={1} />
                <TrendingUp size={36} strokeWidth={3.2} style={{ opacity: ramp(t, 12.0, 0.4) }} />
              </span>
            </div>
          </div>
          <svg className="desk__chart" viewBox={`-8 -12 ${W + 16} ${H + 24}`} width={W} height={H}>
            {[0.25, 0.5, 0.75].map((g) => (
              <line key={g} x1={0} x2={W} y1={H * g} y2={H * g} className="desk__grid" />
            ))}
            <path d={LINE} className="desk__line" pathLength={1} strokeDasharray="1" strokeDashoffset={1 - draw} />
            {draw > 0.01 && <circle cx={ex} cy={ey} r={9} className="desk__dot" />}
          </svg>
          <div className="why" style={pop(t, 14.0, 0.45, 0.85)}>
            <span className="why__face">
              <img src={sprite('Isl_p_baker')} alt="" />
            </span>
            <span className="why__bubble">
              {WHY.slice(0, typed)}
              <i className="why__caret" style={{ opacity: typed < WHY.length ? 1 : 0 }} />
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
