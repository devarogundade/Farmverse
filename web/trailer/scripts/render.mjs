#!/usr/bin/env node
/**
 * Renders the trailer to MP4, frame by frame, from the running dev server (npm run dev:trailer).
 *
 *   node scripts/render.mjs                         → out/farmverse-trailer.mp4 (with audio if found)
 *   node scripts/render.mjs --stills 4,52.5,95      → out/stills/*.png at those seconds
 *   node scripts/render.mjs --from 48 --to 66       → just that part (out/part-48-66.mp4)
 *
 * Options: --url http://localhost:5179  --workers 3  --out <file>  --chrome <path to chrome.exe>
 * Audio: media/audio/voiceover.(wav|mp3|m4a) at full level, media/audio/music.(mp3|wav|m4a) under it.
 */
import { spawn } from 'node:child_process';
import { existsSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import ffmpeg from 'ffmpeg-static';
import { chromium } from 'playwright-core';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const OUT = join(ROOT, 'out');

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
  const voice = audio.voice && join(ROOT, audio.voice.replace(/^\//, ''));
  const music = audio.music && join(ROOT, audio.music.replace(/^\//, ''));
  const args = ['-f', 'concat', '-safe', '0', '-i', list];
  if (voice) args.push('-ss', String(from), '-i', voice);
  if (music) args.push('-ss', String(from), '-i', music);
  const len = to - from;
  if (voice || music) {
    const chains = [];
    const mix = [];
    let i = 1;
    if (voice) {
      chains.push(`[${i++}:a]volume=1.0[v]`);
      mix.push('[v]');
    }
    if (music) {
      chains.push(`[${i++}:a]volume=${voice ? 0.2 : 0.7},afade=t=out:st=${Math.max(0, len - 3)}:d=3[m]`);
      mix.push('[m]');
    }
    chains.push(`${mix.join('')}amix=inputs=${mix.length}:duration=longest:normalize=0[a]`);
    args.push('-filter_complex', chains.join(';'), '-map', '0:v', '-map', '[a]', '-c:a', 'aac', '-b:a', '192k');
  }
  args.push('-c:v', 'copy', '-t', String(len), '-movflags', '+faststart', out);
  await run(args).done;
  rmSync(parts, { recursive: true, force: true });
  console.log(`Done: ${out}${voice || music ? '' : ' (no audio found in media/audio)'}`);
}

if (!CHROME) {
  console.error('Chrome not found: pass --chrome <path> or set CHROME_PATH.');
  process.exit(1);
}
await (STILLS ? stills(STILLS.split(',').map(Number)) : film());
