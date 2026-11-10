import { FarmIcon } from '@farmverse/shared/brand';
import { sprite } from '@farmverse/shared/gameTheme';
import { Check, Lock } from 'lucide-react';
import { useLayoutEffect, useRef, type CSSProperties, type ReactNode } from 'react';
import { clamp, ease, keys, lerp, money, ramp, rise } from './anim';
import { useScene, useT } from './clock';
import { CLIPS, hasClip, syncVideo, type ClipId } from './media';

/** A game title whose words rise in one after another. */
export function Title({
  at,
  children,
  size = 84,
  out,
  className = '',
  style,
  gold,
}: {
  at: number;
  children: string;
  size?: number;
  /** Fades out from this time. */
  out?: number;
  className?: string;
  style?: CSSProperties;
  /** Words to colour gold. */
  gold?: string[];
}) {
  const t = useT();
  const words = children.split(' ');
  const fade = out === undefined ? 1 : 1 - ramp(t, out, 0.35, 'inOut');
  return (
    <h2 className={`tr-title fv-title ${className}`} style={{ fontSize: size, opacity: fade, ...style }}>
      {words.map((w, i) => (
        <span key={i} className={gold?.includes(w) ? 'fv-gold' : undefined} style={{ display: 'inline-block', ...rise(t, at + i * 0.07, 0.5, size * 0.4) }}>
          {w}
          {i < words.length - 1 ? ' ' : ''}
        </span>
      ))}
    </h2>
  );
}

/** A small line under or over a title. */
export function Line({ at, children, out, className = '', style }: { at: number; children: ReactNode; out?: number; className?: string; style?: CSSProperties }) {
  const t = useT();
  const fade = out === undefined ? 1 : 1 - ramp(t, out, 0.35, 'inOut');
  const r = rise(t, at, 0.6, 18);
  return (
    <p className={`tr-line ${className}`} style={{ ...style, ...r, opacity: r.opacity * fade }}>
      {children}
    </p>
  );
}

/** A pill with a tick or a game icon. */
export function Chip({ at, children, icon = 'check', style, className = '' }: { at: number; children: ReactNode; icon?: 'check' | string; style?: CSSProperties; className?: string }) {
  const t = useT();
  const k = clamp((t - at) / 0.45);
  const s = lerp(0.7, 1, ease.back(k));
  return (
    <span className={`tr-chip ${className}`} style={{ opacity: ease.out(clamp(k * 2)), transform: `scale(${s})`, ...style }}>
      {icon === 'check' ? (
        <span className="tr-chip__tick">
          <Check size={22} strokeWidth={4} />
        </span>
      ) : (
        <img src={sprite(icon)} alt="" className="tr-chip__icon" />
      )}
      {children}
    </span>
  );
}

/** FARM amount with the clover. */
export function Farm({ value, size = 24, digits = 0, className = '' }: { value: number; size?: number; digits?: number; className?: string }) {
  return (
    <span className={`tr-farm ${className}`} style={{ fontSize: size }}>
      <FarmIcon size={size * 1.05} />
      <b>{money(value, digits)}</b>
    </span>
  );
}

/** A number that rolls from `from` to `to` between `at` and `at + d`. */
export function rollValue(t: number, at: number, from: number, to: number, d = 1) {
  return lerp(from, to, ease.out(clamp((t - at) / d)));
}

/** A browser window around the game or the market. */
export function Browser({ url, children, className = '', style }: { url: string; children: ReactNode; className?: string; style?: CSSProperties }) {
  return (
    <div className={`tr-browser ${className}`} style={style}>
      <div className="tr-browser__bar">
        <span className="tr-browser__lights">
          <i />
          <i />
          <i />
        </span>
        <span className="tr-browser__url">
          <Lock size={15} strokeWidth={3} />
          {url}
        </span>
      </div>
      <div className="tr-browser__body">{children}</div>
    </div>
  );
}

/**
 * The pointer. `path` is [time, x, y] keyframes in the parent's coordinates; `clicks` are the times
 * it presses. It eases between keyframes the way a hand moves: fast in the middle, settling.
 */
export function Cursor({ path, clicks = [], appear = 0, hide }: { path: [number, number, number][]; clicks?: number[]; appear?: number; hide?: number }) {
  const t = useT();
  const x = keys(t, path.map(([a, x]) => [a, x]));
  const y = keys(t, path.map(([a, , y]) => [a, y]));
  let press = 0;
  let ring = -1;
  for (const c of clicks) {
    const d = t - c;
    if (d > -0.12 && d < 0.18) press = Math.max(press, 1 - Math.abs(d - 0.02) / 0.16);
    if (d >= 0 && d < 0.55) ring = d / 0.55;
  }
  const show = ramp(t, appear, 0.3) * (hide === undefined ? 1 : 1 - ramp(t, hide, 0.3));
  return (
    <div className="tr-cursor" style={{ transform: `translate(${x}px, ${y}px)`, opacity: show }}>
      {ring >= 0 && <span className="tr-cursor__ring" style={{ transform: `translate(-50%, -50%) scale(${0.4 + ring * 1.2})`, opacity: 1 - ring }} />}
      <svg width="40" height="40" viewBox="0 0 32 32" style={{ transform: `scale(${1 - press * 0.14})`, transformOrigin: '6px 4px' }}>
        <path d="M6 3.5v21.2l5.3-5 3.7 8.3 3.7-1.6-3.7-8.2h7.4z" fill="#fff" stroke="#170d06" strokeWidth="2.2" strokeLinejoin="round" />
      </svg>
    </div>
  );
}

/** Press depth of a button pressed at `at`, for a scale. */
export function pressed(t: number, at: number) {
  const d = t - at;
  return d > -0.1 && d < 0.25 ? 1 - 0.05 * (1 - Math.abs(d - 0.04) / 0.2) : 1;
}

/** The game's dim-green ground behind built scenes. */
export function Ground({ image, dim = 0.86, children }: { image?: string; dim?: number; children?: ReactNode }) {
  const t = useT();
  return (
    <div className="tr-ground">
      {image && <img className="tr-ground__img" src={sprite(image)} alt="" style={{ transform: `scale(${1.15 + t * 0.006})`, opacity: 1 - dim }} />}
      <div className="tr-ground__glow" />
      {children}
    </div>
  );
}

/**
 * A full-bleed gameplay recording. Until the file is in media/clips it shows a labelled slot,
 * so the cut can be reviewed with the recordings still to come.
 */
export function Clip({ id, from, until, children }: { id: ClipId; from: number; until: number; children?: ReactNode }) {
  const t = useT();
  const { live } = useScene();
  const ref = useRef<HTMLVideoElement>(null);
  const clip = CLIPS[id];
  const on = live && t >= from - 0.4 && t < until + 0.4;
  const local = Math.max(0, t - from);
  useLayoutEffect(() => {
    if (ref.current) syncVideo(ref.current, clip.start + local, on);
  });
  const fade = ramp(t, from, 0.3, 'inOut');
  const zoom = 1.04 - Math.min(local, 10) * 0.004;
  if (t < from - 0.4 || t > until + 0.6) return null;
  return (
    <div className="tr-clip" style={{ opacity: fade }}>
      {hasClip(id) ? (
        <video ref={ref} className="tr-clip__video" src={`/media/clips/${clip.file}`} muted playsInline preload="auto" style={{ transform: `scale(${zoom})` }} />
      ) : (
        <div className="tr-slot">
          <img className="tr-slot__bg" src={sprite(id === 'clip3' ? 'Home_LoadingBg' : 'Home_Bg')} alt="" style={{ transform: `scale(${zoom + 0.06})` }} />
          <div className="tr-slot__card">
            <span className="tr-slot__kicker">Gameplay · {clip.file}</span>
            <b className="fv-title">{clip.title}</b>
            <span>{clip.what}</span>
          </div>
        </div>
      )}
      <div className="tr-clip__shade" />
      {children}
    </div>
  );
}

/** An item card the way the market and the game frame it. */
export function ItemCard({
  frame = 'blue',
  image,
  name,
  children,
  className = '',
  style,
}: {
  frame?: 'blue' | 'yellow' | 'green' | 'purple' | 'brown';
  image?: string;
  name: string;
  children?: ReactNode;
  className?: string;
  style?: CSSProperties;
}) {
  return (
    <div className={`tr-card fv-frame ${frame} ${className}`} style={style}>
      <div className="tr-card__art">{image && <img src={image} alt="" />}</div>
      <div className="tr-card__foot">
        <b className="fv-title tr-card__name">{name}</b>
        {children}
      </div>
    </div>
  );
}

export const nft = (id: string) => `/nft/items/${id}.png`;
