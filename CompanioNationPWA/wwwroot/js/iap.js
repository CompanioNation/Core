// === Apple In-App Purchase bridge ==========================================
// Communicates with the native CompanioNation iOS wrapper (WKWebView) so a
// subscription can be purchased through StoreKit and activated on the server.
//
// The native side exposes message handlers (iap-products-request,
// iap-purchase-request, ...) and dispatches CustomEvents back to the page
// (iap-products-result, iap-purchase-transaction, iap-purchase-result, ...).
// See ios-app/pwa-shell/IAP.swift and ViewController.swift.

window.companioNationIap = (function () {
    "use strict";

    function isNativeIosApp() {
        return !!(window.webkit
            && window.webkit.messageHandlers
            && window.webkit.messageHandlers.companioNation);
    }

    function post(handlerName, body) {
        if (!window.webkit
            || !window.webkit.messageHandlers
            || !window.webkit.messageHandlers[handlerName]) {
            throw new Error("Native handler not available: " + handlerName);
        }
        window.webkit.messageHandlers[handlerName].postMessage(body);
    }

    // Wait for a one-shot CustomEvent, resolving with its detail or rejecting on timeout.
    function once(eventName, timeoutMs) {
        return new Promise(function (resolve, reject) {
            var timer = null;
            function handler(e) {
                if (timer) { clearTimeout(timer); }
                window.removeEventListener(eventName, handler);
                resolve(e.detail);
            }
            window.addEventListener(eventName, handler);
            if (timeoutMs && timeoutMs > 0) {
                timer = setTimeout(function () {
                    window.removeEventListener(eventName, handler);
                    reject(new Error("Timed out waiting for " + eventName));
                }, timeoutMs);
            }
        });
    }

    // Ask StoreKit to load the given product so it is available to purchase.
    async function fetchProducts(productId) {
        var resultPromise = once("iap-products-result", 30000);
        post("iap-products-request", [productId]);
        return await resultPromise;
    }

    // Purchase the product and return the StoreKit transaction JSON string.
    async function purchase(productId, userUuid) {
        var transactionPromise = once("iap-purchase-transaction", 300000);
        post("iap-purchase-request", JSON.stringify({
            productID: productId,
            quantity: 1,
            userUUID: userUuid || "00000000-0000-0000-0000-000000000000"
        }));
        // iap-purchase-transaction fires only on success; iap-purchase-result reports state.
        return await transactionPromise;
    }

    // Full flow: load product, purchase, then return the signed transaction so the
    // caller can post it to the server for activation. Returns null if not native.
    async function buySubscription(productId, userUuid) {
        if (!isNativeIosApp()) {
            return null;
        }
        await fetchProducts(productId);
        var transaction = await purchase(productId, userUuid);
        return transaction;
    }

    return {
        isNativeIosApp: isNativeIosApp,
        fetchProducts: fetchProducts,
        purchase: purchase,
        buySubscription: buySubscription
    };
})();
