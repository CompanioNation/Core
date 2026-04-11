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

// Directly call this function on user action (like button click)
window.requestNotificationPermission = requestNotificationPermission;
function requestNotificationPermission() {
    if (!("Notification" in window)) {
        console.log("This browser does not support desktop notification.");
        return;
    }

    if (Notification.permission === "granted") {
        console.log("Notifications are already enabled.");
    } else if (Notification.permission !== "denied") {
        Notification.requestPermission().then((permission) => {
            if (permission === "granted") {
                console.log("Notifications enabled successfully.");
            } else {
                console.log("Notifications denied.");
            }
        }).catch((error) => {
            console.error("Error requesting notification permission:", error);
        });
    } else {
        console.log("Notifications are denied. Please enable them in your browser settings.");
    }
}

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

// VAPID public key is passed from Blazor (from CompanioNation.Shared.Util.VapidPublicKey)
window.registerPush = async function (vapidPublicKey) {
    if (!("Notification" in window)) {
        console.log("This browser does not support desktop notification.");
        return;
    }

    if (Notification.permission === "granted") {
        const registration = await navigator.serviceWorker.ready;  // wait until the service worker is ready

        // Check if a push subscription already exists
        let subscription = await registration.pushManager.getSubscription();

        if (!subscription) {
            console.info("Registering for PUSH updates...");
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: vapidPublicKey  // Passed from Blazor
            });
        }

        var push_token = JSON.stringify(subscription);

        return push_token;
    }
}

// Validates the current push subscription and re-registers if needed.
// Returns the push token JSON string if a valid subscription exists, or null.
// This is safe to call frequently — it only subscribes if permission is granted
// and no existing subscription is found.
window.validatePushSubscription = async function (vapidPublicKey) {
    if (!("Notification" in window) || !('serviceWorker' in navigator)) {
        return null;
    }

    if (Notification.permission !== "granted") {
        return null;
    }

    try {
        const registration = await navigator.serviceWorker.ready;
        let subscription = await registration.pushManager.getSubscription();

        if (subscription) {
            // Verify the subscription endpoint is still reachable by checking
            // that the endpoint URL hasn't become empty/null (browser-side invalidation).
            if (!subscription.endpoint) {
                console.info("Push subscription endpoint is invalid, re-subscribing...");
                await subscription.unsubscribe();
                subscription = null;
            }
        }

        if (!subscription) {
            console.info("No push subscription found, re-subscribing...");
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: vapidPublicKey
            });
        }

        return JSON.stringify(subscription);
    } catch (error) {
        console.error("Failed to validate/refresh push subscription:", error);
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

// ──── iOS Native App Bridge (FCM Push Notifications) ────
//
// When running inside the CompanioNation iOS app wrapper (WKWebView),
// the native side sets window.companioNation_fcmToken after obtaining
// a device token from APNs→FCM. The Blazor client reads this to
// register FCM tokens instead of Web Push VAPID subscriptions.
//
// The native app also calls window.companioNation_setFcmToken(token)
// if the token refreshes while the page is already loaded.

window.companioNation_fcmToken = window.companioNation_fcmToken || null;
let _fcmTokenCallback = null;

// Called by the native iOS app when the FCM token is available or refreshes.
window.companioNation_setFcmToken = function (token) {
    window.companioNation_fcmToken = token;
    console.info('[iOS Bridge] FCM token received.');
    if (_fcmTokenCallback) {
        _fcmTokenCallback(token);
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

// Returns the FCM token if available, or null.
window.getFcmToken = function () {
    return window.companioNation_fcmToken || null;
};

// Listen for the 'push-token' CustomEvent dispatched by the native iOS app
// (PushNotifications.swift) when the FCM token is first retrieved or refreshes.
window.addEventListener('push-token', function (e) {
    if (e.detail) {
        window.companioNation_setFcmToken(e.detail);
    }
});


