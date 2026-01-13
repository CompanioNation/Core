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

    let data = {};
    if (event.data) {
        data = event.data.json();
    }

    // Show the notification on the platform
    event.waitUntil(self.registration.showNotification(data.title, data.options));


    // Update the UI
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clients) => {
        clients.forEach(client => {
            console.info('Sending received message to client:', client);
            client.postMessage({ action: 'message_received', userId: data.options.data.userId });
        });
    });
});

// Handle notification click
self.addEventListener('notificationclick', event => {
    const userId = event.notification.data?.userId; // Extract UserId from notification data
    console.log("NOTIFICATION userid: " + userId);
    event.notification.close();

    // Focus or open the app window if applicable
    event.waitUntil(clients.matchAll({ type: 'window' }).then(clientList => {
        if (clientList.length > 0) {
            return clientList[0].focus().then(client => {
                client.navigate(`/Messages/${userId}`); // Navigate to the message URL
            });
        }
        return clients.openWindow(`/Messages/${userId}`); // Open a new window with the message URL
    }));
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
