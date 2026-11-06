// Serves a Unity WebGL build the way the real host does (docs 13 §5.1): the Brotli files with
// Content-Encoding: br and their real content type, everything else as is, each with an ETag and
// Last-Modified so Unity's browser cache (data caching) notices a rebuild. No dependencies.
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
  let stat;
  try { stat = statSync(path); } catch { res.writeHead(404).end('not found'); return; }
  // a rebuild changes size or time: the cached copy is checked against these on every load
  const etag = `"${stat.size.toString(16)}-${Math.floor(stat.mtimeMs).toString(16)}"`;
  const modified = new Date(Math.floor(stat.mtimeMs / 1000) * 1000).toUTCString();
  const since = req.headers['if-modified-since'];
  if (req.headers['if-none-match'] === etag || (!req.headers['if-none-match'] && since && Date.parse(since) >= Date.parse(modified))) {
    res.writeHead(304, { ETag: etag, 'Last-Modified': modified, 'Cache-Control': 'no-cache' }).end();
    return;
  }
  const headers = { 'Content-Length': stat.size, 'Cache-Control': 'no-cache', ETag: etag, 'Last-Modified': modified };
  let name = path;
  if (name.endsWith('.br')) { headers['Content-Encoding'] = 'br'; name = name.slice(0, -3); }
  else if (name.endsWith('.gz')) { headers['Content-Encoding'] = 'gzip'; name = name.slice(0, -3); }
  headers['Content-Type'] = types[extname(name)] ?? 'application/octet-stream';
  res.writeHead(200, headers);
  createReadStream(path).pipe(res);
}).listen(port, () => console.log(`WebGL build ${root} on http://localhost:${port}`));
