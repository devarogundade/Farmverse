import { RENDER, transport } from './clock';

/**
 * Your recordings and audio, served from web/trailer/media. A clip that isn't there yet shows a
 * labelled placeholder in its slot, so the trailer can be reviewed and rendered at any time.
 */
/**
 * Each clip's slot: its scene, and when it is on screen in seconds from the scene's start. `start` is
 * where to begin inside your recording. The clip's own game sound plays in its slot.
 */
export const CLIPS = {
  clip1: { scene: 'grow', from: 0, until: 7, file: 'clip1.mp4', start: 0, title: 'Crops', what: 'Harvest two or three fields of ripe wheat, then replant one.' },
  clip2: { scene: 'grow', from: 7, until: 14.5, file: 'clip2.mp4', start: 0, title: 'Animals and a factory', what: 'Collect eggs and milk, then start or collect a bakery job.' },
  clip3: { scene: 'island', from: 0, until: 7, file: 'clip3.mp4', start: 0, title: 'Zombie night', what: 'A night raid: zombies walk in, your defenders fight them off.' },
  clip4: { scene: 'island', from: 7, until: 14.5, file: 'clip4.mp4', start: 0, title: 'The island grows', what: 'A slow zoom out across a big island: valley, lake, sea, town.' },
  clip5: { scene: 'redeem', from: 4.2, until: 12.5, file: 'clip5.mp4', start: 0, title: 'Redeem in the game', what: 'Storage → NFTs → the wheat NFT → Redeem. Hold on the goods arriving.' },
} as const;
export type ClipId = keyof typeof CLIPS;

const found = new Map<string, boolean>();

async function exists(path: string, kind: string) {
  try {
    const r = await fetch(path, { method: 'HEAD', cache: 'no-store' });
    return r.ok && (r.headers.get('content-type') ?? '').startsWith(kind);
  } catch {
    return false;
  }
}

export const AUDIO = {
  voice: ['/media/audio/voiceover.wav', '/media/audio/voiceover.mp3', '/media/audio/voiceover.m4a'],
  music: ['/media/audio/music.mp3', '/media/audio/music.wav', '/media/audio/music.m4a'],
};
export const audioFound: { voice?: string; music?: string } = {};
export let nearLogo: string | null = null;

/** Looks for every clip, the audio and the optional NEAR logo once, before the first frame. */
export async function probeMedia() {
  await Promise.all([
    ...Object.entries(CLIPS).map(async ([id, c]) => found.set(id, await exists(`/media/clips/${c.file}`, 'video'))),
    ...(Object.keys(AUDIO) as (keyof typeof AUDIO)[]).map(async (k) => {
      for (const p of AUDIO[k]) if (await exists(p, 'audio')) return void (audioFound[k] = p);
    }),
    (async () => {
      if (await exists('/media/brand/near-logo.svg', 'image')) nearLogo = '/media/brand/near-logo.svg';
    })(),
  ]);
}
export const hasClip = (id: ClipId) => found.get(id) ?? false;

// ---- video seeking -------------------------------------------------------------------------

/** Videos on screen this frame and the time each should show. */
const targets = new Map<HTMLVideoElement, number>();

export function syncVideo(v: HTMLVideoElement, time: number, live: boolean) {
  if (RENDER) {
    if (live) targets.set(v, time);
    else targets.delete(v);
    return;
  }
  if (!live || !transport.playing) {
    if (!v.paused) v.pause();
    if (live && Math.abs(v.currentTime - time) > 0.04) v.currentTime = time;
    return;
  }
  if (Math.abs(v.currentTime - time) > 0.25) v.currentTime = time;
  if (v.paused) void v.play().catch(() => {});
}

function seekOne(v: HTMLVideoElement, time: number) {
  return new Promise<void>((resolve) => {
    const done = () => {
      clearTimeout(timer);
      resolve();
    };
    const timer = setTimeout(done, 3000);
    if (v.readyState >= 2 && Math.abs(v.currentTime - time) < 1e-4) return done();
    v.addEventListener(
      'seeked',
      () => {
        if ('requestVideoFrameCallback' in v) v.requestVideoFrameCallback(() => done());
        else done();
      },
      { once: true },
    );
    v.currentTime = time;
  });
}

/** Render mode: waits until every video on screen shows its exact frame. */
export const seekVideos = () => Promise.all([...targets].map(([v, t]) => seekOne(v, t)));
