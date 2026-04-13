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
            }
        } catch (e) {
            console.error('[Service Worker] Failed to parse push payload:', e);
        }

        const title = data.title || 'New Notification';
        const options = data.options || {};

        // Show the notification on the platform
        await self.registration.showNotification(title, options);

        // Update the UI
        try {
            const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
            const userId = options.data?.userId;
            if (userId) {
                clients.forEach(client => {
                    console.info('Sending received message to client:', client);
                    client.postMessage({ action: 'message_received', userId });
                });
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
    console.log('[Service Worker] Notification click, userId:', userId);
    event.notification.close();

    event.waitUntil(
        clients.matchAll({ type: 'window' }).then(clientList => {
            if (clientList.length > 0) {
                return clientList[0].focus().then(client => client.navigate(url));
            }
            return clients.openWindow(url);
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
