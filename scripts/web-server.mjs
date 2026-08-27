import http from 'node:http';
import { createReadStream } from 'node:fs';
import { stat } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const contentTypes = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css', '.wasm': 'application/wasm', '.json': 'application/json', '.txt': 'text/plain; charset=utf-8', '.png': 'image/png', '.ico': 'image/x-icon', '.data': 'application/octet-stream' };
const maxBodyBytes = 1_048_576;

function json(response, status, code) {
  if (response.headersSent || response.destroyed) return;
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  response.end(JSON.stringify({ code }));
}

export function createWebServer({ root = path.join(repository, 'artifacts/unity/web'), apiUrl = 'http://127.0.0.1:5080', mount = '/' } = {}) {
  const base = path.resolve(root);
  const upstream = new URL(apiUrl);
  if (mount !== '/' && !/^\/simops_web_spec_[a-f0-9]{32}\/$/.test(mount)) throw new Error('Invalid test mount.');
  // This helper is local-only. Public deployment needs its own HTTPS gateway.
  if (upstream.protocol !== 'http:' || upstream.hostname !== '127.0.0.1' ||
      !['5080', '5081'].includes(upstream.port) || upstream.username || upstream.password ||
      upstream.pathname !== '/' || upstream.search || upstream.hash) throw new Error('Only local SimOps API ports 5080/5081 are allowed.');

  return http.createServer(async (request, response) => {
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('Cache-Control', 'no-store');
    try {
      const host = request.headers.host ?? '';
      if (!/^(127\.0\.0\.1|localhost):\d+$/.test(host)) return json(response, 403, 'INVALID_HOST');
      // Block cross-site callers without broad CORS. Unity itself uses same-origin requests.
      if (request.headers.origin && request.headers.origin !== 'http://' + host) return json(response, 403, 'CROSS_ORIGIN_BLOCKED');
      const url = new URL(request.url, 'http://' + host);
      if (url.pathname.startsWith('/api/')) {
        const readable = /^\/api\/v1\/public\/(?:seasons\/active|seasons\/[a-fA-F0-9-]+\/(?:config|leaderboard))$/;
        const player = /^\/api\/v1\/player\/(?:register|tickets|runs(?:\/[a-fA-F0-9-]+)?)$/;
        if (!readable.test(url.pathname) && !player.test(url.pathname)) return json(response, 404, 'ROUTE_NOT_EXPOSED');
        if (!['GET', 'POST'].includes(request.method) || (readable.test(url.pathname) && request.method !== 'GET')) return json(response, 405, 'METHOD_NOT_ALLOWED');
        const chunks = []; let size = 0; let tooLarge = false;
        for await (const chunk of request) {
          size += chunk.length;
          if (size > maxBodyBytes) { tooLarge = true; json(response, 413, 'BODY_TOO_LARGE'); break; }
          chunks.push(chunk);
        }
        if (tooLarge) return;
        const headers = { Accept: 'application/json' };
        if (request.headers.authorization) headers.Authorization = request.headers.authorization;
        if (request.headers['content-type']) headers['Content-Type'] = request.headers['content-type'];
        const body = Buffer.concat(chunks); headers['Content-Length'] = body.length;
        const proxy = http.request(new URL(url.pathname + url.search, upstream), { method: request.method, headers }, incoming => {
          response.writeHead(incoming.statusCode, {
            'Content-Type': incoming.headers['content-type'] ?? 'application/json',
            'Cache-Control': 'no-store',
          });
          incoming.pipe(response);
        });
        proxy.setTimeout(20_000, () => proxy.destroy(new Error('API timeout')));
        proxy.on('error', () => json(response, 503, 'LOCAL_API_UNAVAILABLE'));
        response.on('close', () => proxy.destroy());
        proxy.end(body);
        return;
      }
      if (!['GET', 'HEAD'].includes(request.method)) return json(response, 405, 'METHOD_NOT_ALLOWED');
      if (!url.pathname.startsWith(mount)) return json(response, 404, 'NOT_FOUND');
      const decoded = decodeURIComponent('/' + url.pathname.slice(mount.length));
      if (decoded.includes('\0') || decoded.includes('\\')) return json(response, 400, 'INVALID_PATH');
      const filename = path.resolve(base, '.' + (decoded === '/' ? '/index.html' : decoded));
      if (!filename.startsWith(base + path.sep)) return json(response, 403, 'INVALID_PATH');
      const extension = path.extname(filename.endsWith('.gz') ? filename.slice(0, -3) : filename);
      if (!contentTypes[extension]) return json(response, 404, 'NOT_FOUND');
      const info = await stat(filename);
      if (!info.isFile()) return json(response, 404, 'NOT_FOUND');
      response.setHeader('Content-Type', contentTypes[extension]);
      response.setHeader('Content-Length', info.size);
      if (filename.endsWith('.gz')) response.setHeader('Content-Encoding', 'gzip');
      response.writeHead(200);
      if (request.method === 'HEAD') response.end();
      else createReadStream(filename).on('error', () => response.destroy()).pipe(response);
    } catch (error) {
      json(response, error.code === 'ENOENT' ? 404 : 400, error.code === 'ENOENT' ? 'NOT_FOUND' : 'INVALID_REQUEST');
    }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  const value = (flag, fallback) => args.includes(flag) ? args[args.indexOf(flag) + 1] : fallback;
  const port = Number(value('--port', '5174'));
  if (![5174, 5175].includes(port)) throw new Error('Use local web port 5174 (play) or 5175 (test).');
  const mount = value('--mount', '/');
  const server = createWebServer({ apiUrl: value('--api-url', 'http://127.0.0.1:5080'), mount });
  server.on('error', error => { console.error(error.message); process.exitCode = 1; });
  server.listen(port, '127.0.0.1', () => console.log(`SimOps Web: http://127.0.0.1:${port}${mount} (local only)`));
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => server.close());
}
