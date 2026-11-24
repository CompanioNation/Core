// Regression test for the cold-boot cache progress counter in pwa-install.js.
//
// While the app boots, the "Loading..." button shows how many assets the service
// worker has cached ("Loading... 145/307"). That block was dropped once by an
// unrelated refactor and nobody noticed for weeks, so this pins the behaviour:
//   - the numerator counts ONLY precached manifest assets, never runtime-cached
//     extras (which pushed it past the total: "183/165")
//   - the denominator is the precache set from service-worker-assets.js
//   - the counter is torn down once the app is ready, so a later busy state can
//     never read a stale "159/159"
//   - it reads the cache of THIS build, not an older tab's
//   - progress is byte-proportional when service-worker-asset-sizes.js is present, and
//     falls back to the file count when it is missing or incomplete
const fs = require('fs');
const path = require('path');
const vm = require('vm');
const assert = require('assert');

const source = fs.readFileSync(
    path.join(__dirname, '..', '..', 'CompanioNationPWA', 'wwwroot', 'pwa-install.js'), 'utf8');

function counterBlock() {
    const match = source.match(/\/\/ ---- Cold-boot cache progress[\s\S]*?\r?\n\}\)\(\);\r?\n/);
    assert.ok(match, 'cold-boot cache progress block is missing from pwa-install.js');
    return match[0];
}

const BASE = 'https://companionation.com/';

function makeEnv({ caches: stores, manifestVersion, assets, manifestFails, assetSizes, labels: labelCount = 2 }) {
    const timers = new Map();
    const labels = [];
    const spans = [];

    function makeLabel() {
        const children = [];
        const label = {
            className: 'cn-busy-content',
            children,
            querySelector(selector) {
                const cls = selector.replace(/^\./, '');
                return children.filter(c => c.className === cls)[0] || null;
            },
            appendChild(el) {
                children.push(el);
                el.parentNode = label;
                return el;
            },
            removeChild(el) {
                const at = children.indexOf(el);
                if (at !== -1) children.splice(at, 1);
                el.parentNode = null;
                return el;
            }
        };
        labels.push(label);
        return label;
    }

    function makeElement(tagName) {
        const el = {
            tagName: tagName.toUpperCase(),
            className: '',
            attributes: {},
            textContent: '',
            parentNode: null,
            setAttribute(name, value) { el.attributes[name] = value; }
        };
        if (tagName === 'span') spans.push(el);
        return el;
    }

    // Busy labels, like a page with a sign-in CTA and a second action button.
    for (let i = 0; i < labelCount; i++) makeLabel();

    const document = {
        baseURI: BASE,
        head: {
            // A blocked/missing service-worker-assets.js never fires onload.
            appendChild(script) {
                if (!manifestFails && script.onload) script.onload();
                return script;
            }
        },
        createElement: makeElement,
        querySelectorAll(selector) {
            if (selector === '.cn-busy-content') return labels.slice();
            if (selector === '.cn-busy-count') {
                return spans.filter(s => s.parentNode && s.parentNode.children.indexOf(s) !== -1);
            }
            return [];
        }
    };

    const cacheApi = {
        async keys() { return Object.keys(stores); },
        async open(name) {
            const urls = stores[name] || [];
            return { async keys() { return urls.map(url => ({ url })); } };
        }
    };

    const context = {
        document,
        caches: cacheApi,
        URL,
        Set,
        Promise,
        console,
        location: { href: BASE },
        setInterval(fn, ms) { const id = timers.size + 1; timers.set(ms, fn); return id; },
        clearInterval(id) { /* ids are delay-keyed below; clearing is asserted via the DOM */ },
        setTimeout() { return 0; },
        clearTimeout() { }
    };
    context.window = context;
    context.window.assetsManifest = { version: manifestVersion, assets };
    if (assetSizes) context.window.cnAssetSizes = assetSizes;
    vm.createContext(context);
    return { context, timers, labels, spans, addLabel: makeLabel };
}

async function flush() {
    for (let i = 0; i < 4; i++) await new Promise(resolve => setImmediate(resolve));
}

async function run(env) {
    vm.runInContext(counterBlock(), env.context);
    await flush();
}

const ASSETS = [
    { url: 'css/app.css' },
    { url: 'appsettings.json' },
    { url: 'index.html' },
    { url: 'service-worker.js' }, // excluded by the worker's precache rules
    { url: 'images/logo.psd' }    // not an offline asset type
];

async function main() {
    let checks = 0;
    function check(name, fn) {
        fn();
        checks++;
        console.log('  ok - ' + name);
    }

    {
        const env = makeEnv({
            manifestVersion: 'v2',
            assets: ASSETS,
            caches: {
                // An older tab's build keeps its own cache alive; it must be ignored.
                'offline-cache-v1': new Array(99).fill(BASE + 'stale.js'),
                'offline-cache-v2': [
                    BASE + 'css/app.css',
                    BASE + 'appsettings.json',
                    BASE + 'api/session' // runtime-cached extra, NOT a precache asset
                ]
            }
        });
        await run(env);

        check('renders "cached/total" for the precache set only', () => {
            const text = env.spans.map(s => s.textContent);
            assert.deepEqual(text, [' 2/3', ' 2/3']);
        });

        check('counts this build\'s cache, not an older build\'s', () => {
            assert.ok(!env.spans[0].textContent.includes('99'));
        });

        check('counter is decorative for screen readers', () => {
            assert.equal(env.spans[0].attributes['aria-hidden'], 'true');
        });

        check('counter is removed once the app is ready', () => {
            env.context.cnBlazorReady = true;
            const stopGuard = env.timers.get(500);
            assert.equal(typeof stopGuard, 'function');
            stopGuard();
            assert.deepEqual(env.context.document.querySelectorAll('.cn-busy-count'), []);
        });
    }

    {
        // Every manifest asset cached (plus extras): the numerator must be clamped
        // to the denominator rather than reading "4/3".
        const env = makeEnv({
            manifestVersion: 'v9',
            assets: ASSETS,
            caches: {
                'offline-cache-v9': [
                    BASE + 'css/app.css',
                    BASE + 'appsettings.json',
                    BASE + 'index.html',
                    BASE + 'api/one',
                    BASE + 'api/two'
                ]
            }
        });
        await run(env);

        check('numerator never exceeds the denominator', () => {
            assert.equal(env.spans[0].textContent, ' 3/3');
        });
    }

    {
        // Manifest blocked or missing: show a bare count, never a wrong total.
        const env = makeEnv({
            manifestFails: true,
            manifestVersion: 'v1',
            assets: ASSETS,
            caches: { 'offline-cache-v1': [BASE + 'a.css'] }
        });
        await run(env);

        check('degrades to a bare count without a manifest', () => {
            assert.equal(env.spans[0].textContent, ' 1');
        });
    }

    {
        // The busy label can appear (or be re-rendered by Blazor) after caching has already
        // settled, so the counter must still be drawn when nothing new is cached.
        const env = makeEnv({
            labels: 0,
            manifestVersion: 'v3',
            assets: ASSETS,
            caches: { 'offline-cache-v3': [BASE + 'css/app.css', BASE + 'appsettings.json', BASE + 'index.html'] }
        });
        await run(env);

        check('nothing is rendered while no busy label exists', () => {
            assert.deepEqual(env.spans, []);
        });

        env.addLabel();
        env.timers.get(250)();
        await flush();

        check('a label appearing later still gets the counter', () => {
            assert.equal(env.spans[0].textContent, ' 3/3');
        });
    }

    {
        // Byte-proportional mode: the build emits service-worker-asset-sizes.js so the
        // counter can weigh a 10 MB wasm file far more than a 2 KB icon.
        const store = { 'offline-cache-v4': [BASE + 'css/app.css', BASE + 'api/session'] };
        const env = makeEnv({
            manifestVersion: 'v4',
            assets: ASSETS,
            assetSizes: {
                'css/app.css': 1048576,
                'appsettings.json': 524288,
                'index.html': 1572864
            },
            caches: store
        });
        await run(env);

        check('renders byte progress when sizes are known', () => {
            // The runtime-cached api/session extra must not inflate the numerator.
            assert.equal(env.spans[0].textContent, ' 1.0/3.0 MB · 33%');
        });

        store['offline-cache-v4'].push(BASE + 'appsettings.json');
        env.timers.get(250)();
        await flush();

        check('byte progress grows as more of the precache lands', () => {
            assert.equal(env.spans[0].textContent, ' 1.5/3.0 MB · 50%');
        });
    }

    {
        // A partial size list would hit 100% while assets were still downloading, so the
        // counter must stay on the file count instead.
        const env = makeEnv({
            manifestVersion: 'v5',
            assets: ASSETS,
            assetSizes: { 'css/app.css': 1048576, 'appsettings.json': 524288 },
            caches: { 'offline-cache-v5': [BASE + 'css/app.css'] }
        });
        await run(env);

        check('falls back to the file count when sizes are incomplete', () => {
            assert.equal(env.spans[0].textContent, ' 1/3');
        });
    }

    {
        // Small downloads stay readable: KB rather than "0.0 MB".
        const env = makeEnv({
            manifestVersion: 'v6',
            assets: [{ url: 'css/app.css' }, { url: 'appsettings.json' }],
            assetSizes: { 'css/app.css': 1024, 'appsettings.json': 1024 },
            caches: { 'offline-cache-v6': [BASE + 'css/app.css'] }
        });
        await run(env);

        check('formats small byte totals in KB', () => {
            assert.equal(env.spans[0].textContent, ' 1/2 KB · 50%');
        });
    }

    console.log(`PWA cache progress: ${checks} checks passed`);
}

main().catch(error => { console.error(error); process.exitCode = 1; });
