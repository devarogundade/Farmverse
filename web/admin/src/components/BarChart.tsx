import { useState } from 'react';
import { fmt } from './ui';

/**
 * One series of daily bars. The title names the series (no legend); hovering or focusing a day shows
 * its value; a hidden table carries the same numbers for screen readers.
 */
export function BarChart({ title, points, unit = '', tone = 'accent' }: { title: string; points: { day: string; value: number }[]; unit?: string; tone?: 'accent' | 'gold' }) {
  const [hover, setHover] = useState<number | null>(null);
  const W = 600;
  const H = 160;
  const top = 12;
  const max = Math.max(1, ...points.map((p) => p.value));
  const nice = niceMax(max);
  const slot = W / points.length;
  const bar = Math.max(2, slot - 2); // 2px surface gap between bars
  const total = points.reduce((s, p) => s + p.value, 0);
  const shown = hover === null ? null : points[hover];
  const label = (d: string) => new Date(`${d}T00:00:00Z`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', timeZone: 'UTC' });

  return (
    <figure className="chart fv-panel">
      <figcaption className="chart-head">
        <span className="chart-title">{title}</span>
        <span className="chart-value fv-title">
          {shown ? fmt(shown.value) : fmt(total)}
          {unit && <small> {unit}</small>}
        </span>
        <span className="chart-sub muted">{shown ? label(shown.day) : 'last 30 days'}</span>
      </figcaption>
      <div className="chart-plot">
        <svg viewBox={`0 0 ${W} ${H}`} preserveAspectRatio="none" aria-hidden="true" onMouseLeave={() => setHover(null)}>
          {[0.5, 1].map((f) => (
            <line key={f} x1="0" x2={W} y1={top + (H - top) * (1 - f)} y2={top + (H - top) * (1 - f)} className="chart-grid" vectorEffect="non-scaling-stroke" />
          ))}
          <line x1="0" x2={W} y1={H} y2={H} className="chart-base" vectorEffect="non-scaling-stroke" />
          {points.map((p, i) => {
            const h = p.value > 0 ? Math.max(3, ((H - top) * p.value) / nice) : 0;
            const x = i * slot + (slot - bar) / 2;
            return (
              <g key={p.day} onMouseEnter={() => setHover(i)}>
                <rect x={i * slot} y={0} width={slot} height={H} fill="transparent" />
                {h > 0 && <path d={roundedTop(x, H - h, bar, h, Math.min(4, bar / 2))} className={`chart-bar ${tone}${hover === i ? ' on' : ''}`} />}
              </g>
            );
          })}
        </svg>
        {/* The scale's top, written on the line it names */}
        <span className="chart-max muted" style={{ top: `${(top / H) * 100}%` }}>
          {fmt(nice)}
        </span>
        <div className="chart-axis muted">
          <span>{label(points[0]?.day ?? '')}</span>
          <span>{label(points[points.length - 1]?.day ?? '')}</span>
        </div>
      </div>
      <table className="sr-only">
        <caption>{title}</caption>
        <tbody>
          {points.map((p) => (
            <tr key={p.day}>
              <th>{p.day}</th>
              <td>{p.value}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </figure>
  );
}

function niceMax(v: number) {
  const p = 10 ** Math.floor(Math.log10(v));
  for (const m of [1, 2, 2.5, 5, 10]) if (m * p >= v) return m * p;
  return 10 * p;
}

/** A bar whose top corners are rounded and whose foot sits square on the baseline. */
function roundedTop(x: number, y: number, w: number, h: number, r: number) {
  const rr = Math.min(r, h);
  return `M${x},${y + h} V${y + rr} Q${x},${y} ${x + rr},${y} H${x + w - rr} Q${x + w},${y} ${x + w},${y + rr} V${y + h} Z`;
}
