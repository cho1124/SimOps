import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { gzipSync } from 'node:zlib';
import { createWebServer } from './web-server.mjs';

test('local Web host: assets, compression, proxy isolation and path/origin guards', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'simops-web-spec-'));
  await writeFile(path.join(root, 'index.html'), '<h1>arena</h1>');
  await writeFile(path.join(root, 'game.wasm.gz'), gzipSync(Buffer.from('wasm-fixture')));
  const server = createWebServer({ root });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const port = server.address().port;
  const get = (url, options = {}) => new Promise((resolve, reject) => {
    const request = http.request({ hostname: '127.0.0.1', port, path: url, ...options }, response => {
      const chunks = []; response.on('data', chunk => chunks.push(chunk));
      response.on('end', () => resolve({ status: response.statusCode, headers: response.headers, body: Buffer.concat(chunks) }));
    });
    request.on('error', reject); request.end();
  });
  try {
    await t.test('serves game HTML without caching', async () => { const r = await get('/'); assert.equal(r.status, 200); assert.equal(r.headers['cache-control'], 'no-store'); });
    await t.test('wasm gzip MIME/encoding and HEAD length', async () => { const r = await get('/game.wasm.gz', { method: 'HEAD' }); assert.equal(r.status, 200); assert.equal(r.headers['content-type'], 'application/wasm'); assert.equal(r.headers['content-encoding'], 'gzip'); assert.equal(r.body.length, 0); });
    await t.test('admin API is never exposed', async () => assert.equal((await get('/api/v1/liveops/publications')).status, 404));
    await t.test('cross-origin calls rejected', async () => assert.equal((await get('/api/v1/player/register', { method: 'POST', headers: { Origin: 'https://untrusted.example' } })).status, 403));
    await t.test('DNS rebinding Host rejected', async () => assert.equal((await get('/', { headers: { Host: 'untrusted.example' } })).status, 403));
    await t.test('encoded traversal cannot escape asset root', async () => assert.notEqual((await get('/..%2f..%2fREADME.md')).status, 200));
    await t.test('Windows separator path rejected', async () => assert.equal((await get('/..%5cREADME.md')).status, 400));
    await t.test('malformed path rejected', async () => assert.equal((await get('/%zz')).status, 400));
    await t.test('unexpected methods rejected', async () => assert.equal((await get('/', { method: 'POST' })).status, 405));
    await t.test('public writes rejected', async () => assert.equal((await get('/api/v1/public/seasons/active', { method: 'POST' })).status, 405));
    await t.test('remote upstream rejected', () => assert.throws(() => createWebServer({ apiUrl: 'https://example.com' })));
    await t.test('alternate localhost API port rejected', () => assert.throws(() => createWebServer({ apiUrl: 'http://127.0.0.1:9999' })));
    await t.test('oversized player body returns 413 without forwarding', async () => {
      const response = await fetch(`http://127.0.0.1:${port}/api/v1/player/runs`, { method: 'POST', body: 'x'.repeat(1_048_577) });
      assert.equal(response.status, 413); assert.equal((await response.json()).code, 'BODY_TOO_LARGE');
    });
    await t.test('isolated mount serves only its namespaced asset URL', async () => {
      const mount = '/simops_web_spec_' + 'a'.repeat(32) + '/';
      const isolated = createWebServer({ root, mount });
      await new Promise(resolve => isolated.listen(0, '127.0.0.1', resolve));
      const base = `http://127.0.0.1:${isolated.address().port}`;
      try {
        assert.equal((await fetch(base + mount)).status, 200);
        assert.equal((await fetch(base + '/')).status, 404);
        assert.equal((await fetch(base + '/simops_web_spec_' + 'b'.repeat(32) + '/')).status, 404);
      } finally { await new Promise(resolve => isolated.close(resolve)); }
    });
    await t.test('arbitrary mount rejected', () => assert.throws(() => createWebServer({ mount: '/other/' })));
  } finally {
    await new Promise(resolve => server.close(resolve));
    // Only the mkdtemp-created fixture directory is removed, never user files.
    assert.equal(path.dirname(path.resolve(root)), path.resolve(os.tmpdir()));
    assert.ok(path.basename(root).startsWith('simops-web-spec-'));
    await rm(root, { recursive: true, force: true });
  }
});

test('player API proxy preserves only player headers and handles unavailable API', async t => {
  let observed;
  const api = http.createServer(async (req, res) => {
    const chunks = []; for await (const chunk of req) chunks.push(chunk);
    observed = { url: req.url, headers: req.headers, body: Buffer.concat(chunks).toString() };
    res.writeHead(202, { 'Content-Type': 'application/json' }); res.end('{"status":"queued"}');
  });
  await new Promise((resolve, reject) => { api.once('error', reject); api.listen(5081, '127.0.0.1', resolve); });
  const web = createWebServer({ apiUrl: 'http://127.0.0.1:5081' });
  await new Promise(resolve => web.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${web.address().port}`;
  try {
    await t.test('forwards player body and bearer, strips administrator key', async () => {
      const response = await fetch(base + '/api/v1/player/runs', { method: 'POST', headers: { 'Content-Type': 'application/json', Authorization: 'Bearer fixture-only', 'X-SimOps-Admin-Key': 'must-not-forward' }, body: '{"actions":[]}' });
      assert.equal(response.status, 202); assert.equal((await response.json()).status, 'queued');
      assert.equal(observed.body, '{"actions":[]}'); assert.equal(observed.headers.authorization, 'Bearer fixture-only');
      assert.equal(observed.headers['x-simops-admin-key'], undefined);
    });
    await t.test('preserves leaderboard query parameters', async () => {
      await fetch(base + '/api/v1/public/seasons/10000000-0000-0000-0000-000000000002/leaderboard?limit=3');
      assert.ok(observed.url.endsWith('?limit=3'));
    });
    await new Promise(resolve => api.close(resolve));
    await t.test('unavailable backend becomes a readable 503', async () => { const response = await fetch(base + '/api/v1/public/seasons/active'); assert.equal(response.status, 503); assert.equal((await response.json()).code, 'LOCAL_API_UNAVAILABLE'); });
  } finally { await new Promise(resolve => web.close(resolve)); if (api.listening) await new Promise(resolve => api.close(resolve)); }
});
