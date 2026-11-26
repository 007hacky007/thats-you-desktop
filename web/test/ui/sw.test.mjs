import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const SOURCE = readFileSync(new URL('../../sw.js', import.meta.url), 'utf8');
const SCOPE = 'http://127.0.0.1:8088/iwyk/';
const V1 = '0123456789abcdef';
const V2 = 'fedcba9876543210';

function makeResponse(body, status = 200) {
  return {
    ok: status >= 200 && status < 300, status, type: 'basic', body,
    clone() { return makeResponse(body, status); },
    async json() { return typeof body === 'string' ? JSON.parse(body) : body; },
  };
}

function makeEnv(files) {
  const store = new Map(); // cache name -> Map(url -> response)
  const fetched = [];
  const env = { files, offline: false };
  const keyOf = (r) => (typeof r === 'string' ? r : r.url);
  const caches = {
    async open(name) {
      if (!store.has(name)) store.set(name, new Map());
      const m = store.get(name);
      return {
        async put(r, res) { m.set(keyOf(r), res); },
        async match(r) { return m.get(keyOf(r)); },
      };
    },
    async keys() { return Array.from(store.keys()); },
    async delete(name) { return store.delete(name); },
  };
  const handlers = {};
  const self = {
    registration: { scope: SCOPE },
    location: { origin: 'http://127.0.0.1:8088' },
    addEventListener(type, fn) { handlers[type] = fn; },
    async skipWaiting() { env.skipped = true; },
    clients: { async claim() { env.claimed = true; } },
  };
  const fetch = async (r) => {
    const url = keyOf(r);
    fetched.push(url);
    if (env.offline) throw new TypeError('offline');
    const rel = url.startsWith(SCOPE) ? url.slice(SCOPE.length) : url;
    if (!(rel in env.files)) return makeResponse('', 404);
    return makeResponse(env.files[rel]);
  };
  const context = vm.createContext({ self, caches, fetch, URL, console, Promise, Date });
  vm.runInContext(SOURCE, context, { filename: 'sw.js' });
  async function dispatch(type, extra = {}) {
    let p = null;
    let responded = null;
    const waits = [];
    const ev = { ...extra, waitUntil(x) { waits.push(x); p = x; }, respondWith(x) { responded = x; } };
    handlers[type](ev);
    if (p) await p;
    const res = responded ? await responded : null;
    await Promise.all(waits);
    return res;
  }
  const req = (path, mode = 'cors', method = 'GET') => ({ url: SCOPE + path, mode, method, headers: { get: () => null } });
  return { env, store, fetched, context, dispatch, req };
}

test('pure helpers', () => {
  const { context } = makeEnv({});
  assert.equal(context.cacheNameFor(V1), `iwyk-${V1}`);
  assert.equal(context.parsePrecache(null), null);
  assert.equal(context.parsePrecache({ version: 'xyz', files: [] }), null);
  assert.equal(context.parsePrecache({ version: V1 }), null);
  assert.equal(context.parsePrecache({ version: V1, files: ['../x'] }), null);
  assert.equal(context.parsePrecache({ version: V1, files: ['https://evil.invalid/a.js'] }), null);
  assert.equal(context.parsePrecache({ version: V1, files: ['/abs.js'] }), null);
  const pc = context.parsePrecache({ version: V1, files: ['index.html', 'js/main.js', 'sw.js', 'precache.json', 'index.html'] });
  assert.equal(pc.version, V1);
  assert.deepEqual(Array.from(pc.files), ['index.html', 'js/main.js']);
  assert.deepEqual(Array.from(context.cachesToDelete([`iwyk-${V1}`, `iwyk-${V2}`, 'iwyk-runtime', 'other-app'], `iwyk-${V2}`)), [`iwyk-${V1}`, 'iwyk-runtime']);
});

test('install precaches every listed file and activate removes old caches', async () => {
  const files = {
    'precache.json': { version: V1, files: ['index.html', 'js/main.js', 'art/manifest.json'] },
    'index.html': '<html>', 'js/main.js': 'main', 'art/manifest.json': '{}',
  };
  const { env, store, dispatch } = makeEnv(files);
  store.set('iwyk-aaaaaaaaaaaaaaaa', new Map());
  store.set('iwyk-runtime', new Map());
  store.set('someone-else', new Map());
  await dispatch('install');
  assert.ok(env.skipped);
  const c = store.get(`iwyk-${V1}`);
  assert.deepEqual(Array.from(c.keys()).sort(), [`${SCOPE}art/manifest.json`, `${SCOPE}index.html`, `${SCOPE}js/main.js`]);
  await dispatch('activate');
  assert.ok(env.claimed);
  assert.deepEqual(Array.from(store.keys()).sort(), [`iwyk-${V1}`, 'someone-else']);
});

test('install without precache.json only skips waiting (runtime mode)', async () => {
  const { env, store, dispatch } = makeEnv({});
  await dispatch('install');
  assert.ok(env.skipped);
  assert.equal(store.size, 0);
});

test('a failed precache leaves no half-filled cache', async () => {
  const { store, dispatch } = makeEnv({ 'precache.json': { version: V1, files: ['index.html', 'missing.js'] }, 'index.html': 'x' });
  await assert.rejects(dispatch('install'));
  assert.equal(store.has(`iwyk-${V1}`), false);
});

test('fetch is cache-first with network fill, navigations get index.html offline', async () => {
  const files = { 'precache.json': { version: V1, files: ['index.html', 'js/main.js'] }, 'index.html': 'INDEX', 'js/main.js': 'MAIN', 'js/late.js': 'LATE' };
  const { env, store, fetched, dispatch, req } = makeEnv(files);
  await dispatch('install');
  await dispatch('activate');
  fetched.length = 0;
  env.offline = true;
  const main = await dispatch('fetch', { request: req('js/main.js') });
  assert.equal(main.body, 'MAIN');
  const nav = await dispatch('fetch', { request: req('', 'navigate') });
  assert.equal(nav.body, 'INDEX');
  env.offline = false;
  const late = await dispatch('fetch', { request: req('js/late.js') });
  assert.equal(late.body, 'LATE');
  assert.ok(store.get(`iwyk-${V1}`).has(`${SCOPE}js/late.js`));
  env.offline = true;
  const late2 = await dispatch('fetch', { request: req('js/late.js') });
  assert.equal(late2.body, 'LATE');
});

test('navigation picks up a new build in the background', async () => {
  const files = { 'precache.json': { version: V1, files: ['index.html'] }, 'index.html': 'OLD' };
  const { env, store, dispatch, req } = makeEnv(files);
  await dispatch('install');
  await dispatch('activate');
  env.files = { 'precache.json': { version: V2, files: ['index.html'] }, 'index.html': 'NEW' };
  const first = await dispatch('fetch', { request: req('', 'navigate') });
  assert.equal(first.body, 'OLD');
  assert.deepEqual(Array.from(store.keys()), [`iwyk-${V2}`]);
  const second = await dispatch('fetch', { request: req('', 'navigate') });
  assert.equal(second.body, 'NEW');
});

test('runtime mode is network first with the cache as fallback', async () => {
  const files = { 'index.html': 'I1', 'js/a.js': 'A1' };
  const { env, dispatch, req } = makeEnv(files);
  await dispatch('install');
  await dispatch('activate');
  assert.equal((await dispatch('fetch', { request: req('js/a.js') })).body, 'A1');
  env.files['js/a.js'] = 'A2';
  assert.equal((await dispatch('fetch', { request: req('js/a.js') })).body, 'A2');
  await dispatch('fetch', { request: req('index.html') });
  env.offline = true;
  assert.equal((await dispatch('fetch', { request: req('js/a.js') })).body, 'A2');
  assert.equal((await dispatch('fetch', { request: req('', 'navigate') })).body, 'I1');
});

test('foreign origins, non-GET, sw.js and precache.json are not intercepted', async () => {
  const { dispatch, req } = makeEnv({});
  assert.equal(await dispatch('fetch', { request: { url: 'https://example.invalid/x.js', method: 'GET', mode: 'cors', headers: { get: () => null } } }), null);
  assert.equal(await dispatch('fetch', { request: req('x', 'cors', 'POST') }), null);
  assert.equal(await dispatch('fetch', { request: req('sw.js') }), null);
  assert.equal(await dispatch('fetch', { request: req('precache.json') }), null);
});
