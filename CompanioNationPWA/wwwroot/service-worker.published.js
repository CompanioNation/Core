/*

OLD SERVICE WORKER FOR DEBUGGING
BEGIN

// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
self.addEventListener('fetch', () => { });

END
OLD SERVICE WORKER FOR DEBUGGING

*/

// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));


// Handle message received event
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SHOW_NOTIFICATION') {
        const { title, options, userId } = event.data.payload;
        options.data = { url: `/Messages/${userId}`, userId }; // Pass the UserId to the notification data
        self.registration.showNotification(title, options);
    }
});

// Handle notification click
self.addEventListener('notificationclick', event => {
    console.log('[Service Worker] Notification click Received.');

    const userId = event.notification.data?.userId; // Extract UserId from notification data
    const url = event.notification.data?.url;
    event.notification.close();

    // Focus or open the app window if applicable
    event.waitUntil(clients.matchAll({ type: 'window' }).then(clientList => {
        if (clientList.length > 0) {
            return clientList[0].focus().then(client => {
                client.navigate(url); // Navigate to the message URL
            });
        }
        return clients.openWindow(url); // Open a new window with the message URL
    }));

});


// **New Code for Push Notifications Start Here**


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


// **New Code for Push Notifications End Here**




const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html$/, /\.js$/, /\.json$/, /\.css$/, /\.woff$/, /\.png$/, /\.jpe?g$/, /\.gif$/, /\.ico$/, /\.blat$/, /\.dat$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

// Replace with your base path if you are hosting on a subfolder. Ensure there is a trailing '/'.
const base = "/";
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall(event) {
    console.info('Service Worker: Install Begin');

    try {
        const cache = await caches.open(cacheName);
        const assetsRequests = self.assetsManifest.assets
            .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
            .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
            .map(asset => new Request(asset.url, { cache: 'no-cache' }));

        console.log('Caching assets:', assetsRequests);
        await Promise.all(assetsRequests.map(async (request) => {
            try {
                const response = await fetch(request);
                if (response.ok) {
                    await cache.put(request, response);
                } else {
                    console.error('Failed to fetch:', request.url, response.status, response.statusText);
                }
            } catch (error) {
                console.error('Failed to fetch:', request.url, error);
            }
        }));
        console.info('Assets cached successfully');
    } catch (error) {
        console.error('Failed to cache assets during install', error);
    }

    // Now go into the Activate phase
    self.skipWaiting();
    console.info("Service Worker: Install Complete!");
}

async function onActivate(event) {
    console.info('Service Worker: Activate Begin');

    try {
        const cacheKeys = await caches.keys();
        await Promise.all(cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => caches.delete(key)));
        console.info('Old caches deleted');
    } catch (error) {
        console.error('Failed to delete old caches during activate', error);
    }


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

async function onFetch(event) {

    if (event.request.method === 'GET') {
        const requestURL = new URL(event.request.url);

        // Skip caching requests with unsupported schemes (e.g., data:, chrome-extension:, etc.)
        if (requestURL.protocol !== 'http:' && requestURL.protocol !== 'https:') {
            return fetch(event.request);
        }

        const cache = await caches.open(cacheName);


        // Determine if the request should serve index.html
        const shouldServeIndexHtml =
            (event.request.mode === 'navigate' && !manifestUrlList.some(url => url === event.request.url)) ||
            requestURL.href === baseUrl.href;

        // Use a Request object for index.html with cache busting
        const request = shouldServeIndexHtml ? new Request('index.html', { cache: 'reload' }) : event.request;

        try {
            const cachedResponse = await cache.match(request);
            if (cachedResponse) {
                console.info('Cache hit:', request.url);
                return cachedResponse;
            } else {
                console.info('Cache miss:', request.url);
            }
        } catch (error) {
            console.error('Failed to fetch or cache match:', error);
        }

        // Fetch from the network if not found in the cache
        let networkResponse;
        try {
            if (requestURL.origin !== self.origin) {
                // Make sure to use mode: 'cors' for Cross-Origin requests, so that we can cache it
                networkResponse = await fetch(request, { mode: 'cors', cache: 'no-cache' });
            } else {
                networkResponse = await fetch(request, { cache: 'no-cache' });
            }
            if (networkResponse && networkResponse.ok) {
                console.info('Network fetch successful:', request.url);

                // Cache the fetched response asynchronously
                cache.put(event.request, networkResponse.clone()).catch(cacheError => {
                    console.error('Failed to cache network response:', cacheError);
                });
                return networkResponse;
            } else {
                console.error('Network fetch failed:', event.request.url, networkResponse.status, networkResponse.statusText);
            }
        } catch (networkError) {
            console.error('Network fetch threw an error:', networkError);
        }
    }

    // Fallback fetch if all else fails
    return fetch(event.request).catch(fetchError => {
        console.error('Default fetch attempt failed:', fetchError);
    });
}


