#!/usr/bin/env node
// Vercel refuses files over 100 MB, and the game's Brotli data file is bigger. This splits it, in a copy of the
// WebGL build, into parts that each compress to well under the limit (Build/<name>.data.part<i>.br), and changes the
// page to download the parts (the browser unpacks each one), join them, and hand Unity the joined data.
//
//   node deployment/webgl-split.mjs <build copy dir>
import { readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { brotliCompressSync, brotliDecompressSync, constants as z } from 'node:zlib';

const dir = process.argv[2];
if (!dir) throw new Error('usage: webgl-split.mjs <build copy dir>');
const build = join(dir, 'Build');
const dataFile = readdirSync(build).find((f) => f.endsWith('.data.br'));
if (!dataFile) throw new Error(`no .data.br in ${build}`);
const LIMIT = 90 * 1024 * 1024; // under Vercel's 100 MB, with room to spare

const packed = readFileSync(join(build, dataFile));
const raw = brotliDecompressSync(packed);
const pack = (buf) =>
  brotliCompressSync(buf, {
    params: { [z.BROTLI_PARAM_QUALITY]: 9, [z.BROTLI_PARAM_LGWIN]: 24, [z.BROTLI_PARAM_SIZE_HINT]: buf.length },
  });

let count = Math.max(2, Math.ceil(packed.length / LIMIT) + 1);
let parts;
for (;;) {
  const size = Math.ceil(raw.length / count);
  parts = [];
  for (let i = 0; i < count; i++) parts.push(raw.subarray(i * size, Math.min(raw.length, (i + 1) * size)));
  const packedParts = parts.map(pack);
  if (packedParts.every((p) => p.length < LIMIT)) {
    parts = parts.map((p, i) => ({ name: dataFile.replace(/\.data\.br$/, `.data.part${i}.br`), raw: p.length, packed: packedParts[i] }));
    break;
  }
  count++;
}
for (const p of parts) writeFileSync(join(build, p.name), p.packed);
rmSync(join(build, dataFile));
console.log(`• ${dataFile} (${(packed.length / 2 ** 20).toFixed(1)} MB) → ${parts.length} parts: ${parts.map((p) => `${(p.packed.length / 2 ** 20).toFixed(1)} MB`).join(', ')}`);

// The page: fetch the parts (progress over the unpacked bytes), join them into a Blob, and load Unity from it.
const page = join(dir, 'index.html');
let html = readFileSync(page, 'utf8');
const list = JSON.stringify(parts.map((p) => ({ url: `Build/${p.name}`, size: p.raw })));
const loader = `
    // The data file comes in parts (hosting limit): fetch them, join them, and give Unity the whole.
    var FV_PARTS = ${list};
    function fvLoadData(onProgress) {
      var total = FV_PARTS.reduce(function (s, p) { return s + p.size; }, 0), got = 0;
      return Promise.all(FV_PARTS.map(function (p) {
        return fetch(p.url).then(function (res) {
          if (!res.ok || !res.body) throw new Error('HTTP ' + res.status + ' for ' + p.url);
          var reader = res.body.getReader(), chunks = [];
          return (function pump() {
            return reader.read().then(function (r) {
              if (r.done) return new Blob(chunks);
              chunks.push(r.value); got += r.value.length; onProgress(Math.min(1, got / total));
              return pump();
            });
          })();
        });
      })).then(function (blobs) { return URL.createObjectURL(new Blob(blobs)); });
    }
`;
const call = html.indexOf('createUnityInstance(canvas, {');
if (call < 0) throw new Error('index.html: createUnityInstance call not found');
// insert the helper, then wrap the call so it runs with the joined data
html = html.slice(0, call) + loader + '    var fvBar = document.getElementById(\'fv-bar-fill\');\n    fvLoadData(function (p) { fvBar.style.width = Math.round(p * 80) + \'%\'; }).then(function (dataUrl) {\n    return ' + html.slice(call);
html = html
  .replace(/dataUrl: "Build\/[^"]+\.data\.br",/, 'dataUrl: dataUrl,\n      cacheControl: function (url) { return url.indexOf("blob:") === 0 ? "no-store" : "must-revalidate"; },')
  .replace(
    "document.getElementById('fv-bar-fill').style.width = Math.round(progress * 100) + '%';",
    "fvBar.style.width = Math.round(80 + progress * 20) + '%';",
  );
// close the wrapper after Unity's .then(...) chain: the .catch now covers both steps
html = html.replace(
  /\}\)\.then\(function \(instance\) \{([\s\S]*?)\}\)\.catch\(/,
  (_m, body) => `});\n    }).then(function (instance) {${body}}).catch(`,
);
writeFileSync(page, html);
console.log('• index.html loads the parts');
