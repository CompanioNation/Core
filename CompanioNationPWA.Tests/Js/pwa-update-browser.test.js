// Optional real-browser lifecycle regression test. Run with Node 22+ on Windows with Edge installed.
// This uses a temporary browser profile and a local HTTP server; it does not contact the app.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { spawn } = require('node:child_process');

const root = path.join(__dirname, '..', '..', 'CompanioNationPWA', 'wwwroot');
const browserPath = process.env.PWA_TEST_BROWSER || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe';
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const sri = bytes => 'sha256-' + crypto.createHash('sha256').update(bytes).digest('base64');
let version = 'v1';
let badHash = false;
let browser;
let socket;
let server;
let profile;

async function waitFor(fn, description, timeout = 15000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) {
        const value = await fn();
        if (value) return value;
        await sleep(100);
    }
    throw new Error('Timed out waiting for ' + description);
}

async function main() {
    assert.ok(fs.existsSync(browserPath), 'Set PWA_TEST_BROWSER to a Chromium browser executable');
    const worker = fs.readFileSync(path.join(root, 'service-worker.published.js'));
    const install = fs.readFileSync(path.join(root, 'pwa-install.js'));
    server = http.createServer((req, res) => {
        res.setHeader('Cache-Control', 'no-store');
        if (req.url === '/service-worker.js') {
            res.setHeader('Content-Type', 'text/javascript'); res.end(worker);
        } else if (req.url === '/service-worker-assets.js') {
            // Real hashes so the worker's SRI check actually runs; a deliberately wrong hash
            // proves a tampered response is rejected instead of cached.
            const assets = [
                { url: 'pwa-install.js', hash: sri(install) },
                { url: `_framework/app.${version}.wasm`, hash: sri(badHash ? 'tampered' : version) }
            ];
            res.setHeader('Content-Type', 'text/javascript');
            res.end('self.assetsManifest = ' + JSON.stringify({ version, assets }) + ';');
        } else if (req.url === '/pwa-install.js') {
            res.setHeader('Content-Type', 'text/javascript'); res.end(install);
        } else if (req.url === `/_framework/app.${version}.wasm`) {
            res.end(version);
        } else if (req.url === '/') {
            res.setHeader('Content-Type', 'text/html');
            res.end('<!doctype html><script src="/pwa-install.js"></script><script>window.cnBlazorReady = true;</script>');
        } else {
            res.statusCode = 404; res.end('not found');
        }
    });
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const base = `http://127.0.0.1:${server.address().port}/`;
    profile = fs.mkdtempSync(path.join(__dirname, '.browser-test-'));
    browser = spawn(browserPath, ['--headless=new', '--disable-gpu', '--no-first-run', '--disable-extensions', '--disable-popup-blocking', '--remote-debugging-port=0', `--user-data-dir=${profile}`, base], { stdio: 'ignore' });
    const portFile = path.join(profile, 'DevToolsActivePort');
    const port = await waitFor(() => fs.existsSync(portFile) && fs.readFileSync(portFile, 'utf8').split(/\r?\n/)[0], 'browser debugging port');
    const targets = await waitFor(async () => {
        const response = await fetch(`http://127.0.0.1:${port}/json/list`);
        const items = await response.json();
        return items.find(item => item.type === 'page' && item.url.startsWith(base));
    }, 'test page');
    socket = new WebSocket(targets.webSocketDebuggerUrl);
    await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
    let nextId = 0;
    const pending = new Map();
    socket.onmessage = event => {
        const message = JSON.parse(event.data);
        if (!message.id || !pending.has(message.id)) return;
        const { resolve, reject } = pending.get(message.id); pending.delete(message.id);
        if (message.error) reject(new Error(message.error.message)); else resolve(message.result);
    };
    function command(method, params = {}) {
        const id = ++nextId;
        socket.send(JSON.stringify({ id, method, params }));
        return new Promise((resolve, reject) => pending.set(id, { resolve, reject }));
    }
    async function evaluate(expression) {
        const result = await command('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
        if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
        return result.result.value;
    }
    // Precache through a MessageChannel so the test waits for the worker's own completion
    // signal instead of polling cache contents (which races on the failure path).
    async function precache() {
        return evaluate(`new Promise(resolve => {
            const channel = new MessageChannel();
            const timeout = setTimeout(() => resolve('timeout'), 30000);
            channel.port1.onmessage = event => {
                clearTimeout(timeout);
                resolve(event.data && event.data.ok ? 'ok' : 'failed');
            };
            navigator.serviceWorker.controller.postMessage({ type: 'CN_PRECACHE' }, [channel.port2]);
        })`);
    }
    await waitFor(() => evaluate('typeof window.cnActivateUpdate === "function"'), 'page script');
    assert.equal(await evaluate('Promise.race([navigator.serviceWorker.ready.then(() => true), new Promise(resolve => setTimeout(() => resolve(false), 5000))])'), true, 'first worker must activate promptly for push');
    await waitFor(() => evaluate('caches.keys().then(keys => keys.includes("offline-cache-v1"))'), 'initial cache');
    await waitFor(() => evaluate('caches.open("offline-cache-v1").then(c => c.match("/_framework/app.v1.wasm")).then(Boolean)'), 'first cache warm-up');
    await evaluate('!!(window.otherTab = window.open("/"))');
    await waitFor(() => evaluate('window.otherTab && window.otherTab.cnBlazorReady === true'), 'second tab');

    version = 'v2';
    await evaluate('navigator.serviceWorker.getRegistration().then(r => r.update())');
    await waitFor(() => evaluate('navigator.serviceWorker.getRegistration().then(r => !!r.waiting)'), 'waiting worker');
    assert.equal(await evaluate('caches.keys().then(keys => keys.includes("offline-cache-v2"))'), false, 'waiting update must not precache');
    assert.equal(await evaluate('navigator.serviceWorker.getRegistration().then(r => r.active !== r.waiting)'), true);

    await evaluate('window.cnActivateUpdate()');
    assert.equal(await evaluate('navigator.serviceWorker.getRegistration().then(r => navigator.serviceWorker.controller === r.active)'), true);
    assert.equal(await evaluate('caches.keys().then(keys => keys.includes("offline-cache-v1"))'), true, 'activation must retain the old cache');
    assert.equal(await evaluate('fetch("/_framework/app.v1.wasm").then(r => r.text())'), 'v1', 'old tabs can still load old framework assets');
    assert.equal(await evaluate('caches.keys().then(keys => keys.includes("offline-cache-v2"))'), true, 'active worker opens its cache for fetches');
    assert.equal(await evaluate('caches.open("offline-cache-v2").then(c => c.match("/_framework/app.v2.wasm")).then(Boolean)'), false, 'activation alone does not download the update');
    await evaluate('window.otherTab.close()');
    await evaluate('location.reload()');
    await waitFor(() => evaluate('caches.open("offline-cache-v2").then(c => c.match("/_framework/app.v2.wasm")).then(Boolean)'), 'updated cache warm-up', 20000);
    await waitFor(() => evaluate('caches.keys().then(keys => !keys.includes("offline-cache-v1"))'), 'old cache cleanup');
    assert.equal(await evaluate('caches.open("offline-cache-v2").then(c => c.match("/__cn_precache_verified_v2")).then(Boolean)'), true, 'a complete precache is marked verified');

    // A tampered asset must be rejected by the manifest integrity hash: it never reaches the
    // cache, and the previous build's cache survives so already-open tabs keep working.
    version = 'v3';
    badHash = true;
    await evaluate('navigator.serviceWorker.getRegistration().then(r => r.update())');
    await waitFor(() => evaluate('navigator.serviceWorker.getRegistration().then(r => !!r.waiting)'), 'tampered update waiting');
    await evaluate('window.cnActivateUpdate()');
    assert.equal(await precache(), 'failed', 'a tampered asset must make the precache report failure');
    assert.equal(await evaluate('caches.open("offline-cache-v3").then(c => c.match("/pwa-install.js")).then(Boolean)'), true, 'the untampered asset is cached');
    assert.equal(await evaluate('caches.open("offline-cache-v3").then(c => c.match("/_framework/app.v3.wasm")).then(Boolean)'), false, 'a hash mismatch must not be cached');
    assert.equal(await evaluate('caches.open("offline-cache-v3").then(c => c.match("/__cn_precache_verified_v2")).then(Boolean)'), false, 'a failed precache must not be marked verified');
    assert.equal(await evaluate('caches.keys().then(keys => keys.includes("offline-cache-v2"))'), true, 'a failed precache must retain the previous cache');
    console.log('PWA browser lifecycle: first install, waiting update, two tabs, activation, cache warm-up and SRI rejection passed');
}

main().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
    if (socket && socket.readyState === WebSocket.OPEN) {
        socket.send(JSON.stringify({ id: -1, method: 'Browser.close' }));
    }
    if (browser && browser.exitCode === null) {
        await Promise.race([new Promise(resolve => browser.once('exit', resolve)), sleep(5000)]);
        if (browser.exitCode === null) browser.kill();
    }
    if (socket) socket.close();
    if (server) await new Promise(resolve => server.close(resolve));
    if (profile) {
        for (let attempt = 0; attempt < 10; attempt++) {
            try { fs.rmSync(profile, { recursive: true, force: true }); break; }
            catch (error) {
                if (attempt === 9) { console.error('Failed to remove browser test profile:', profile, error.message); process.exitCode = 1; }
                else await sleep(1000);
            }
        }
    }
});
