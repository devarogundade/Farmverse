import '@farmverse/shared/theme.css';
import './trailer.css';
import { useProgress } from '@react-three/drei';
import { advance } from '@react-three/fiber';
import { StrictMode, useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { createRoot } from 'react-dom/client';
import { RENDER, clock, transport, useNow } from './clock';
import { CLIPS, audioFound, hasClip, probeMedia, seekVideos, type ClipId } from './media';
import { resolveCues } from './sfx';
import { Trailer } from './Trailer';
import { FPS, HEIGHT, SCENES, TOTAL, WIDTH } from './timeline';

const raf = () => new Promise<void>((r) => requestAnimationFrame(() => r()));
const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

/** Waits for every 3D model to finish loading. */
async function modelsLoaded() {
  for (let i = 0; i < 600; i++) {
    if (!useProgress.getState().active) return;
    await sleep(50);
  }
}

/** Draws exactly frame `t`: the DOM, every 3D canvas and every video. */
async function seek(t: number) {
  flushSync(() => clock.set(t));
  await raf();
  advance(t);
  await seekVideos();
  await modelsLoaded();
  advance(t);
  await raf();
  await raf();
}

/** Recorded clips and where they sit, for mixing their own game sound into the render. */
function clipSlots() {
  return (Object.keys(CLIPS) as ClipId[]).filter(hasClip).map((id) => {
    const c = CLIPS[id];
    const scene = SCENES.find((s) => s.id === c.scene)!;
    return { id, file: `media/clips/${c.file}`, start: c.start, t: scene.start + c.from, dur: c.until - c.from };
  });
}

/** The renderer's handle (scripts/render.mjs). */
async function ready() {
  await document.fonts.ready;
  await Promise.all([...document.images].map((img) => (img.complete ? null : img.decode().catch(() => null))));
  // Visit each scene once so every canvas sizes itself and every model loads.
  for (const s of SCENES) await seek(s.start + 2);
  await modelsLoaded();
  await sleep(500);
  await seek(0);
  return { fps: FPS, total: TOTAL, width: WIDTH, height: HEIGHT, audio: audioFound, sfx: resolveCues(), clips: clipSlots() };
}

declare global {
  interface Window {
    __trailer?: { ready: typeof ready; seek: typeof seek };
  }
}

/** Preview: one sound effect, cut and faded the way the render will be. */
function playCue(c: ReturnType<typeof resolveCues>[number]) {
  const a = new Audio(`/game-audio/${c.file}`);
  const vol = c.vol ?? 0.6;
  a.volume = c.fadeIn ? 0 : vol;
  a.loop = Boolean(c.loop || c.dur);
  void a.play().catch(() => {});
  const t0 = performance.now();
  if (c.fadeIn || c.fadeOut || c.dur) {
    const step = () => {
      const s = (performance.now() - t0) / 1000;
      let k = 1;
      if (c.fadeIn) k = Math.min(k, s / c.fadeIn);
      if (c.dur && c.fadeOut) k = Math.min(k, (c.dur - s) / c.fadeOut);
      a.volume = Math.max(0, Math.min(1, vol * k));
      if (c.dur && s >= c.dur) return a.pause();
      requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }
}

const fmt = (t: number) => `${Math.floor(t / 60)}:${String(Math.floor(t % 60)).padStart(2, '0')}.${String(Math.floor((t % 1) * FPS)).padStart(2, '0')}`;

/** Preview: plays in real time, with the voiceover and music if they are there. */
function Player() {
  const now = useNow();
  const [playing, setPlaying] = useState(false);
  const [scale, setScale] = useState(1);
  const audio = useRef<HTMLAudioElement[]>([]);

  useEffect(() => {
    audio.current = [];
    if (audioFound.voice) audio.current.push(new Audio(audioFound.voice));
    if (audioFound.music) {
      const m = new Audio(audioFound.music);
      m.volume = 0.25;
      audio.current.push(m);
    }
    const fit = () => setScale(Math.min(window.innerWidth / WIDTH, (window.innerHeight - 64) / HEIGHT));
    fit();
    window.addEventListener('resize', fit);
    return () => window.removeEventListener('resize', fit);
  }, []);

  useEffect(() => {
    transport.playing = playing;
    audio.current.forEach((a) => {
      a.currentTime = clock.get();
      if (playing) void a.play().catch(() => {});
      else a.pause();
    });
    if (!playing) {
      // Paused: keep redrawing the 3D (models finish loading, canvases resize).
      const id = setInterval(() => advance(clock.get()), 200);
      return () => clearInterval(id);
    }
    let last = performance.now();
    let id = 0;
    const cues = resolveCues();
    const tick = (ms: number) => {
      const from = clock.get();
      const t = Math.min(TOTAL, from + (ms - last) / 1000);
      last = ms;
      for (const c of cues) if (c.t > from && c.t <= t) playCue(c);
      clock.set(t);
      advance(t);
      if (t >= TOTAL) setPlaying(false);
      else id = requestAnimationFrame(tick);
    };
    id = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(id);
  }, [playing]);

  const jump = (t: number) => {
    const v = Math.max(0, Math.min(TOTAL, t));
    clock.set(v);
    audio.current.forEach((a) => (a.currentTime = v));
    requestAnimationFrame(() => advance(v));
  };

  useEffect(() => {
    const key = (e: KeyboardEvent) => {
      if (e.code === 'Space') {
        e.preventDefault();
        setPlaying((p) => !p);
      }
      if (e.code === 'ArrowRight') jump(clock.get() + (e.shiftKey ? 1 / FPS : 1));
      if (e.code === 'ArrowLeft') jump(clock.get() - (e.shiftKey ? 1 / FPS : 1));
    };
    window.addEventListener('keydown', key);
    return () => window.removeEventListener('keydown', key);
  }, []);

  return (
    <div className="player">
      <div className="player__stage" style={{ width: WIDTH * scale, height: HEIGHT * scale }}>
        <div style={{ transform: `scale(${scale})`, transformOrigin: '0 0' }}>
          <Trailer />
        </div>
      </div>
      <div className="player__bar">
        <button onClick={() => setPlaying((p) => !p)}>{playing ? 'Pause' : 'Play'}</button>
        <span className="player__time">{fmt(now)}</span>
        <input type="range" min={0} max={TOTAL} step={1 / FPS} value={now} onChange={(e) => jump(Number(e.target.value))} />
        <span className="player__scenes">
          {SCENES.map((s) => (
            <button key={s.id} onClick={() => jump(s.start)} className={now >= s.start ? 'is-past' : ''} title={s.label}>
              {s.label}
            </button>
          ))}
        </span>
      </div>
    </div>
  );
}

await probeMedia();
const start = Number(new URLSearchParams(location.search).get('t') ?? 0);
clock.set(start);

createRoot(document.getElementById('root')!).render(
  RENDER ? (
    <Trailer />
  ) : (
    <StrictMode>
      <Player />
    </StrictMode>
  ),
);
if (RENDER) window.__trailer = { ready, seek };
else setTimeout(() => advance(start), 600);
