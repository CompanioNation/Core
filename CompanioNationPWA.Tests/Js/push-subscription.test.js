// Regression harness for the Web Push helpers in CompanioNationPWA/wwwroot/pwa-install.js.
//
// pwa-install.js is plain browser JS with no build step and no module system, so it cannot be
// exercised from the C# test project. This harness loads the real file into a stubbed browser
// sandbox (node:vm) and asserts the behaviour C# depends on: which subscription is reused, which
// failures are retried, and — critically — that every failure path records a reason readable by
// CompanioNationSignalRClient.GetPushSubscriptionFailureAsync(). Without that reason, a
// "[Push] Push registration FAILED despite permission being GRANTED" alert carries no
// diagnosable information at all.
//
// Run:  node CompanioNationPWA.Tests/Js/push-subscription.test.js
// CI:   the "Run PWA JS tests" step in Services/.github/workflows/build-and-test.yml
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const SOURCE = path.join(__dirname, '..', '..', 'CompanioNationPWA', 'wwwroot', 'pwa-install.js');
const src = fs.readFileSync(SOURCE, 'utf8');

// A syntactically valid 87-char base64url VAPID public key (length is what matters here).
const VAPID = 'B' + 'A'.repeat(86);

let failures = 0;
function check(name, condition, detail) {
    if (condition) {
        console.log('PASS  ' + name);
    } else {
        failures++;
        console.log('FAIL  ' + name + (detail ? '  -> ' + detail : ''));
    }
}

function makeRegistration(behavior) {
    const reg = {
        scope: 'https://companionation.com/',
        unsubscribed: false,
        subscribeCalls: 0,
        getSubscriptionCalls: 0,
        addEventListener() { },
        pushManager: null
    };
    reg.pushManager = {
        async getSubscription() {
            reg.getSubscriptionCalls++;
            return behavior.getSubscription ? behavior.getSubscription(reg.getSubscriptionCalls) : null;
        },
        async subscribe() {
            reg.subscribeCalls++;
            const next = behavior.subscribeErrors && behavior.subscribeErrors.shift();
            if (next) { throw next; }
            return { endpoint: 'https://fcm.googleapis.com/fcm/send/abc123' };
        }
    };
    return reg;
}

function abortError() {
    const e = new Error('Registration failed - push service error');
    e.name = 'AbortError';
    return e;
}

function invalidStateError() {
    const e = new Error('The subscription is not compatible');
    e.name = 'InvalidStateError';
    return e;
}

function build(opts) {
    const sandbox = {};
    sandbox.window = sandbox;
    sandbox.console = { log() { }, info() { }, warn() { }, error() { } };
    sandbox.setTimeout = setTimeout;
    sandbox.clearTimeout = clearTimeout;
    // pwa-install.js polls for Blazor readiness with setInterval; the pollers are irrelevant
    // here, so they are stubbed out rather than left to hold the process open.
    sandbox.setInterval = () => 0;
    sandbox.clearInterval = () => { };
    sandbox.atob = atob;
    sandbox.document = {
        referrer: '',
        createElement: () => ({ setAttribute() { }, style: {}, appendChild() { } }),
        head: { appendChild() { } },
        body: { appendChild() { } },
        querySelectorAll: () => [],
        addEventListener() { }
    };
    sandbox.localStorage = {
        _data: {},
        getItem(k) { return Object.prototype.hasOwnProperty.call(this._data, k) ? this._data[k] : null; },
        setItem(k, v) { this._data[k] = String(v); },
        removeItem(k) { delete this._data[k]; }
    };
    sandbox.Notification = { permission: opts.permission || 'granted' };
    sandbox.matchMedia = () => ({ matches: false, addEventListener() { } });
    sandbox.addEventListener = () => { };
    sandbox.removeEventListener = () => { };
    sandbox.navigator = {
        userAgent: 'Mozilla/5.0 (Linux; Android 10; K) Chrome/154.0.0.0 Mobile Safari/537.36',
        platform: 'Linux armv8l',
        maxTouchPoints: 5,
        serviceWorker: {
            ready: opts.readyNever ? new Promise(() => { }) : Promise.resolve(opts.registration),
            register: () => Promise.resolve(opts.registration)
        }
    };
    sandbox.PushManager = function () { };
    vm.createContext(sandbox);
    vm.runInContext(src, sandbox, { filename: 'pwa-install.js' });
    if (opts.optedOut) { sandbox.window.setPushOptOut(true); }
    return sandbox;
}

async function main() {
    // An existing, still-valid subscription must be reused without touching subscribe().
    {
        const reg = makeRegistration({ getSubscription: () => ({ endpoint: 'https://fcm.googleapis.com/fcm/send/existing' }) });
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('existing subscription reused', typeof token === 'string' && token.includes('existing'), token);
        check('no failure recorded on success', w.getPushSubscriptionFailure() === null, w.getPushSubscriptionFailure());
        check('no subscribe call when reusing', reg.subscribeCalls === 0);
    }

    // A missing subscription is created.
    {
        const reg = makeRegistration({});
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('new subscription created', typeof token === 'string' && token.includes('fcm/send/abc123'), token);
        check('failure cleared after success', w.getPushSubscriptionFailure() === null);
    }

    // A transient push-service error (the most likely cause of the 2026-10-05 production alert)
    // must self-heal rather than page the developer.
    {
        const reg = makeRegistration({ subscribeErrors: [abortError()] });
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('AbortError retried and succeeded', typeof token === 'string' && token.includes('fcm/send/abc123'), token);
        check('AbortError retry made 2 subscribe calls', reg.subscribeCalls === 2, String(reg.subscribeCalls));
        check('AbortError retry leaves no failure', w.getPushSubscriptionFailure() === null);
    }

    // A persistent error must be reported with the browser's own name and message.
    {
        const reg = makeRegistration({ subscribeErrors: [abortError(), abortError()] });
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        const failure = w.getPushSubscriptionFailure();
        check('persistent AbortError returns null', token === null, String(token));
        check('persistent AbortError records reason',
            failure === 'reason=subscribe-failed; name=AbortError; message=Registration failed - push service error',
            failure);
    }

    // A subscription created with a different applicationServerKey (VAPID rotation) must be
    // dropped and recreated — Chrome refuses to replace it, which would otherwise be permanent.
    {
        let unsubscribed = false;
        const stale = {
            endpoint: 'https://fcm.googleapis.com/fcm/send/stale',
            async unsubscribe() { unsubscribed = true; return true; }
        };
        const reg = makeRegistration({
            getSubscription: (n) => (n === 1 ? null : stale),
            subscribeErrors: [invalidStateError()]
        });
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('InvalidStateError recovered', typeof token === 'string' && token.includes('fcm/send/abc123'), token);
        check('InvalidStateError dropped stale subscription', unsubscribed === true);
        check('InvalidStateError leaves no failure', w.getPushSubscriptionFailure() === null);
    }

    // Expected, non-actionable states are classified rather than reported as breakage.
    {
        const reg = makeRegistration({});
        const w = build({ registration: reg, permission: 'default' });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('permission default returns null', token === null);
        check('permission default recorded', w.getPushSubscriptionFailure() === 'reason=permission-not-granted', w.getPushSubscriptionFailure());
        check('permission default does not subscribe', reg.subscribeCalls === 0);
    }

    {
        const reg = makeRegistration({});
        const w = build({ registration: reg, optedOut: true });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('opt-out returns null', token === null);
        check('opt-out recorded', w.getPushSubscriptionFailure() === 'reason=opted-out', w.getPushSubscriptionFailure());
        check('opt-out does not subscribe', reg.subscribeCalls === 0);
    }

    // Any other error is still recorded verbatim.
    {
        const reg = makeRegistration({ subscribeErrors: [new TypeError('applicationServerKey is not valid')] });
        const w = build({ registration: reg });
        const token = await w.getOrCreatePushSubscription(VAPID);
        check('unexpected error returns null', token === null);
        check('unexpected error recorded',
            w.getPushSubscriptionFailure() === 'reason=subscribe-failed; name=TypeError; message=applicationServerKey is not valid',
            w.getPushSubscriptionFailure());
    }

    // navigator.serviceWorker.ready used to be awaited unbounded, so a missing active registration
    // hung the whole registration forever with no evidence. It must time out and say so.
    {
        const reg = makeRegistration({});
        const w = build({ registration: reg, readyNever: true });
        const started = Date.now();
        const token = await w.getOrCreatePushSubscription(VAPID);
        const elapsed = Date.now() - started;
        check('dead service worker returns null', token === null);
        check('dead service worker reported',
            (w.getPushSubscriptionFailure() || '').includes('serviceWorker.ready did not resolve'),
            w.getPushSubscriptionFailure());
        check('dead service worker bounded near 10s', elapsed >= 9500 && elapsed < 15000, elapsed + 'ms');
    }

    console.log('');
    if (failures === 0) {
        console.log('ALL CHECKS PASSED');
    } else {
        console.log(failures + ' CHECK(S) FAILED');
    }
    process.exit(failures === 0 ? 0 : 1);
}

main().catch(e => { console.error('HARNESS ERROR', e); process.exit(2); });
