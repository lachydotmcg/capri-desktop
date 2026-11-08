// A local copy of the phone page (site/remote) for the page tests, on 127.0.0.1. It keeps every address asked for.

import { createServer } from 'node:http';
import { readFileSync } from 'node:fs';
import { dirname, extname, join, normalize } from 'node:path';
import { fileURLToPath } from 'node:url';

export const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
export const SITE = join(ROOT, 'site', 'remote');

const TYPES = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.svg': 'image/svg+xml', '.png': 'image/png', '.json': 'application/json', '.webmanifest': 'application/manifest+json', '.woff2': 'font/woff2', '.ico': 'image/x-icon' };

export const startSite = port => new Promise((resolve, reject) => {
  const asked = [];
  const server = createServer((request, response) => {
    asked.push(request.url);
    const path = new URL(request.url, 'http://127.0.0.1').pathname;
    const file = normalize(join(SITE, path === '/' ? 'index.html' : decodeURIComponent(path)));
    if (!file.startsWith(SITE)) { response.writeHead(403); response.end(); return; }
    try {
      const body = readFileSync(file);
      response.writeHead(200, { 'content-type': TYPES[extname(file)] || 'application/octet-stream', 'cache-control': 'no-store' });
      response.end(body);
    } catch {
      response.writeHead(404);
      response.end();
    }
  });
  server.on('error', reject);
  server.listen(port, '127.0.0.1', () => resolve({ asked, close: () => new Promise(done => server.close(done)) }));
});

// The page's early access, passed the way the page keeps it (with the hash in app.js, never the password), and its home
// screen guide marked as seen.
export const earlyAccess = () => {
  const early = /const EARLY_HASH = '([0-9a-f]{64})'/.exec(readFileSync(join(SITE, 'app.js'), 'utf8'));
  if (!early) throw new Error('the early access hash is not in app.js');
  return `try { localStorage.setItem('capri-remote-early-access', '${early[1]}'); localStorage.setItem('capri-remote-home-guide', '1'); } catch (error) {}`;
};
