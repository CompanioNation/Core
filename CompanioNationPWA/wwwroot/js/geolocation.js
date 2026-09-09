// Geolocation interop for the "Use my current location" feature.
// Wraps navigator.geolocation in a Promise and returns either
// { ok: true, latitude, longitude } or { ok: false, error } where error is
// the GeolocationPositionError code (1 = permission denied, 2 = position
// unavailable, 3 = timeout, 0 = unsupported).
window.companioNationGeo = {
    getCurrentPosition: function () {
        return new Promise(function (resolve) {
            if (!('geolocation' in navigator)) {
                resolve({ ok: false, error: 0 });
                return;
            }

            navigator.geolocation.getCurrentPosition(
                function (position) {
                    resolve({
                        ok: true,
                        latitude: position.coords.latitude,
                        longitude: position.coords.longitude
                    });
                },
                function (error) {
                    resolve({ ok: false, error: error.code });
                },
                {
                    enableHighAccuracy: false,
                    timeout: 10000,
                    maximumAge: 300000
                });
        });
    },

    // Returns 'granted', 'prompt', or 'denied'. Some browsers don't expose
    // the Permissions API for geolocation, in which case this returns
    // 'unknown' and the caller falls back to requesting the position.
    getPermissionState: function () {
        if (!navigator.permissions || !navigator.permissions.query) {
            return Promise.resolve('unknown');
        }
        return navigator.permissions
            .query({ name: 'geolocation' })
            .then(function (status) { return status.state; })
            .catch(function () { return 'unknown'; });
    }
};
