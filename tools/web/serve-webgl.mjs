// Serves a Unity WebGL build the way the real host does (docs 13 §5.1): the Brotli files with
// Content-Encoding: br and their real content type, everything else as is. No dependencies.
// Usage: node tools/web/serve-webgl.mjs [dir=Builds/WebGL-dev] [port=8000]
import { createServer } from 'node:http';
import { createReadStream, statSync } from 'node:fs';
import { extname, join, normalize, resolve } from 'node:path';

const root = resolve(process.argv[2] ?? 'Builds/WebGL-dev');
const port = Number(process.argv[3] ?? 8000);
const types = {
  '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.mjs': 'application/javascript',
  '.css': 'text/css', '.json': 'application/json', '.wasm': 'application/wasm', '.data': 'application/octet-stream',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.webmanifest': 'application/manifest+json',
};

createServer((req, res) => {
  const url = decodeURIComponent((req.url ?? '/').split('?')[0]);
  let path = normalize(join(root, url));
  if (!path.startsWith(root)) { res.writeHead(403).end(); return; }
  try { if (statSync(path).isDirectory()) path = join(path, 'index.html'); } catch { res.writeHead(404).end('not found'); return; }
  let size;
  try { size = statSync(path).size; } catch { res.writeHead(404).end('not found'); return; }
  const headers = { 'Content-Length': size, 'Cache-Control': 'no-cache' };
  let name = path;
  if (name.endsWith('.br')) { headers['Content-Encoding'] = 'br'; name = name.slice(0, -3); }
  else if (name.endsWith('.gz')) { headers['Content-Encoding'] = 'gzip'; name = name.slice(0, -3); }
  headers['Content-Type'] = types[extname(name)] ?? 'application/octet-stream';
  res.writeHead(200, headers);
  createReadStream(path).pipe(res);
}).listen(port, () => console.log(`WebGL build ${root} on http://localhost:${port}`));
