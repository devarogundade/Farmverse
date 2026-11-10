/** Small motion helpers. Every one is a pure function of time. */

export const clamp = (v: number, a = 0, b = 1) => Math.min(b, Math.max(a, v));
export const lerp = (a: number, b: number, k: number) => a + (b - a) * k;

export const ease = {
  linear: (k: number) => k,
  out: (k: number) => 1 - Math.pow(1 - k, 3),
  outQuint: (k: number) => 1 - Math.pow(1 - k, 5),
  inOut: (k: number) => (k < 0.5 ? 4 * k * k * k : 1 - Math.pow(-2 * k + 2, 3) / 2),
  in: (k: number) => k * k * k,
  /** A little overshoot, for things that land. */
  back: (k: number) => {
    const c1 = 1.5;
    const c3 = c1 + 1;
    return 1 + c3 * Math.pow(k - 1, 3) + c1 * Math.pow(k - 1, 2);
  },
};
export type Ease = keyof typeof ease;

/** 0 before `a`, 1 after `a + d`, eased in between. */
export function ramp(t: number, a: number, d = 0.5, e: Ease = 'out') {
  return ease[e](clamp((t - a) / d));
}

/** Rises at `a`, falls at `b` (each over `d`). For things that come and go. */
export function window_(t: number, a: number, b: number, d = 0.4) {
  return ramp(t, a, d) * (1 - ramp(t, b, d, 'inOut'));
}

/** A value between keyframes [[time, value], ...], eased per segment. */
export function keys(t: number, frames: [number, number][], e: Ease = 'inOut') {
  if (t <= frames[0][0]) return frames[0][1];
  for (let i = 1; i < frames.length; i++) {
    const [t1, v1] = frames[i];
    const [t0, v0] = frames[i - 1];
    if (t <= t1) return lerp(v0, v1, ease[e]((t - t0) / (t1 - t0 || 1)));
  }
  return frames[frames.length - 1][1];
}

/** Rise in: opacity and a short lift, as a style. */
export function rise(t: number, a: number, d = 0.55, dist = 28) {
  const k = ramp(t, a, d);
  return { opacity: k, transform: `translateY(${(1 - k) * dist}px)` };
}

/** Pop in: scale from `from` with a small overshoot. */
export function pop(t: number, a: number, d = 0.5, from = 0.6) {
  const k = clamp((t - a) / d);
  const s = lerp(from, 1, ease.back(k));
  return { opacity: ease.out(clamp(k * 1.8)), transform: `scale(${s})` };
}

export const money = (n: number, digits = 0) =>
  n.toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });

/** A deterministic 0..1 random from an integer. */
export const hash = (i: number) => {
  const x = Math.sin(i * 127.1 + 311.7) * 43758.5453;
  return x - Math.floor(x);
};
