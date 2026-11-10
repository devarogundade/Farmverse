import react from '@vitejs/plugin-react';
import { createReadStream, existsSync, statSync } from 'node:fs';
import { extname, join, normalize } from 'node:path';
import { defineConfig, type Plugin } from 'vite';

const MEDIA = join(import.meta.dirname, 'media');
/** The game's own sound effects (git-ignored, paid pack): read in place, never copied. */
const GAME_AUDIO = join(import.meta.dirname, '..', '..', 'Assets', 'Farmverse', 'Content', 'Audio');
const TYPES: Record<string, string> = {
  '.mp4': 'video/mp4', '.webm': 'video/webm', '.mov': 'video/quicktime',
  '.wav': 'audio/wav', '.ogg': 'audio/ogg', '.mp3': 'audio/mpeg', '.m4a': 'audio/mp4',
  '.svg': 'image/svg+xml', '.png': 'image/png', '.webp': 'image/webp', '.json': 'application/json',
};

/**
 * Serves web/trailer/media at /media (your clips and audio), with byte ranges so <video> can seek.
 * The shared game art stays the public dir, as for the other sites.
 */
function media(): Plugin {
  return {
    name: 'trailer-media',
    configureServer(server) {
      for (const [route, root] of [['/media', MEDIA], ['/game-audio', GAME_AUDIO]] as const)
      server.middlewares.use(route, (req, res, next) => {
        const path = normalize(join(root, decodeURIComponent((req.url ?? '/').split('?')[0])));
        if (!path.startsWith(root) || !existsSync(path) || !statSync(path).isFile()) {
          res.statusCode = 404;
          res.end();
          return;
        }
        const size = statSync(path).size;
        res.setHeader('Content-Type', TYPES[extname(path).toLowerCase()] ?? 'application/octet-stream');
        res.setHeader('Accept-Ranges', 'bytes');
        res.setHeader('Cache-Control', 'no-cache');
        const range = /bytes=(\d*)-(\d*)/.exec(req.headers.range ?? '');
        if (range) {
          const start = range[1] ? Number(range[1]) : 0;
          const end = range[2] ? Number(range[2]) : size - 1;
          res.statusCode = 206;
          res.setHeader('Content-Range', `bytes ${start}-${end}/${size}`);
          res.setHeader('Content-Length', end - start + 1);
          createReadStream(path, { start, end }).pipe(res);
        } else {
          res.setHeader('Content-Length', size);
          if (req.method === 'HEAD') return void res.end();
          createReadStream(path).pipe(res);
        }
        void next;
      });
    },
  };
}

export default defineConfig({
  plugins: [react(), media()],
  publicDir: '../shared/public',
  server: { port: 5179, strictPort: true },
});
