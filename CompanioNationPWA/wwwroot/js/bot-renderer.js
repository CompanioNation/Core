// bot-renderer.js
//
// Single source of truth for detecting automated "renderer" user agents —
// search-engine crawlers and inspection tools (Googlebot, Search Console URL
// Inspection, Lighthouse, etc.) that load the page, boot the WASM runtime, and
// then tear it down once they've captured the content.
//
// Why this exists: the Blazor WebAssembly framework treats that teardown as an
// unhandled error — ExitStatus 0 is a CLEAN shutdown, but the framework's
// callEntryPoint catch still paints the yellow #blazor-error-ui bar into the
// DOM, and it ends up in the screenshots Google stores. Real browsers never
// tear the runtime down that way, so real users keep the error bar for genuine
// crashes.
//
// To add a new crawler/renderer that misbehaves the same way, append its UA
// token to BOT_UA_TOKENS below. That's the only place that ever needs to
// change.

(function () {
    'use strict';

    var BOT_UA_TOKENS = [
        'Googlebot',
        'Google-InspectionTool',
        'Mediapartners-Google',
        'HeadlessChrome',
        'Chrome-Lighthouse'
    ];

    window.cnIsBotRenderer = function () {
        try {
            var ua = navigator.userAgent || '';
            return BOT_UA_TOKENS.some(function (token) {
                return ua.indexOf(token) !== -1;
            });
        } catch (e) {
            return false;
        }
    };

    // Hides the Blazor "unhandled error" bar for automated renderers so the
    // clean WASM shutdown on teardown never shows up in their captured
    // screenshots. No-op for everyone else. An !important rule wins over the
    // framework's inline style="display:block".
    window.cnHideBlazorErrorUiForBotRenderers = function () {
        if (!window.cnIsBotRenderer()) return;
        var style = document.createElement('style');
        style.textContent = '#blazor-error-ui { display: none !important; }';
        document.head.appendChild(style);
    };
})();
