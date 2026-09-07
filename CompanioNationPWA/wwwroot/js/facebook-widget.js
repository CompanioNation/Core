// Lazily loads the Facebook SDK and parses the Page Plugin in a specific container.
// Designed for Blazor WASM where inline <script> tags in components don't execute
// reliably during dynamic rendering.
//
// Responsive behavior: the fixed data-width fallback (500) made the feed overflow
// narrow screens and cause horizontal scrolling. We measure the container's client
// width (clamped to the same 500px cap), write it onto .fb-page before each parse,
// and re-parse on resize so the iframe always matches the available space.

window.loadFacebookWidget = function (containerId) {
    var container = document.getElementById(containerId);
    if (!container) return;

    function applyResponsiveWidth() {
        var page = container.querySelector('.fb-page');
        if (!page) return;

        var width = Math.max(180, Math.min(500, container.clientWidth || 500));
        page.setAttribute('data-width', String(width));
    }

    function parseContainer() {
        applyResponsiveWidth();
        if (window.FB && window.FB.XFBML) {
            window.FB.XFBML.parse(container);
        }
    }

    // SDK already loaded — just re-measure and re-parse this container
    if (window.FB && window.FB.XFBML) {
        parseContainer();
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
        parseContainer();
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

    // Re-parse (debounced) whenever the available width changes so the feed
    // tracks container size instead of overflowing it.
    if (!window.__cnFbResizeBound) {
        window.__cnFbResizeBound = true;
        var resizeTimer;
        var onResize = function () {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(function () {
                parseContainer();
            }, 150);
        };
        window.addEventListener('resize', onResize);
        window.addEventListener('orientationchange', onResize);
    }
};
