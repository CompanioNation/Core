// Both workers must display the server's latest push body without accumulating old bodies.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..', '..', 'CompanioNationPWA', 'wwwroot');
let checks = 0;
function check(fn) { fn(); checks++; }

async function runWorker(name) {
    const handlers = {};
    const shown = [];
    const forwarded = [];
    const context = {
        origin: 'https://example.test', URL,
        assetsManifest: { version: 'test', assets: [] },
        importScripts() {},
        addEventListener(type, handler) { handlers[type] = handler; },
        registration: {
            async showNotification(title, options) { shown.push({ title, options }); },
            async getNotifications() { throw new Error('Worker must not read old notifications'); }
        },
        clients: {
            matchAll: async () => [{ url: 'https://example.test/', postMessage(message) { forwarded.push(message); } }]
        },
        console: { log() {}, info() {}, warn() {}, error() {} }
    };
    context.self = context;
    vm.createContext(context);
    vm.runInContext(fs.readFileSync(path.join(root, name), 'utf8'), context, { filename: name });

    async function push(title, body, tag = 'new_message') {
        let pending;
        const options = { body, tag, data: { userId: 7, url: '/Messages/7' } };
        handlers.push({ data: { json: () => ({ title, options }) }, waitUntil(promise) { pending = promise; } });
        await pending;
        return options;
    }

    const first = await push('Alice', 'old\nnew');
    check(() => assert.equal(shown[0].title, 'Alice'));
    check(() => assert.equal(shown[0].options.body, 'old\nnew'));
    check(() => assert.deepEqual(shown[0].options, first));
    const second = await push('Alice', 'new\nlatest');
    check(() => assert.equal(shown[1].options.body, 'new\nlatest'));
    check(() => assert.deepEqual(shown[1].options, second));
    check(() => assert.equal(shown[1].options.data.url, '/Messages/7'));
    check(() => assert.equal(forwarded.length, 2));
    check(() => assert.equal(forwarded[1].action, 'message_received'));
    check(() => assert.equal(forwarded[1].userId, 7));
    const promo = await push('CompanioNation', 'promotion', 'promotional');
    check(() => assert.deepEqual(shown[2].options, promo));
    check(() => assert.equal(shown[2].options.body, 'promotion'));
}

(async () => {
    for (const worker of ['service-worker.js', 'service-worker.published.js']) await runWorker(worker);
    console.log(`Push notification body: ${checks} checks passed`);
})().catch(error => { console.error(error); process.exitCode = 1; });
