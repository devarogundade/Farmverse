#!/usr/bin/env node
/**
 * Renders the trailer to MP4, frame by frame, from the running dev server (npm run dev:trailer).
 *
 *   node scripts/render.mjs                         → out/farmverse-trailer.mp4 (with audio if found)
 *   node scripts/render.mjs --stills 4,52.5,95      → out/stills/*.png at those seconds
 *   node scripts/render.mjs --from 48 --to 66       → just that part (out/part-48-66.mp4)
 *   node scripts/render.mjs --remux                 → redo only the sound on the last full render
 *
 * Options: --url http://localhost:5179  --workers 3  --out <file>  --chrome <path to chrome.exe>
 * Sound: the game's effects (src/sfx.ts, from Assets/Farmverse/Content/Audio), media/audio/voiceover.(wav|mp3|m4a)
 * at full level and media/audio/music.(mp3|wav|m4a) under it.
 */
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ffmpeg from 'ffmpeg-static';
import { chromium } from 'playwright-core';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, 'out');
/** The game's own audio (git-ignored, paid pack): read in place, never copied. */
const GAME_AUDIO = join(ROOT, '..', '..', 'Assets', 'Farmverse', 'Content', 'Audio');

const arg = (name, def) => {
  const i = process.argv.indexOf(`--${name}`);
  return i > 0 ? process.argv[i + 1] : def;
};
const URL = arg('url', 'http://localhost:5179');
const WORKERS = Number(arg('workers', 3));
const STILLS = arg('stills');
const CHROME =
  arg('chrome') ??
  process.env.CHROME_PATH ??
  ['C:/Program Files/Google/Chrome/Application/chrome.exe', '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome', '/usr/bin/google-chrome'].find(existsSync);

async function openPage(browser) {
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  page.on('pageerror', (e) => console.error('[page]', e.message));
  await page.goto(`${URL}/?render=1`, { waitUntil: 'load' });
  await page.waitForFunction(() => window.__trailer, null, { timeout: 60_000 });
  const info = await page.evaluate(() => window.__trailer.ready());
  return { page, info };
}

function launch() {
  return chromium.launch({
    executablePath: CHROME,
    headless: true,
    args: ['--ignore-gpu-blocklist', '--enable-gpu-rasterization', '--use-angle=d3d11', '--autoplay-policy=no-user-gesture-required', '--hide-scrollbars', '--mute-audio'],
  });
}

/** Whether a recording has a sound track. */
async function hasAudio(file) {
  const p = spawn(ffmpeg, ['-hide_banner', '-i', file], { stdio: ['ignore', 'ignore', 'pipe'] });
  let err = '';
  p.stderr.on('data', (d) => (err += d));
  await new Promise((r) => p.on('close', r));
  return /Stream #.*Audio:/.test(err);
}

function run(args, stdin = false) {
  const p = spawn(ffmpeg, ['-hide_banner', '-loglevel', 'error', '-y', ...args], { stdio: [stdin ? 'pipe' : 'ignore', 'inherit', 'inherit'] });
  const done = new Promise((res, rej) => p.on('close', (c) => (c === 0 ? res() : rej(new Error(`ffmpeg exited ${c}`)))));
  return { p, done };
}

/** Renders frames [a, b) into one video-only file. */
async function renderRange(browser, a, b, fps, file, onFrame) {
  const { page } = await openPage(browser);
  const cdp = await page.context().newCDPSession(page);
  const enc = run(['-f', 'image2pipe', '-framerate', String(fps), '-c:v', 'mjpeg', '-i', '-', '-c:v', 'libx264', '-preset', 'medium', '-crf', '16', '-pix_fmt', 'yuv420p', '-r', String(fps), file], true);
  for (let f = a; f < b; f++) {
    await page.evaluate((t) => window.__trailer.seek(t), f / fps);
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: 94, optimizeForSpeed: true });
    if (!enc.p.stdin.write(Buffer.from(data, 'base64'))) await new Promise((r) => enc.p.stdin.once('drain', r));
    onFrame();
  }
  enc.p.stdin.end();
  await enc.done;
  await page.close();
}

async function stills(times) {
  const browser = await launch();
  const { page } = await openPage(browser);
  const dir = join(OUT, 'stills');
  mkdirSync(dir, { recursive: true });
  for (const t of times) {
    await page.evaluate((v) => window.__trailer.seek(v), t);
    const file = join(dir, `t${String(t).replace('.', '_')}.png`);
    await page.screenshot({ path: file });
    console.log(file);
  }
  await browser.close();
}

async function film() {
  const browser = await launch();
  const { page, info } = await openPage(browser);
  await page.close();
  const { fps, total, audio } = info;
  const from = Number(arg('from', 0));
  const to = Math.min(total, Number(arg('to', total)));
  const whole = from === 0 && to === total;
  const out = resolve(arg('out', join(OUT, whole ? 'farmverse-trailer.mp4' : `part-${from}-${to}.mp4`)));
  mkdirSync(OUT, { recursive: true });
  const parts = join(OUT, 'parts');
  rmSync(parts, { recursive: true, force: true });
  mkdirSync(parts, { recursive: true });

  const first = Math.round(from * fps);
  const last = Math.round(to * fps);
  const n = last - first;
  const per = Math.ceil(n / WORKERS);
  let done = 0;
  const t0 = Date.now();
  const tick = () => {
    done++;
    if (done % 30 === 0 || done === n) {
      const s = (Date.now() - t0) / 1000;
      const eta = (s / done) * (n - done);
      process.stdout.write(`\r${done}/${n} frames · ${(done / s).toFixed(1)} fps · ${Math.round(eta)} s left   `);
    }
  };
  const files = [];
  await Promise.all(
    Array.from({ length: WORKERS }, (_, w) => {
      const a = first + w * per;
      const b = Math.min(last, a + per);
      if (a >= b) return null;
      const file = join(parts, `p${w}.mp4`);
      files[w] = file;
      return renderRange(browser, a, b, fps, file, tick);
    }),
  );
  await browser.close();
  console.log('\nJoining…');

  const list = join(parts, 'list.txt');
  writeFileSync(list, files.filter(Boolean).map((f) => `file '${f.replace(/\\/g, '/')}'`).join('\n'));
  // Keep the silent picture, so the sound can be redone later without re-rendering (--remux).
  const video = join(OUT, whole ? 'video.mp4' : `video-${from}-${to}.mp4`);
  await run(['-f', 'concat', '-safe', '0', '-i', list, '-c', 'copy', video]).done;
  rmSync(parts, { recursive: true, force: true });
  await mux(video, info, from, to, out);
}

/**
 * Lays the sound under the picture: the game's sound effects at their cues, the voiceover at full
 * level and music under it, if those are in media/audio. A limiter keeps the sum from clipping.
 */
async function mux(video, info, from, to, out) {
  const { audio, sfx, clips = [] } = info;
  const len = to - from;
  const voice = audio.voice && join(ROOT, audio.voice.replace(/^\//, ''));
  const music = audio.music && join(ROOT, audio.music.replace(/^\//, ''));
  const args = ['-i', video];
  const chains = [];
  const bus = [];
  let i = 1;

  const cues = sfx.filter((c) => c.t >= from && c.t < to && existsSync(join(GAME_AUDIO, c.file)));
  const missing = sfx.filter((c) => !existsSync(join(GAME_AUDIO, c.file)));
  if (missing.length) console.warn(`Missing sound files: ${[...new Set(missing.map((c) => c.file))].join(', ')}`);
  const sfxBus = [];
  for (const c of cues) {
    if (c.loop || c.dur) args.push('-stream_loop', '-1');
    args.push('-i', join(GAME_AUDIO, c.file));
    const dur = c.dur ?? 30;
    const ms = Math.round((c.t - from) * 1000);
    const f = [`atrim=0:${dur}`, 'asetpts=PTS-STARTPTS', 'aformat=sample_rates=48000:channel_layouts=stereo'];
    if (c.fadeIn) f.push(`afade=t=in:st=0:d=${c.fadeIn}`);
    if (c.dur && c.fadeOut) f.push(`afade=t=out:st=${Math.max(0, c.dur - c.fadeOut)}:d=${c.fadeOut}`);
    f.push(`volume=${c.vol ?? 0.6}`, `adelay=${ms}:all=1`);
    chains.push(`[${i}:a]${f.join(',')}[s${i}]`);
    sfxBus.push(`[s${i}]`);
    i++;
  }
  // The gameplay clips keep their own game sound, in their slots (skipped if a recording has none).
  const clipBus = [];
  for (const c of clips) {
    const file = join(ROOT, c.file);
    if (c.t + c.dur <= from || c.t >= to || !(await hasAudio(file))) continue;
    args.push('-ss', String(c.start), '-t', String(c.dur), '-i', file);
    const ms = Math.max(0, Math.round((c.t - from) * 1000));
    chains.push(`[${i}:a]asetpts=PTS-STARTPTS,aformat=sample_rates=48000:channel_layouts=stereo,afade=t=in:st=0:d=0.3,afade=t=out:st=${Math.max(0, c.dur - 0.5)}:d=0.5,volume=${voice ? 0.55 : 0.9},adelay=${ms}:all=1[c${i}]`);
    clipBus.push(`[c${i}]`);
    i++;
  }
  sfxBus.push(...clipBus);
  if (sfxBus.length) {
    chains.push(`${sfxBus.join('')}amix=inputs=${sfxBus.length}:duration=longest:normalize=0,volume=${voice ? 0.75 : 1}[sfx]`);
    bus.push('[sfx]');
  }
  if (voice) {
    args.push('-ss', String(from), '-i', voice);
    chains.push(`[${i++}:a]aformat=sample_rates=48000:channel_layouts=stereo,volume=1.0[v]`);
    bus.push('[v]');
  }
  if (music) {
    args.push('-ss', String(from), '-i', music);
    chains.push(`[${i++}:a]aformat=sample_rates=48000:channel_layouts=stereo,volume=${voice ? 0.2 : 0.6},afade=t=out:st=${Math.max(0, len - 3)}:d=3[m]`);
    bus.push('[m]');
  }
  if (bus.length) {
    chains.push(`${bus.join('')}amix=inputs=${bus.length}:duration=longest:normalize=0,alimiter=limit=0.95,apad[a]`);
    args.push('-filter_complex', chains.join(';'), '-map', '0:v', '-map', '[a]', '-c:a', 'aac', '-b:a', '192k');
  }
  args.push('-c:v', 'copy', '-t', String(len), '-movflags', '+faststart', out);
  await run(args).done;
  const parts = [cues.length && `${cues.length} sound effects`, clipBus.length && `game sound from ${clipBus.length} clips`, voice && 'voiceover', music && 'music'].filter(Boolean);
  console.log(`Done: ${out} (${parts.length ? parts.join(', ') : 'no sound'})`);
}

/** Redoes only the sound on the last full render's picture (after adding a voiceover or music). */
async function remux() {
  const video = join(OUT, 'video.mp4');
  if (!existsSync(video)) throw new Error('No out/video.mp4 yet: render the whole trailer once first.');
  const browser = await launch();
  const { page, info } = await openPage(browser);
  await page.close();
  await browser.close();
  await mux(video, info, 0, info.total, resolve(arg('out', join(OUT, 'farmverse-trailer.mp4'))));
}

if (!CHROME) {
  console.error('Chrome not found: pass --chrome <path> or set CHROME_PATH.');
  process.exit(1);
}
await (STILLS ? stills(STILLS.split(',').map(Number)) : process.argv.includes('--remux') ? remux() : film());
