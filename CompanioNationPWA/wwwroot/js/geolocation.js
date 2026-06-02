// Geolocation interop for the "Use my current location" feature.
// Wraps navigator.geolocation in a Promise and returns a simple
// { latitude, longitude } object, or null when the position cannot be
// obtained (permission denied, unavailable, timeout, or unsupported).
window.companioNationGeo = {
    getCurrentPosition: function () {
        return new Promise(function (resolve) {
            if (!('geolocation' in navigator)) {
                resolve(null);
                return;
            }

            navigator.geolocation.getCurrentPosition(
                function (position) {
                    resolve({
                        latitude: position.coords.latitude,
                        longitude: position.coords.longitude
                    });
                },
                function () {
                    // Permission denied, position unavailable, or timed out.
                    resolve(null);
                },
                {
                    enableHighAccuracy: false,
                    timeout: 10000,
                    maximumAge: 300000
                });
        });
    }
};
