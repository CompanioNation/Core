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

window.installNewVersion = async function () {
    if (!('serviceWorker' in navigator)) {
        console.warn('Service Worker not supported.');
        return;
    }
    try {
        console.info('NEW VERSION DETECTED!');
        const registration = await navigator.serviceWorker.ready;
        console.info('Updating...');
        await registration.update();
        console.info('Update check complete');
    } catch (error) {
        console.error('Update failed:', error);
    }
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

    navigator.serviceWorker.addEventListener('message', (event) => {
        if (event.data.action === 'navigate') {
            console.info("Refresh current page request received");
            window.location.reload();
        }
    });
}

// Blazor calls this after boot; the service worker is already registered by this point.
window.registerServiceWorker = async function () {
    return _swRegistrationPromise ? await _swRegistrationPromise : null;
};

// Flag to indicate the script has loaded (used by Blazor to wait for readiness)
window.pwaInstallReady = true;

