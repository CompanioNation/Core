// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

self.addEventListener('install', (event) => {
    console.log("install event");
    self.skipWaiting();
});

// Handle incoming push events
self.addEventListener('push', event => {
    console.log('[Service Worker] Push Received.');

    const work = (async () => {
        let data = {};
        try {
            if (event.data) {
                data = event.data.json();
                console.info('[Service Worker] Push payload:', JSON.stringify(data).substring(0, 200));
            } else {
                console.warn('[Service Worker] Push event has no data payload.');
            }
        } catch (e) {
            console.error('[Service Worker] Failed to parse push payload:', e);
        }

        const title = data.title || 'New Notification';
        const options = data.options || {};
        console.info('[Service Worker] Showing notification. Title:', title, 'userId:', options.data?.userId || 'none');

        // Show the notification on the platform
        await self.registration.showNotification(title, options);

        // Update the UI
        try {
            const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
            const userId = options.data?.userId;
            console.info('[Service Worker] Open clients:', clients.length, 'userId to forward:', userId || 'none');
            if (userId) {
                clients.forEach(client => {
                    console.info('[Service Worker] Sending message_received to client:', client.url);
                    client.postMessage({ action: 'message_received', userId });
                });
            } else {
                console.warn('[Service Worker] No userId in notification data — cannot forward to clients.');
            }
        } catch (e) {
            console.error('[Service Worker] Failed to notify clients:', e);
        }
    })();

    event.waitUntil(work);
});

// Handle notification click
self.addEventListener('notificationclick', event => {
    const userId = event.notification.data?.userId;
    const url = event.notification.data?.url || (userId ? `/Messages/${userId}` : '/');
    console.log('[Service Worker] Notification click. userId:', userId, 'url:', url);
    event.notification.close();

    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(clientList => {
            console.info('[Service Worker] Notification click: found', clientList.length, 'open window(s).');
            if (clientList.length > 0) {
                console.info('[Service Worker] Focusing existing window and posting navigate_to:', url);
                return clientList[0].focus().then(client => {
                    client.postMessage({ action: 'navigate_to', url });
                });
            }
            console.info('[Service Worker] No open window found — opening new window:', url);
            return self.clients.openWindow(url);
        }).catch(err => {
            console.error('[Service Worker] Notification click failed:', err);
        })
    );
});

self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
async function onActivate(event) {
    console.info('Service Worker: Activate Begin');

    // Claim all clients so the service worker takes control of them immediately
    event.waitUntil(
        self.clients.claim().then(() => {
            // Now notify the clients to navigate
            self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
                clients.forEach(client => {
                    console.info('Sending navigate message to client:', client);
                    client.postMessage({ action: 'navigate' });
                });
            });
        })
    );


    console.info("Service Worker: Activate Complete!");
}
