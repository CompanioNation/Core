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
        console.warn('[Push] Permission not granted — returning without subscribing.');
        return { permission, pushToken: null };
    }

    // Permission granted — subscribe (or re-validate existing subscription)
    const pushToken = await window.validatePushSubscription(vapidPublicKey);
    console.info('[Push] requestNotificationPermission complete. pushToken:', pushToken ? 'obtained' : 'null');
    return { permission, pushToken };
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

// Validates the current push subscription and re-registers if needed.
// Returns the push token JSON string if a valid subscription exists, or null.
// This is safe to call frequently
// and no existing subscription is found.
window.validatePushSubscription = async function (vapidPublicKey) {
    console.info('[Push] validatePushSubscription called.');

    if (!("Notification" in window) || !('serviceWorker' in navigator)) {
        console.warn('[Push] validatePushSubscription: Notification API or Service Worker not available. Notification:', ("Notification" in window), 'SW:', ('serviceWorker' in navigator));
        return null;
    }

    if (Notification.permission !== "granted") {
        console.warn('[Push] validatePushSubscription: Permission is "' + Notification.permission + '", not "granted". Skipping.');
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
        console.error('[Push] Failed to validate/refresh push subscription:', error);
        return null;
    }
}

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
            // Google bot can't register service workers, so an error is thrown and we just need to ignore it
            console.error('Service worker registration failed:', error);
            return null;
        });
}

// Blazor calls this after boot; the service worker is already registered by this point.
window.registerServiceWorker = async function () {
    return _swRegistrationPromise ? await _swRegistrationPromise : null;
};

// Flag to indicate the script has loaded (used by Blazor to wait for readiness)
window.pwaInstallReady = true;

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

// Listen for the 'push-token' CustomEvent dispatched by the native iOS app
// (PushNotifications.swift) when the FCM token is first retrieved or refreshes.
window.addEventListener('push-token', function (e) {
    // _sanitizeFcmToken inside companioNation_setFcmToken rejects blanks/garbage,
    // so it is safe to forward e.detail directly here.
    if (e.detail) {
        window.companioNation_setFcmToken(e.detail);
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


