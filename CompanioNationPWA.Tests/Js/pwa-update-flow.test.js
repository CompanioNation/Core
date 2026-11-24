// Exercise the real service worker, update coordinator, and refresh UI in browser-like sandboxes.
// Run: node CompanioNationPWA.Tests/Js/pwa-update-flow.test.js
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..', '..', 'CompanioNationPWA');
const workerSource = fs.readFileSync(path.join(root, 'wwwroot', 'service-worker.published.js'), 'utf8');
const pageSource = fs.readFileSync(path.join(root, 'wwwroot', 'pwa-install.js'), 'utf8');
const layoutSource = fs.readFileSync(path.join(root, 'Layout', 'MainLayout.razor'), 'utf8');
const origin = 'https://example.test/';
const markerUrl = new URL('/__cn_precache_verified_v2', origin).href;
const bodyFor = url => 'BODY:' + url;
const hashFor = url => 'sha256-' + crypto.createHash('sha256').update(bodyFor(url)).digest('base64');
const assets = [
    { url: 'app.css', hash: hashFor('app.css') },
    { url: 'app.wasm', hash: hashFor('app.wasm') },
    { url: 'service-worker.js', hash: hashFor('service-worker.js') },
    { url: 'note.txt', hash: hashFor('note.txt') }
];
let checks = 0;
function check(name, fn) { fn(); checks++; }

const urlOf = request => typeof request === 'string' ? new URL(request, origin).href : request.url;
function rawResponse(text) {
    const bytes = Buffer.from(text);
    return {
        ok: true,
        body: text,
        clone() { return this; },
        async arrayBuffer() { return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength); }
    };
}
function responseFor(url) {
    return rawResponse(bodyFor(new URL(url, origin).pathname.replace(/^\//, '')));
}

function makeWorker(options = {}) {
    const handlers = {};
    const storage = new Map([['offline-cache-old', new Map()]]);
    if (options.cached) {
        storage.set('offline-cache-new', new Map(Object.entries(options.cached)
            .map(([url, response]) => [new URL(url, origin).href, response])));
    }
    const fetched = [];
    const errors = [];
    const warnings = [];
    let skips = 0;
    const context = {
        origin: origin.slice(0, -1), URL,
        assetsManifest: { version: 'new', assets: options.assets || assets },
        importScripts() {},
        addEventListener(type, fn) { handlers[type] = fn; },
        skipWaiting() { skips++; return Promise.resolve(); },
        clients: { claim: async () => {}, matchAll: async () => Array(options.clients ?? 1).fill({}) },
        registration: { showNotification: async () => {} },
        console: { info() {}, log() {}, warn(...args) { warnings.push(args); }, error(...args) { errors.push(args); } },
        Request: class {
            constructor(url, options = {}) {
                this.url = new URL(url, origin).href;
                this.integrity = options.integrity;
            }
        },
        Response: class { constructor(body) { this.body = body; } },
        btoa: value => Buffer.from(value, 'binary').toString('base64'),
        crypto: {
            subtle: {
                async digest(algorithm, data) {
                    assert.equal(algorithm, 'SHA-256');
                    const digest = crypto.createHash('sha256').update(Buffer.from(new Uint8Array(data))).digest();
                    return digest.buffer.slice(digest.byteOffset, digest.byteOffset + digest.byteLength);
                }
            }
        },
        fetch: async request => {
            fetched.push(request);
            if (options.responses && options.responses.has(request.url)) return options.responses.get(request.url);
            return options.fail && request.url.endsWith('app.wasm')
                ? { ok: false, status: 503, statusText: 'Unavailable' }
                : responseFor(request.url);
        },
        caches: {
            async open(name) {
                if (!storage.has(name)) storage.set(name, new Map());
                const cache = storage.get(name);
                return {
                    async match(request) { return cache.get(urlOf(request)); },
                    async put(request, response) { cache.set(urlOf(request), response); },
                    async delete(request) { return cache.delete(urlOf(request)); }
                };
            },
            async keys() { return [...storage.keys()]; },
            async delete(name) { return storage.delete(name); }
        }
    };
    context.self = context;
    vm.createContext(context);
    vm.runInContext(workerSource, context);
    function event(type, data, ports) {
        let pending;
        handlers[type]({ data, ports, waitUntil(promise) { pending = promise; } });
        return pending;
    }
    function fetchEvent(url) {
        let pending;
        handlers.fetch({ request: { url: new URL(url, origin).href, method: 'GET', mode: 'cors' }, respondWith(promise) { pending = promise; } });
        return pending;
    }
    return { event, fetchEvent, storage, fetched, errors, warnings, get skips() { return skips; } };
}

function makePage(registration, options = {}) {
    const listeners = new Set();
    const serviceWorker = {
        controller: { name: 'old' },
        register: async () => registration,
        ready: Promise.resolve(registration),
        addEventListener(type, fn) { if (type === 'controllerchange') listeners.add(fn); },
        removeEventListener(type, fn) { if (type === 'controllerchange') listeners.delete(fn); },
        change(worker) { this.controller = worker; for (const fn of [...listeners]) fn(); }
    };
    const context = {
        console: { info() {}, log() {}, warn() {}, error() {} },
        setTimeout(fn, ms) { return ms === 120000 ? 0 : setTimeout(fn, options.fastTimeout && ms > 1000 ? 1 : ms); },
        clearTimeout, setInterval() { return 0; }, clearInterval() {}, atob,
        navigator: { serviceWorker, userAgent: 'Chrome', platform: 'Win32', maxTouchPoints: 0 },
        document: { referrer: '', createElement: () => ({ style: {}, setAttribute() {}, appendChild() {} }), head: { appendChild() {} }, body: { appendChild() {} }, querySelectorAll: () => [], addEventListener() {} },
        localStorage: { getItem() { return null; }, setItem() {}, removeItem() {} },
        matchMedia: () => ({ matches: false, addEventListener() {} }),
        addEventListener() {}, removeEventListener() {}, Notification: { permission: 'granted' },
        PushManager: function () {}
    };
    context.window = context;
    vm.createContext(context);
    vm.runInContext(pageSource, context);
    return { context, serviceWorker, listeners };
}

async function main() {
    {
        const w = makeWorker();
        await w.event('install');
        check('installation neither downloads nor activates an update', () => {
            assert.equal(w.fetched.length, 0); assert.equal(w.skips, 0);
        });
        await w.event('message', { type: 'CN_ACTIVATE_UPDATE' });
        check('explicit activation calls skipWaiting', () => assert.equal(w.skips, 1));
        const replies = [];
        await w.event('message', { type: 'CN_PRECACHE' }, [{ postMessage: message => replies.push(message) }]);
        check('precache includes only supported assets and removes old cache after success', () => {
            assert.equal(w.fetched.length, 2);
            assert.equal(w.storage.get('offline-cache-new').size, 3);
            assert.equal(w.storage.has('offline-cache-old'), false);
        });
        check('precache requests carry the manifest integrity hash', () => {
            assert.deepEqual(w.fetched.map(request => request.integrity).sort(), [hashFor('app.css'), hashFor('app.wasm')].sort());
        });
        check('successful precache records the verification marker', () => {
            assert.ok(w.storage.get('offline-cache-new').has(markerUrl));
        });
        check('precache reports success on the transferred port', () => {
            assert.equal(replies.length, 1);
            assert.equal(replies[0].type, 'CN_PRECACHE_DONE');
            assert.equal(replies[0].ok, true);
        });
        await w.event('message', { type: 'CN_PRECACHE' });
        check('already cached assets are not fetched again', () => assert.equal(w.fetched.length, 2));
    }
    {
        const w = makeWorker({ cached: { 'app.css': rawResponse('TAMPERED') } });
        w.storage.get('offline-cache-new').set(new URL('/__cn_precache_verified', origin).href, rawResponse('new'));
        await w.event('message', { type: 'CN_PRECACHE' });
        check('an old marker cannot prevent a tampered cached asset being refetched', () => {
            assert.equal(w.fetched.length, 2);
            assert.equal(w.storage.get('offline-cache-new').get(new URL('app.css', origin).href).body, 'BODY:app.css');
        });
    }
    {
        const w = makeWorker({ cached: { 'app.css': rawResponse('TAMPERED'), 'app.wasm': responseFor('app.wasm') } });
        w.storage.get('offline-cache-new').set(markerUrl, rawResponse('new'));
        await w.event('message', { type: 'CN_PRECACHE' });
        check('the verification marker suppresses re-verification', () => assert.equal(w.fetched.length, 0));
    }
    {
        const script = { url: 'pwa-install.js', hash: hashFor('pwa-install.js') };
        const responses = new Map();
        const w = makeWorker({ assets: [...assets, script], responses });
        await w.event('message', { type: 'CN_PRECACHE' });
        const cache = w.storage.get('offline-cache-new');
        const scriptUrl = new URL(script.url, origin).href;
        const cssUrl = new URL('app.css', origin).href;
        responses.set(scriptUrl, rawResponse('NEW DEPLOYMENT'));
        assert.equal((await w.fetchEvent(script.url)).body, 'NEW DEPLOYMENT', w.warnings.map(x => x.map(String).join(' ')).join('; '));
        await new Promise(resolve => setImmediate(resolve));
        check('network-first mismatches do not overwrite verified cache entries', () => {
            assert.equal(cache.get(scriptUrl).body, bodyFor(script.url));
        });
        cache.delete(cssUrl);
        responses.set(cssUrl, rawResponse('TAMPERED'));
        assert.equal((await w.fetchEvent('app.css')).body, 'TAMPERED');
        await new Promise(resolve => setImmediate(resolve));
        check('cache-miss mismatches do not enter a verified cache', () => assert.equal(cache.has(cssUrl), false));
        responses.set(cssUrl, responseFor(cssUrl));
        await w.fetchEvent('app.css');
        await new Promise(resolve => setImmediate(resolve));
        check('matching cache-miss responses are cached', () => assert.equal(cache.get(cssUrl).body, bodyFor('app.css')));
        await w.event('message', { type: 'CN_PRECACHE' });
        check('subsequent warm-up keeps verified cached bytes', () => assert.equal(cache.get(scriptUrl).body, bodyFor(script.url)));
    }
    {
        const w = makeWorker({ assets: [{ url: 'app.css' }] });
        await w.fetchEvent('app.css');
        await new Promise(resolve => setImmediate(resolve));
        await w.event('message', { type: 'CN_PRECACHE' });
        check('a missing manifest hash cannot create a trusted cache entry or marker', () => {
            assert.equal(w.storage.get('offline-cache-new').has(new URL('app.css', origin).href), false);
            assert.equal(w.storage.get('offline-cache-new').has(markerUrl), false);
            assert.equal(w.storage.has('offline-cache-old'), true);
            assert.ok(w.errors.length);
        });
    }
    {
        const w = makeWorker({ fail: true });
        await w.event('message', { type: 'CN_PRECACHE' });
        check('incomplete precache retains the old cache and reports the failure', () => {
            assert.equal(w.storage.has('offline-cache-old'), true); assert.ok(w.errors.length);
            assert.equal(w.storage.get('offline-cache-new').has(markerUrl), false);
        });
        const replies = [];
        await w.event('message', { type: 'CN_PRECACHE' }, [{ postMessage: message => replies.push(message) }]);
        await new Promise(resolve => setImmediate(resolve));
        check('precache reports failure on the transferred port', () => {
            assert.equal(replies.length, 1);
            assert.equal(replies[0].type, 'CN_PRECACHE_DONE');
            assert.equal(replies[0].ok, false);
        });
    }
    {
        const w = makeWorker({ clients: 2 });
        await w.event('message', { type: 'CN_PRECACHE' });
        check('multiple tabs retain older caches', () => assert.equal(w.storage.has('offline-cache-old'), true));
    }
    {
        const registration = { scope: origin, installing: null, waiting: null, active: {}, addEventListener() {}, async update() {} };
        const p = makePage(registration);
        const worker = { postMessage(message) { assert.equal(message.type, 'CN_ACTIVATE_UPDATE'); p.serviceWorker.change(worker); } };
        registration.waiting = worker;
        await p.context.cnActivateUpdate();
        check('refresh activates the waiting worker and removes its listener', () => {
            assert.equal(p.serviceWorker.controller, worker); assert.equal(p.listeners.size, 0);
        });
    }
    {
        const registration = { scope: origin, installing: null, waiting: null, active: {}, addEventListener() {}, async update() { throw new Error('offline'); } };
        const p = makePage(registration);
        await assert.rejects(p.context.cnActivateUpdate(), /offline/);
        checks++;
    }
    {
        const registration = { scope: origin, installing: null, waiting: { postMessage() {} }, active: {}, addEventListener() {}, async update() {} };
        const p = makePage(registration, { fastTimeout: true });
        await assert.rejects(p.context.cnActivateUpdate(), /activation timed out/);
        check('activation timeout removes its controller listener', () => assert.equal(p.listeners.size, 0));
    }
    {
        let finishUpdate;
        let messages = 0;
        const registration = {
            scope: origin, installing: null, waiting: { postMessage() { messages++; } }, active: {}, addEventListener() {},
            update() { return new Promise(resolve => { finishUpdate = resolve; }); }
        };
        const p = makePage(registration, { fastTimeout: true });
        await assert.rejects(p.context.cnActivateUpdate(), /update timed out/);
        finishUpdate();
        await new Promise(resolve => setImmediate(resolve));
        check('timed-out update does not activate later', () => assert.equal(messages, 0));
    }
    {
        const match = layoutSource.match(/    var _refreshInProgress = false;[\s\S]*?    window\.refreshForUpdate = refreshForUpdate;/);
        assert.ok(match, 'refresh function exists');
        const elements = Object.fromEntries(['.update-toast-prompt', '.update-toast-progress', '.update-toast-failure'].map(k => [k, { style: {} }]));
        let reloads = 0;
        const context = {
            console: { warn() {} },
            document: { querySelector: k => elements[k], getElementById: () => null },
            location: { reload() { reloads++; } },
            cnActivateUpdate: async () => { throw new Error('offline'); }
        };
        context.window = context;
        vm.createContext(context);
        vm.runInContext(match[0], context);
        await context.refreshForUpdate();
        check('failed refresh remains on the page and offers retry', () => {
            assert.equal(reloads, 0); assert.equal(context._isRefreshing, false);
            assert.equal(elements['.update-toast-failure'].style.display, '');
            assert.equal(elements['.update-toast-prompt'].style.display, '');
        });
        context.cnActivateUpdate = async () => {};
        await context.refreshForUpdate();
        check('successful retry reloads', () => assert.equal(reloads, 1));
    }
    console.log(`PWA update flow: ${checks} checks passed`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
