// Lazily loads the Facebook SDK and parses the Page Plugin in a specific container.
// Designed for Blazor WASM where inline <script> tags in components don't execute
// reliably during dynamic rendering.

window.loadFacebookWidget = function (containerId) {
    var container = document.getElementById(containerId);
    if (!container) return;

    // SDK already loaded — just re-parse this container
    if (window.FB && window.FB.XFBML) {
        window.FB.XFBML.parse(container);
        return;
    }

    // Ensure fb-root exists (required by Facebook SDK)
    if (!document.getElementById('fb-root')) {
        var root = document.createElement('div');
        root.id = 'fb-root';
        document.body.prepend(root);
    }

    // Callback fires once SDK finishes loading
    window.fbAsyncInit = function () {
        FB.init({ xfbml: false, version: 'v22.0' });
        FB.XFBML.parse(container);
    };

    // Load the SDK script (once)
    if (!document.getElementById('facebook-jssdk')) {
        var script = document.createElement('script');
        script.id = 'facebook-jssdk';
        script.async = true;
        script.src = 'https://connect.facebook.net/en_US/sdk.js';
        script.onerror = function () {
            console.warn('Facebook SDK failed to load (blocked or network error)');
        };
        document.head.appendChild(script);
    }
};
