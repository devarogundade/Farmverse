import { useId } from 'react';
import { FarmIcon } from '@farmverse/shared/brand';
import { sprite } from '@farmverse/shared/gameTheme';
import { flatArt, isGolden, type NftMetadata, type NftType } from '@farmverse/shared/nft/metadata';
import type { EventKind, Token } from '../api/types';

export { FarmIcon };

/** Game sprite for each kind of item (filter chips, headings). */
export const TYPE_ICON: Record<NftType, string> = {
  worker: 'Isl_p_worker', tractor: 'Icon_Hammer', building: 'Isl_b_barn', land: 'Paint_plot_field', good: 'Paint_crop_wheat', animal: 'Paint_animal_cow',
  pack: 'Icon_Gift', deed: 'Icon_Map',
};

/** Game item frame (ItemFrame01_Single_*) per kind of item. */
export const FRAME: Record<NftType, string> = {
  worker: 'yellow', tractor: 'blue', building: 'brown', land: 'green', good: 'purple', animal: 'green', pack: 'blue', deed: 'green',
};

/** The frame classes for an item: golden harvests get the gold frame and a glow. */
export const frameOf = (m: Pick<NftMetadata, 'type' | 'item_id'>) => (isGolden(m) ? 'yellow golden' : FRAME[m.type]);

/** Packs and deeds have no 3D model: their game art, centred where the model would be. */
export function FlatArt({ meta, className = '' }: { meta: Pick<NftMetadata, 'type' | 'item_id'>; className?: string }) {
  const art = flatArt(meta);
  if (!art) return null;
  return (
    <span className={`flat-art ${className}`}>
      <img src={sprite(art)} alt="" />
    </span>
  );
}

export function Price({ value, size = 'md' }: { value?: number; size?: 'md' | 'lg' }) {
  if (value == null) return <span className="price muted">Not listed</span>;
  return (
    <span className={`price price-${size}`}>
      <FarmIcon size={size === 'lg' ? 22 : 14} />
      {fmt(value)}
      <small>FARM</small>
    </span>
  );
}

/** An item's name for display. Stacked goods are minted as "Wheat ×250"; where the ×250 badge sits
 * beside the name, the name drops it so the amount is said once. */
export const itemName = (token: Token) =>
  token.metadata.quantity != null ? token.title.replace(/\s*×\s*[\d,.]+\s*$/, '') || token.title : token.title;

export const fmt = (n: number) => (n >= 1000 ? n.toLocaleString('en-US', { maximumFractionDigits: 0 }) : n.toLocaleString('en-US', { maximumFractionDigits: 2 }));

const AVATAR_HUES = [268, 28, 190, 330, 140, 48, 210];
export function AccountAvatar({ account, size = 28 }: { account: string; size?: number }) {
  let h = 0;
  for (const ch of account) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  const hue = AVATAR_HUES[h % AVATAR_HUES.length];
  return (
    <span
      className="acct-avatar"
      style={{ width: size, height: size, background: `linear-gradient(135deg, hsl(${hue} 80% 62%), hsl(${(hue + 40) % 360} 70% 42%))`, fontSize: size * 0.42 }}
      aria-hidden="true"
    >
      {account[0]?.toUpperCase()}
    </span>
  );
}

const PILL: Record<EventKind, string> = { minted: 'Minted', listed: 'Listed', bought: 'Bought', delisted: 'Delisted', redeemed: 'Used in game', sent: 'Sent', offer: 'Offer' };
export function EventPill({ kind }: { kind: EventKind }) {
  return <span className={`pill pill-${kind}`}>{PILL[kind]}</span>;
}

export function timeAgo(at: number, now = Date.now()) {
  const s = Math.max(1, Math.round((now - at) / 1000));
  if (s < 3600) return `${Math.max(1, Math.round(s / 60))}m ago`;
  if (s < 86400) return `${Math.round(s / 3600)}h ago`;
  return `${Math.round(s / 86400)}d ago`;
}

/** Smooth area sparkline with an end marker, like the price card in the design. */
export function Sparkline({ values, height = 96 }: { values: number[]; height?: number }) {
  const id = useId();
  const w = 300;
  const min = Math.min(...values);
  const max = Math.max(...values);
  const pad = 8;
  const pts = values.map((v, i) => [
    (i / (values.length - 1)) * (w - pad * 2) + pad,
    height - pad - ((v - min) / (max - min || 1)) * (height - pad * 2),
  ]);
  let d = `M${pts[0][0]},${pts[0][1]}`;
  for (let i = 1; i < pts.length; i++) {
    const [x0, y0] = pts[i - 1];
    const [x1, y1] = pts[i];
    const mx = (x0 + x1) / 2;
    d += ` C${mx},${y0} ${mx},${y1} ${x1},${y1}`;
  }
  const [ex, ey] = pts[pts.length - 1];
  return (
    <svg className="sparkline" viewBox={`0 0 ${w} ${height}`} preserveAspectRatio="none" role="img" aria-label="Price over the last 24 days">
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stopColor="var(--accent-2)" stopOpacity="0.35" />
          <stop offset="1" stopColor="var(--accent-2)" stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={`${d} L${ex},${height} L${pts[0][0]},${height} Z`} fill={`url(#${id})`} />
      <path d={d} fill="none" stroke="var(--accent-2)" strokeWidth="3" vectorEffect="non-scaling-stroke" />
      <circle cx={ex} cy={ey} r="5" fill="var(--gold)" stroke="var(--ink)" strokeWidth="2.5" vectorEffect="non-scaling-stroke" />
    </svg>
  );
}
