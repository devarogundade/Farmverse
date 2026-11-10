import { createContext, useContext, useSyncExternalStore } from 'react';

/**
 * The trailer's one clock, in seconds. Every scene draws itself from this number alone, so any
 * frame can be drawn on its own: the preview plays it in real time, the renderer steps it frame
 * by frame.
 */
let now = 0;
const subs = new Set<() => void>();

export const clock = {
  get: () => now,
  set(t: number) {
    now = t;
    subs.forEach((f) => f());
  },
  subscribe(f: () => void) {
    subs.add(f);
    return () => void subs.delete(f);
  },
};

/** Global time, in seconds since the trailer began. */
export const useNow = () => useSyncExternalStore(clock.subscribe, clock.get);

/** Render mode: the renderer drives the clock and wants exact frames. */
export const RENDER = new URLSearchParams(location.search).has('render');

export interface SceneTime {
  /** Seconds since this scene began. */
  t: number;
  /** The scene's length. */
  dur: number;
  /** True while the scene is on screen. */
  live: boolean;
}
export const SceneCtx = createContext<SceneTime>({ t: 0, dur: 1, live: false });
/** Seconds since the current scene began. */
export const useT = () => useContext(SceneCtx).t;
export const useScene = () => useContext(SceneCtx);

/** Preview transport: whether the clock is running in real time. */
export const transport = { playing: false };
