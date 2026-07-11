// analytics.js
// Google Analytics 4 event tracking helper for CompanioNation™

(function() {
    'use strict';

    // Detect the app shell context once (cached for session)
    var _appShell = null;

    function getAppShell() {
        if (_appShell !== null) {
            return _appShell;
        }

        try {
            // Native iOS WKWebView wrapper
            if (window.isNativeIosApp && window.isNativeIosApp()) {
                _appShell = 'ios';
                return _appShell;
            }

            // Android Trusted Web Activity (Google Play)
            if (document.referrer && document.referrer.indexOf('android-app://') === 0) {
                _appShell = 'android_twa';
                return _appShell;
            }

            // Installed/Store PWA (Microsoft Store, installed desktop/mobile PWA)
            var mq = window.matchMedia;
            if (mq && (mq('(display-mode: standalone)').matches
                || mq('(display-mode: fullscreen)').matches
                || mq('(display-mode: minimal-ui)').matches
                || mq('(display-mode: window-controls-overlay)').matches)) {
                _appShell = 'installed_pwa';
                return _appShell;
            }

            // iOS Safari "Add to Home Screen" standalone mode
            if (window.navigator.standalone === true) {
                _appShell = 'installed_pwa';
                return _appShell;
            }

            // Standard web browser
            _appShell = 'browser';
            return _appShell;
        } catch (e) {
            console.warn('[Analytics] Failed to detect app_shell:', e);
            _appShell = 'browser'; // Safe fallback
            return _appShell;
        }
    }

    /**
     * Send a custom event to Google Analytics 4 via GTM dataLayer.
     * Automatically adds the 'app_shell' parameter to all events.
     * 
     * @param {string} eventName - GA4 event name (e.g., 'sign_up', 'purchase')
     * @param {object} params - Optional event parameters (e.g., { method: 'email', value: 9.99, currency: 'USD' })
     * 
     * @example
     * gaEvent('sign_up', { method: 'email' });
     * gaEvent('purchase', { transaction_id: 'txn_123', value: 19.99, currency: 'USD', items: [...] });
     * gaEvent('companion_viewed', { companion_id: 'uuid-here' });
     */
    window.gaEvent = function(eventName, params) {
        if (!eventName || typeof eventName !== 'string') {
            console.warn('[Analytics] gaEvent called with invalid event name:', eventName);
            return;
        }

        try {
            // Initialize dataLayer if it doesn't exist (GTM should have created it, but be defensive)
            window.dataLayer = window.dataLayer || [];

            // Merge app_shell with provided params
            var eventData = {
                event: eventName,
                app_shell: getAppShell()
            };

            if (params && typeof params === 'object') {
                for (var key in params) {
                    if (params.hasOwnProperty(key)) {
                        eventData[key] = params[key];
                    }
                }
            }

            // Push to dataLayer (GTM forwards to GA4)
            window.dataLayer.push(eventData);

            console.info('[Analytics] Event sent:', eventName, eventData);
        } catch (error) {
            console.error('[Analytics] Failed to send event:', eventName, error);
        }
    };

    /**
     * Helper to send a page view event.
     * In most cases, GTM auto-tracking handles this via history changes,
     * but this is available for manual tracking if needed.
     * 
     * @param {string} pagePath - The virtual page path (e.g., '/profile/edit')
     * @param {string} pageTitle - The page title
     */
    window.gaPageView = function(pagePath, pageTitle) {
        window.gaEvent('page_view', {
            page_path: pagePath || window.location.pathname,
            page_title: pageTitle || document.title
        });
    };

    console.info('[Analytics] analytics.js loaded. App shell:', getAppShell());
})();
