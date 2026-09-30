// pwa-install.js
let deferredPrompt = null;
let shouldShowInstallButton = false;

window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault();
    deferredPrompt = e;
    shouldShowInstallButton = true;
});

window.promptPWAInstall = function () {
    showPWAInstallPrompt();
};


function showPWAInstallPrompt() {
    if (deferredPrompt) {
        deferredPrompt.prompt();
        deferredPrompt.userChoice.then((choiceResult) => {
            if (choiceResult.outcome === 'accepted') {
                console.log('User accepted the install prompt');
            } else {
                console.log('User dismissed the install prompt');
            }
            deferredPrompt = null;
            shouldShowInstallButton = false;
        }).catch((error) => {
            console.error("Error during install prompt:", error);
        });
    } else {
        console.log('No deferred prompt available.');
    }
}

window.shouldShowInstallButton = function () {
    return shouldShowInstallButton;
};

// This is called when the user logs out (or deletes their account - todo)
window.unregisterPush = async function () {
    if ('serviceWorker' in navigator) {
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();

            if (subscription) {
                await subscription.unsubscribe();
                console.info('Push subscription successfully unregistered.');
            } else {
                console.info('No push subscription found to unregister.');
            }

            // Optional: Unregister the service worker as well
            // await registration.unregister();
            // console.info('Service worker unregistered.');
        } catch (error) {
            console.error('Failed to unregister push subscription:', error);
        }
    }
}


// Requests notification permission and subscribes in a single call.
// MUST be called from a user gesture (button click). Keeping the entire flow
// in JS guarantees the browser's transient user activation is preserved.
// Returns { permission, pushToken } where pushToken is the subscription JSON or null.
window.requestNotificationPermission = async function (vapidPublicKey) {
    console.info('[Push] requestNotificationPermission called.');

    if (!("Notification" in window)) {
        console.warn('[Push] Notification API not supported in this browser.');
        return { permission: 'unsupported', pushToken: null };
    }

    console.info('[Push] Current permission state:', Notification.permission);
    const permission = await Notification.requestPermission();
    console.info('[Push] Permission prompt result:', permission);

	if (permission !== 'granted') {
		console.warn('[Push] Permission not granted - returning without subscribing.');
		return { permission, pushToken: null };
	}

	// Permission granted - subscribe (or reuse the existing subscription)
	const pushToken = await window.getOrCreatePushSubscription(vapidPublicKey);
	console.info('[Push] requestNotificationPermission complete. pushToken:', pushToken ? 'obtained' : 'null');
	return { permission, pushToken };
};

// Called DIRECTLY from the "Enable Notifications" button's synchronous onclick.
// This exists because Chrome on Android drops Notification.requestPermission()
// when the "user gesture" has been consumed by any prior async hop - and going
// button-click -> Blazor invokeMethodAsync -> C# await -> JSRuntime.InvokeAsync
// crosses two async boundaries before the prompt is even attempted, so the
// prompt silently no-ops (the console shows "Current permission state: default"
// but no "Permission prompt result:" line ever follows). Calling
// Notification.requestPermission() FIRST, synchronously inside onclick, keeps
// the user activation alive for the browser prompt. Only AFTER the prompt has
// resolved do we hand the result off to Blazor for subscribing + server upload.
window.enableNotificationsFromClick = function () {
	console.info('[Push] enableNotificationsFromClick invoked from user gesture.');

	if (!("Notification" in window)) {
		console.warn('[Push] Notification API not supported in this browser.');
		return;
	}

	// Synchronous kickoff of the prompt - DO NOT await anything above this line.
	// Assigning the promise so the browser sees the requestPermission() call
	// originating from the current click's synchronous stack frame.
	var promptPromise;
	try {
		promptPromise = Notification.requestPermission();
	} catch (e) {
		console.error('[Push] Notification.requestPermission threw synchronously:', e);
		return;
	}

	// requestPermission's legacy callback-only signature (older browsers) returns
	// undefined; guard against that.
	if (!promptPromise || typeof promptPromise.then !== 'function') {
		console.warn('[Push] Notification.requestPermission did not return a Promise; cannot proceed.');
		return;
	}

	promptPromise.then(function (permission) {
		console.info('[Push] Permission prompt result:', permission);
		if (permission !== 'granted') {
			console.warn('[Push] Permission not granted - not subscribing.');
			if (window.dotNetObject) {
				window.dotNetObject.invokeMethodAsync('SetInstallBannerState');
			}
			return;
		}
		return window.getOrCreatePushSubscription(window._cnVapidPublicKey).then(function (pushToken) {
			console.info('[Push] Post-permission subscription:', pushToken ? (pushToken.length + ' chars') : 'null');
			// Explicit opt-in: clear any stored opt-out so the connect-time validation
			// keeps this subscription alive instead of tearing it down again.
			if (pushToken) {
				window.setPushOptOut(false);
			}
			if (window.dotNetObject) {
				window.dotNetObject.invokeMethodAsync('SetInstallBannerState');
				if (pushToken) {
					window.dotNetObject.invokeMethodAsync('SendPushTokenToServer', pushToken);
				} else {
					// Classify WHY the subscription failed: an unfixable context (no Push API,
					// iOS Safari in a tab, or a Brave-style policy blocker) is recorded as info;
					// anything else is a genuine registration bug and alerts.
					window.cnPushCapability().then(function (cap) {
						var unfixable = cap === 'unsupported' || cap === 'requires-install' || cap === 'brave';
						window.dotNetObject.invokeMethodAsync('LogPushSubscriptionFailure', unfixable);
					});
				}
			}
		});
	}).catch(function (err) {
		console.error('[Push] enableNotificationsFromClick failed:', err);
	});
};


// Converts a base64url-encoded VAPID public key into the Uint8Array that
// PushManager.subscribe() requires for applicationServerKey. Passing the raw
// string causes subscribe() to throw, which is why push tokens stopped being created.
function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - (base64String.length % 4)) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const rawData = atob(base64);
    const outputArray = new Uint8Array(rawData.length);
    for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }
    return outputArray;
}

// ---- Push opt-out (Settings toggle) ----
// Persisted client-side so the choice survives reloads and the connect-time
// validation cannot silently re-enable push the user deliberately turned off.
// Opting back in (either from Settings or the Home banner) clears the flag.
const CN_PUSH_OPT_OUT_KEY = 'cn_push_opt_out';

window.isPushOptedOut = function () {
    try { return localStorage.getItem(CN_PUSH_OPT_OUT_KEY) === '1'; }
    catch (e) { return false; }
};

window.setPushOptOut = function (optedOut) {
    try {
        if (optedOut) {
            localStorage.setItem(CN_PUSH_OPT_OUT_KEY, '1');
        } else {
            localStorage.removeItem(CN_PUSH_OPT_OUT_KEY);
        }
    } catch (e) {
        console.warn('[Push] Could not persist push opt-out state:', e);
    }
};

// Returns the current device's Web Push subscription as a JSON string, REUSING an
// existing subscription when one is present and CREATING one when none exists.
// (Formerly named validatePushSubscription; that name hid the create side effect,
// which is exactly what made "turn off" bounce back on the next connect.)
// Returns null when Web Push is unavailable, permission is not granted, the user
// has opted out in Settings, or subscribing fails. Safe to call frequently.
window.getOrCreatePushSubscription = async function (vapidPublicKey) {
    console.info('[Push] getOrCreatePushSubscription called.');

    // Respect an explicit opt-out: never (re)create a subscription the user turned off.
    if (window.isPushOptedOut()) {
        console.info('[Push] getOrCreatePushSubscription: user opted out — not creating a subscription.');
        return null;
    }

    if (!("Notification" in window) || !('serviceWorker' in navigator)) {
        console.warn('[Push] getOrCreatePushSubscription: Notification API or Service Worker not available. Notification:', ("Notification" in window), 'SW:', ('serviceWorker' in navigator));
        return null;
    }

    if (Notification.permission !== "granted") {
        console.warn('[Push] getOrCreatePushSubscription: Permission is "' + Notification.permission + '", not "granted". Skipping.');
        return null;
    }

    try {
        const registration = await navigator.serviceWorker.ready;
        console.info('[Push] Service worker ready. Scope:', registration.scope);
        let subscription = await registration.pushManager.getSubscription();

        if (subscription) {
            // Verify the subscription endpoint is still reachable by checking
            // that the endpoint URL hasn't become empty/null (browser-side invalidation).
            if (!subscription.endpoint) {
                console.warn('[Push] Existing subscription has invalid/empty endpoint, re-subscribing...');
                await subscription.unsubscribe();
                subscription = null;
            } else {
                console.info('[Push] Existing push subscription is valid. Endpoint:', subscription.endpoint.substring(0, 60) + '...');
            }
        }

        if (!subscription) {
            console.info('[Push] No valid push subscription found, creating new subscription...');
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                // applicationServerKey must be a BufferSource (Uint8Array); passing the raw
                // base64url VAPID string throws and silently prevents subscription/token creation.
                applicationServerKey: urlBase64ToUint8Array(vapidPublicKey)
            });
            console.info('[Push] New push subscription created. Endpoint:', subscription.endpoint.substring(0, 60) + '...');
        }

        return JSON.stringify(subscription);
    } catch (error) {
        console.error('[Push] Failed to get or create push subscription:', error);
        return null;
    }
}

// Returns a snapshot of the current push-notification state for the Settings toggle:
//   capability  - 'available' | 'unsupported' | 'requires-install' | 'brave' | 'native-ios'
//   permission  - web: Notification.permission; native iOS: the UNUserNotificationCenter state
//   subscribed  - true when this device currently holds a live subscription/token
//   optedOut    - true when the user turned push off in Settings (persisted client-side)
window.getPushState = async function () {
    var capability = 'available';
    try { capability = await window.cnPushCapability(); } catch (e) { capability = 'available'; }

    var permission = 'default';
    try {
        if (capability === 'native-ios') {
            permission = await window.getNativeIosPushState();
        } else if (typeof Notification !== 'undefined') {
            permission = Notification.permission;
        }
    } catch (e) {
        permission = 'unknown';
    }

    var subscribed = false;
    try {
        if (capability === 'native-ios') {
            // The FCM token arrives asynchronously after boot, so getFcmToken() is null on a
            // fresh page load even for a user whose push is fully enabled. Treat permission
            // granted as subscribed so the toggle shows ON during that window rather than a
            // misleading OFF that would flip back once the token arrives.
            subscribed = !!window.getFcmToken()
                || permission === 'authorized'
                || permission === 'ephemeral'
                || permission === 'provisional';
        } else if ('serviceWorker' in navigator && 'PushManager' in window) {
            // Race the registration promise against a short timeout: navigator.serviceWorker.ready
            // never resolves when no worker is registered (e.g. insecure context), which would
            // otherwise hang the Settings page render.
            var registration = await Promise.race([
                navigator.serviceWorker.ready,
                new Promise(function (resolve) { setTimeout(function () { resolve(null); }, 3000); })
            ]);
            if (registration) {
                var subscription = await registration.pushManager.getSubscription();
                subscribed = !!(subscription && subscription.endpoint);
            }
        }
    } catch (e) {
        subscribed = false;
    }

    var optedOut = false;
    try { optedOut = window.isPushOptedOut(); } catch (e) { optedOut = false; }

    return { capability: capability, permission: permission, subscribed: subscribed, optedOut: optedOut };
};

// ---- Settings push-toggle bridge ----
// The ENABLE action must run from the toggle's synchronous onclick so the browser keeps
// the user activation for Notification.requestPermission() (Chrome on Android silently
// drops the prompt if any async hop precedes it). Results are posted back to the Settings
// component through window.cnSettingsDotNet.

window.cnSettingsSetDotNetRef = function (ref) {
    window.cnSettingsDotNet = ref;
};

// Opens the OS app-notification settings for the TWA (or falls back to the
// browser's site settings for that origin). Needed because once the web-layer
// Notification.permission is 'denied' the browser/TWA will never re-prompt, and
// on Android 13+ the OS app permission is what controls the TWA's notifications.
window.cnOpenNotificationSettings = function () {
    try {
        if (typeof window.isNativeIosApp === 'function' && window.isNativeIosApp()) {
            // iOS notification settings deep link (best-effort in the wrapper).
            if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers['open-notification-settings']) {
                window.webkit.messageHandlers['open-notification-settings'].postMessage(null);
                return;
            }
            return;
        }
    } catch (e) { /* fall through */ }

    try {
        if (document.referrer && document.referrer.indexOf('android-app://') === 0) {
            // TWA: open the Android app's notification settings directly.
            location.href = 'intent://com.companionation.app#Intent;action=android.settings.APP_NOTIFICATION_SETTINGS;package=com.companionation.app;end';
            return;
        }
    } catch (e) { /* fall through */ }

    try {
        // Plain browser: Chrome's per-origin notification settings are not
        // deep-linkable, so fall back to the site settings page.
        location.href = 'chrome://settings/content/notifications';
    } catch (e) { /* ignore */ }
};

window.cnSettingsClearDotNetRef = function () {
    window.cnSettingsDotNet = null;
};

// Invoked from the toggle's onclick. `el` is the checkbox; its new checked state is the
// user's desired state.
window.cnSettingsTogglePush = function (el) {
    if (!el) return;

    // Lock the checkbox immediately so a double-click cannot fire a second permission
    // prompt. The Blazor handler re-renders (and re-enables) it when the async
    // enable/disable completes — see OnPushEnabledFromToggle / OnPushDisabledFromToggle.
    el.disabled = true;

    var enable = !!el.checked;
    if (enable) {
        window.cnSettingsEnablePush();
    } else if (window.cnSettingsDotNet) {
        window.cnSettingsDotNet.invokeMethodAsync('OnPushDisabledFromToggle');
    }
};

function _cnSettingsReport(status, token) {
    if (window.cnSettingsDotNet) {
        window.cnSettingsDotNet.invokeMethodAsync('OnPushEnabledFromToggle', status, token || null);
    }
}

// Enables push from Settings. MUST be called synchronously from a click handler so
// Notification.requestPermission() still runs inside the click's user activation.
window.cnSettingsEnablePush = function () {
    console.info('[Push] cnSettingsEnablePush invoked from user gesture.');

    // Explicit gesture: the user wants push on, so clear any stored opt-out up front.
    window.setPushOptOut(false);

    if (window.isNativeIosApp && window.isNativeIosApp()) {
        _cnSettingsEnableNativeIos();
        return;
    }

    // Web / Android.
    if (!("Notification" in window)) {
        _cnSettingsReport('failed');
        return;
    }

    if (Notification.permission === 'granted') {
        // Already granted: reuse the existing subscription. No prompt and no re-subscribe
        // churn — this is what makes re-enabling after a toggle-off instant and reliable.
        window.getOrCreatePushSubscription(window._cnVapidPublicKey).then(function (token) {
            _cnSettingsReport(token ? 'ok' : 'failed', token);
        }).catch(function (err) {
            console.error('[Push] cnSettingsEnablePush (already granted) failed:', err);
            _cnSettingsReport('failed');
        });
        return;
    }

    if (Notification.permission === 'denied') {
        _cnSettingsReport('denied');
        return;
    }

    // 'default' — must prompt. requestPermission() is invoked synchronously here (before
    // the promise continuation runs) so the transient user activation is preserved.
    Notification.requestPermission().then(function (permission) {
        if (permission !== 'granted') {
            _cnSettingsReport('denied');
            return null;
        }
        return window.getOrCreatePushSubscription(window._cnVapidPublicKey);
    }).then(function (token) {
        if (token) {
            _cnSettingsReport('ok', token);
        } else if (Notification.permission === 'granted') {
            _cnSettingsReport('failed');
        } else {
            _cnSettingsReport('denied');
        }
    }).catch(function (err) {
        console.error('[Push] cnSettingsEnablePush failed:', err);
        _cnSettingsReport('failed');
    });
};

// Native iOS enable path. iOS has no transient user-activation constraint on its
// permission prompt (unlike Chrome on Android), so awaiting the state probe first is safe.
function _cnSettingsEnableNativeIos() {
    window.getNativeIosPushState().then(function (state) {
        if (state === 'denied' || state === 'unsupported' || state === 'unknown') {
            _cnSettingsReport('denied');
            return;
        }
        if (state === 'notDetermined') {
            return window.requestNativeIosPushPermission().then(function (permission) {
                if (permission !== 'granted') {
                    _cnSettingsReport('denied');
                    return null; // sentinel: already reported
                }
                return 'granted';
            });
        }
        return state; // authorized | ephemeral | provisional
    }).then(function (granted) {
        if (granted === null || granted === undefined) return; // already reported
        // Permission is granted. The FCM token may not have arrived yet; if so, ask the
        // native side to re-fetch it. The existing OnFcmTokenChanged callback forwards it to
        // the server the moment it lands, so we can report success now instead of failing.
        var token = window.getFcmToken();
        if (token) {
            _cnSettingsReport('ok', token);
        } else {
            try { window.companioNation_requestFcmToken(); } catch (e) { /* best-effort */ }
            _cnSettingsReport('pending');
        }
    }).catch(function (err) {
        console.error('[Push] cnSettingsEnablePush (native iOS) failed:', err);
        _cnSettingsReport('failed');
    });
}

// Single source of truth for whether/how Web Push (VAPID) can work in this browser/context.
// Returns one of:
//   'native-ios'       - inside the native iOS app (FCM bridge, not Web Push)
//   'unsupported'      - no PushManager/serviceWorker: Web Push is impossible here
//   'requires-install' - iOS/iPadOS Safari in a tab: must be added to Home Screen first
//   'brave'            - Brave: Google push service disabled by default (may still work)
//   'available'        - nothing blocking; consult Notification.permission
window.cnPushCapability = async function () {
    try {
        if (typeof window.isNativeIosApp === 'function' && window.isNativeIosApp()) {
            return 'native-ios';
        }
    } catch { /* fall through */ }

    if (!('serviceWorker' in navigator) || !('PushManager' in window)) {
        return 'unsupported';
    }

    try {
        var ua = navigator.userAgent || '';
        var isIos = /iPad|iPhone|iPod/.test(ua)
            || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
        var isStandalone = window.navigator.standalone === true
            || (window.matchMedia && window.matchMedia('(display-mode: standalone)').matches);
        if (isIos && !isStandalone) return 'requires-install';
    } catch { /* iOS detection unavailable */ }

    try {
        if (navigator.brave && typeof navigator.brave.isBrave === 'function') {
            if (await navigator.brave.isBrave()) return 'brave';
        }
    } catch { /* fall through */ }

    try {
        const brands = navigator.userAgentData && navigator.userAgentData.brands;
        if (Array.isArray(brands) && brands.some(b => /brave/i.test(b.brand))) return 'brave';
    } catch { /* brand list unavailable */ }

    return 'available';
};

// Register the service worker immediately on script load, before Blazor boots.
// Storing the promise lets registerServiceWorker() await the same work without re-registering.
let _swRegistrationPromise = null;
if ('serviceWorker' in navigator) {
    _swRegistrationPromise = navigator.serviceWorker
        .register('service-worker.js')
        .then(registration => {
            console.info(`Service worker registered (scope: ${registration.scope})`);
            registration.addEventListener('updatefound', () => {
                console.info("Update found!");
            });
            return registration;
        })
        .catch(error => {
            // Googlebot and other renderers can't register service workers, so
            // this rejection is expected noise rather than a real failure. Only
            // log it for actual browsers where it may indicate a real problem.
            if (!(window.cnIsBotRenderer && window.cnIsBotRenderer())) {
                console.error('Service worker registration failed:', error);
            }
            return null;
        });
}

// Blazor calls this after boot; the service worker is already registered by this point.
window.registerServiceWorker = async function () {
    return _swRegistrationPromise ? await _swRegistrationPromise : null;
};

// Flag to indicate the script has loaded (used by Blazor to wait for readiness)
window.pwaInstallReady = true;

// ---- Cold-boot cache progress ("Loading... 145/307") ----
//
// While the app boots, every asset the page requests is stored in the service
// worker's offline cache. This shows how many are cached in the button's busy
// label so the wait is visible to users instead of just a spinner.
//
// Purely observational: it READS the Cache API (shared between the page and the
// service worker) and writes text into the DOM. It changes no caching behaviour
// and sends no messages, so it is silent in the console. The total is the same
// asset manifest the worker precaches from, loaded as a plain script (which
// assigns window.assetsManifest).
(function () {
    if (!('caches' in window)) return;

    var lastCached = -1;
    var total = 0;

    var manifest = document.createElement('script');
    manifest.src = 'service-worker-assets.js';
    manifest.onload = function () {
        try { total = (window.assetsManifest && window.assetsManifest.assets || []).length; } catch (e) { total = 0; }
    };
    document.head.appendChild(manifest);

    function renderCount(count) {
        var labels = document.querySelectorAll('.cn-busy-content');
        for (var i = 0; i < labels.length; i++) {
            var span = labels[i].querySelector('.cn-busy-count');
            if (!span) {
                span = document.createElement('span');
                span.className = 'cn-busy-count';
                labels[i].appendChild(span);
            }
            span.textContent = ' ' + count + (total ? '/' + total : '');
        }
    }

    async function sample() {
        try {
            var names = await caches.keys();
            var name = names.filter(function (n) { return n.indexOf('offline-cache-') === 0; })[0];
            if (!name) return;
            var cached = (await (await caches.open(name)).keys()).length;
            if (cached !== lastCached) {
                lastCached = cached;
                renderCount(cached);
            }
        } catch (e) {
            // Cache API unavailable — nothing to show.
        }
    }

    var timer = setInterval(sample, 250);
    sample();

    // Stop once the app is interactive (the busy label is gone by then anyway),
    // with a hard cap so this can never leak a timer.
    var stopGuard = setInterval(function () {
        if (window.cnBlazorReady === true) stop();
    }, 500);
    setTimeout(stop, 120000);

    function stop() {
        clearInterval(timer);
        clearInterval(stopGuard);
    }
})();

// ---- iOS Native App Bridge (FCM Push Notifications) ----
//
// When running inside the CompanioNation iOS app wrapper (WKWebView),
// the native side sets window.companioNation_fcmToken after obtaining
// a device token from APNs?FCM. The Blazor client reads this to
// register FCM tokens instead of Web Push VAPID subscriptions.
//
// The native app also calls window.companioNation_setFcmToken(token)
// if the token refreshes while the page is already loaded.

window.companioNation_fcmToken = window.companioNation_fcmToken || null;
let _fcmTokenCallback = null;

// Normalizes any value the native bridge hands us into either a genuine token
// string or null. Rejects empty/whitespace AND the literal strings "null" /
// "undefined" (which Swift/JS interop can produce when interpolating a nil token).
// This is the single choke point that guarantees a blank/garbage FCM token can
// never be stored locally or forwarded to the server (defense in depth on top of
// the C# IsNullOrWhiteSpace guards).
function _sanitizeFcmToken(token) {
    if (token === null || token === undefined) return null;
    var t = String(token).trim();
    if (t === '' || t.toLowerCase() === 'null' || t.toLowerCase() === 'undefined') return null;
    return t;
}

// Called by the native iOS app when the FCM token is available or refreshes.
window.companioNation_setFcmToken = function (token) {
    var sanitized = _sanitizeFcmToken(token);
    if (!sanitized) {
        // Ignore blank/garbage tokens outright: do NOT overwrite a previously
        // stored valid token and do NOT fire the change callback (which would push
        // an empty token toward the server). The real token will arrive later.
        console.warn('[iOS Bridge] Ignoring empty/invalid FCM token from native bridge.');
        return;
    }
    window.companioNation_fcmToken = sanitized;
    console.info('[iOS Bridge] FCM token received (' + sanitized.length + ' chars).');
    if (_fcmTokenCallback) {
        _fcmTokenCallback(sanitized);
    }
};

// Called by Blazor to register a callback for FCM token changes.
window.companioNation_onFcmTokenChanged = function (dotnetHelper) {
    _fcmTokenCallback = function (token) {
        dotnetHelper.invokeMethodAsync('OnFcmTokenChanged', token);
    };
};

// Returns true if running inside the native iOS app wrapper.
window.isNativeIosApp = function () {
    return !!(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.companioNation);
};

// Returns true when the app is running inside one of the packaged "wrapper" apps
// rather than a regular web browser tab. Covers:
//   - the native iOS WKWebView wrapper (exposes our message handler),
//   - the Android Trusted Web Activity from Google Play (android-app:// referrer),
//   - an installed / Microsoft Store PWA (runs in a standalone display mode).
window.isWrapperApp = function () {
    try {
        if (window.isNativeIosApp()) {
            return true;
        }

        // Android Trusted Web Activity (Google Play) launches with this referrer.
        if (document.referrer && document.referrer.indexOf('android-app://') === 0) {
            return true;
        }

        // Installed / Store PWA (Microsoft Store, installed desktop/mobile PWA) runs
        // in a non-"browser" display mode rather than a normal browser tab.
        var mq = window.matchMedia;
        if (mq && (mq('(display-mode: standalone)').matches
            || mq('(display-mode: fullscreen)').matches
            || mq('(display-mode: minimal-ui)').matches
            || mq('(display-mode: window-controls-overlay)').matches)) {
            return true;
        }

        // iOS Safari "Add to Home Screen" standalone flag.
        if (window.navigator.standalone === true) {
            return true;
        }
    } catch (e) {
        // On any failure, fall back to "browser" so the store links remain available.
        console.warn('isWrapperApp detection failed:', e);
    }
    return false;
};

// Convenience inverse used by the landing page to decide whether to show the
// app-store badges (they should only appear in a real web browser).
window.isWebBrowser = function () {
    return !window.isWrapperApp();
};

// Returns the FCM token if available, or null. Sanitized so callers never see
// an empty string or the literal "null"/"undefined" (would blank the DB token).
window.getFcmToken = function () {
    return _sanitizeFcmToken(window.companioNation_fcmToken);
};

// Asks the native iOS wrapper to (re)fetch its FCM token and dispatch it back
// via the 'push-token' CustomEvent. Required after a full page reload (e.g. the
// post-Google-login Nav.NavigateTo("/", true)) because JS globals are cleared,
// so window.companioNation_fcmToken is null until the native side pushes it
// again — but the native side only dispatched it once at app start. Without this,
// DoLogin's GetPushTokenAsync returns empty and the token never reaches the DB.
window.companioNation_requestFcmToken = function () {
    try {
        if (window.isNativeIosApp()
            && window.webkit.messageHandlers['push-token']) {
            window.webkit.messageHandlers['push-token'].postMessage(null);
            console.info('[iOS Bridge] Requested FCM token from native side.');
        }
    } catch (e) {
        console.warn('companioNation_requestFcmToken failed:', e);
    }
};

// Listen for the 'push-token' CustomEvent dispatched by the native iOS app
// (PushNotifications.swift) when the FCM token is first retrieved or refreshes.
window.addEventListener('push-token', function (e) {
    // _sanitizeFcmToken inside companioNation_setFcmToken rejects blanks/garbage,
    // so it is safe to forward e.detail directly here.
    if (e.detail) {
        window.companioNation_setFcmToken(e.detail);
    }
});

// ---- Native iOS Push Notification Click / Delivery Bridge ----
//
// The web/Android path handles notification clicks in the service worker
// (service-worker.published.js -> 'notificationclick') which postMessages
// { action: 'navigate_to', url } to the page, and MainLayout.razor calls
// dotNetObject.NavigateToUrl(url). Native iOS has no service worker, so
// PushNotifications.swift dispatches these two CustomEvents into the WebView
// carrying the FCM userInfo JSON as e.detail. We mirror the SW behavior here.

function _parsePushDetail(detail) {
    if (!detail) return null;
    if (typeof detail === 'object') return detail;
    try { return JSON.parse(String(detail)); } catch (e) {
        console.warn('[iOS Push] Could not parse push detail JSON:', e);
        return null;
    }
}

// Fired when the user TAPS a native iOS notification. Route to the conversation
// the same way the web notificationclick handler does.
window.addEventListener('push-notification-click', function (e) {
    var data = _parsePushDetail(e.detail);
    if (!data) return;

    // FCM merges the server-side "data" dictionary into userInfo at the top level,
    // so 'url' and 'userId' arrive as direct keys (see FcmPushService.SendAsync).
    var url = data.url;
    if (!url && data.userId) {
        url = '/Messages/' + data.userId;
    }
    if (!url) {
        console.warn('[iOS Push] Click had no url/userId in payload:', data);
        return;
    }
    console.info('[iOS Push] Notification click -> navigate:', url);

    if (window.dotNetObject) {
        window.dotNetObject.invokeMethodAsync('NavigateToUrl', url);
    } else {
        // Blazor not booted yet (cold launch from a tapped notification).
        // Stash the target; MainLayout's setDotNetObjectReference will flush it
        // as soon as .NET is reachable. Show an "Opening link…" overlay so the
        // user isn't left staring at the main screen during the WASM boot.
        console.info('[iOS Push] Blazor not booted yet — stashing navigation url.');
        window._cnPendingNavigateUrl = url;
        if (window.cnShowDeepLinkOverlay) window.cnShowDeepLinkOverlay(url);
    }
});

// Fired when a native iOS notification is DELIVERED (foreground or wake).
// Mirrors the service worker's 'message_received' postMessage so the unread
// badge / conversation list refreshes without waiting for the next SignalR round-trip.
window.addEventListener('push-notification', function (e) {
    var data = _parsePushDetail(e.detail);
    if (!data) return;
    var userId = data.userId;
    if (userId === undefined || userId === null || userId === '') return;
    var userIdNum = parseInt(userId, 10);
    if (isNaN(userIdNum)) return;
    if (window.dotNetObject) {
        console.info('[iOS Push] Foreground push -> MessageReceived userId:', userIdNum);
        window.dotNetObject.invokeMethodAsync('MessageReceived', userIdNum);
    }
});

// ---- Native iOS Push Permission Bridge ----
//
// The native iOS app (ViewController.swift) handles 'push-permission-request'
// and 'push-permission-state' message posts and dispatches corresponding
// CustomEvents back to the WebView after calling UNUserNotificationCenter APIs.

// Helper to wait for a one-shot CustomEvent (mirrors the pattern in iap.js).
function _waitForNativeEvent(eventName, timeoutMs) {
    return new Promise(function (resolve, reject) {
        var timer = null;
        function handler(e) {
            if (timer) { clearTimeout(timer); }
            window.removeEventListener(eventName, handler);
            resolve(e.detail);
        }
        window.addEventListener(eventName, handler);
        if (timeoutMs && timeoutMs > 0) {
            timer = setTimeout(function () {
                window.removeEventListener(eventName, handler);
                reject(new Error('Timed out waiting for ' + eventName));
            }, timeoutMs);
        }
    });
}

// Requests native iOS push notification permission and returns the result.
// Returns a Promise that resolves to 'granted', 'denied', or rejects on timeout.
// MUST be called from a user gesture (button click) to preserve user activation.
window.requestNativeIosPushPermission = async function () {
    if (!window.isNativeIosApp()) {
        throw new Error('requestNativeIosPushPermission called outside native iOS app');
    }

    console.info('[iOS Push] Requesting native push permission...');

    // Set up listener for the result before posting to avoid race condition
    var resultPromise = _waitForNativeEvent('push-permission-request', 30000);

    // Post to the native handler (ViewController.swift will call handlePushPermission())
    window.webkit.messageHandlers['push-permission-request'].postMessage(null);

    var result = await resultPromise;
    console.info('[iOS Push] Permission result:', result);
    return result; // 'granted' or 'denied'
};

// Queries the current native iOS push permission state without prompting.
// Returns a Promise that resolves to the state string:
// 'notDetermined', 'denied', 'authorized', 'ephemeral', 'provisional', or 'unknown'.
window.getNativeIosPushState = async function () {
    if (!window.isNativeIosApp()) {
        return 'unsupported';
    }

    console.info('[iOS Push] Querying native push state...');

    var statePromise = _waitForNativeEvent('push-permission-state', 10000);
    window.webkit.messageHandlers['push-permission-state'].postMessage(null);

    var state = await statePromise;
    console.info('[iOS Push] Push state:', state);
    return state;
};

// Returns true when push-notification permission has actually been GRANTED for
// the current platform. Used to distinguish a genuine registration failure from
// the normal "user hasn't opted in yet" state (web: Notification API,
// native iOS: UNUserNotificationCenter authorization status).
window.isPushPermissionGranted = async function () {
    try {
        if (window.isNativeIosApp && window.isNativeIosApp()) {
            var state = await window.getNativeIosPushState();
            return state === 'authorized' || state === 'ephemeral' || state === 'provisional';
        }
        if (typeof Notification === 'undefined') return false;
        return Notification.permission === 'granted';
    } catch (e) {
        console.warn('[Push] isPushPermissionGranted check failed:', e);
        return false;
    }
};


// ---- Native iOS Google OAuth Bridge ----
//
// Google blocks / breaks OAuth (especially the multi-step 2FA continuation) inside an
// embedded WKWebView, producing a generic "400 That's an error" on accounts.google.com.
// On native iOS we therefore DON'T navigate the app's webview to Google. Instead we hand
// the fully-built authorization URL to the native wrapper (WebView.swift), which runs it in
// ASWebAuthenticationSession (shares Safari's cookie jar ? 2FA works) and returns the final
// custom-scheme callback URL. We parse the authorization `code` + `state` out of that URL and
// hand them back to Blazor for the normal server-side token exchange.
//
// The native side handles the 'google-oauth' message post and dispatches a
// 'google-oauth-result' CustomEvent whose detail is the callback URL string (or an object
// with { error } on failure/cancel).

// Runs the Google OAuth flow natively. `authUrl` is the complete accounts.google.com
// authorization URL (built by Login.razor.js with the iOS client_id + reversed-ID redirect).
// Custom error thrown when the user intentionally cancels the native OAuth flow.
// Login.razor.js catches this and silently aborts instead of treating it as a failure.
class OAuthCancelledError extends Error {
    constructor(message) {
        super(message);
        this.name = 'OAuthCancelledError';
        this.isCancellation = true;
    }
}

// `callbackScheme` is the reversed-client-ID URL scheme ASWebAuthenticationSession watches for.
// Resolves to { code, state } on success, or throws on error/cancel.
window.companioNation_startGoogleOAuth = async function (authUrl, callbackScheme) {
    if (!window.isNativeIosApp()) {
        throw new Error('companioNation_startGoogleOAuth called outside native iOS app');
    }

    console.info('[iOS OAuth] Starting native Google OAuth via ASWebAuthenticationSession...');

    // Listen for the result before posting to avoid a race.
    var resultPromise = _waitForNativeEvent('google-oauth-result', 300000); // 5 min: 2FA can be slow

    window.webkit.messageHandlers['google-oauth'].postMessage({
        url: authUrl,
        callbackScheme: callbackScheme
    });

    var detail = await resultPromise;

    // Native may report an explicit error / user cancellation.
    if (detail && typeof detail === 'object' && detail.error) {
        // User-initiated cancellations are not errors — throw a distinguishable type
        // so Login.razor.js can silently abort instead of logging a false alarm.
        if (detail.error === 'canceled' || detail.error === 'cancelled') {
            throw new OAuthCancelledError('User cancelled Google sign-in.');
        }
        throw new Error('[iOS OAuth] ' + detail.error);
    }

    // Otherwise detail is the callback URL string. Parse code + state.
    var callbackUrl = (detail && typeof detail === 'object' && detail.url) ? detail.url : detail;
    if (!callbackUrl || typeof callbackUrl !== 'string') {
        throw new Error('[iOS OAuth] Native returned no callback URL.');
    }

    // The callback is a custom-scheme URL (com.googleusercontent.apps.XXX:/oauth2redirect?...).
    // Its params live after the '?', which URL() parses even for custom schemes.
    var query = callbackUrl.indexOf('?') >= 0 ? callbackUrl.substring(callbackUrl.indexOf('?') + 1) : '';
    var params = new URLSearchParams(query);
    var err = params.get('error');
    if (err) {
        throw new Error('[iOS OAuth] Google returned error=' + err +
            (params.get('error_description') ? (', ' + params.get('error_description')) : ''));
    }
    return {
        code: params.get('code'),
        state: params.get('state')
    };
};


