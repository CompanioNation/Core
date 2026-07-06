// === Native store In-App Purchase bridge ===================================
// Lets a CompanioNita subscription be purchased through the active native shell
// and activated on the server:
//   - Apple iOS wrapper (WKWebView + StoreKit) via message handlers.
//   - Google Play TWA (Bubblewrap) and Microsoft Store PWA (PWABuilder) via the
//     W3C Digital Goods API + Payment Request API.
//
// The iOS native side exposes message handlers (iap-products-request,
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

    // === Digital Goods API (Google Play TWA / Microsoft Store PWA) ===========
    // Both the Android (Bubblewrap TWA) and Windows (PWABuilder) shells expose the
    // W3C Digital Goods API plus the Payment Request API. We tell the two stores
    // apart by which Digital Goods service backend resolves.

    var GOOGLE_PLAY_SERVICE = "https://play.google.com/billing";
    var MICROSOFT_STORE_SERVICE = "https://store.microsoft.com/billing";

    // Returns the Digital Goods service for the given backend URL, or null if the
    // API is unavailable or that store backend is not present.
    async function getDigitalGoodsService(serviceUrl) {
        if (typeof window.getDigitalGoodsService !== "function") {
            return null;
        }
        try {
            return await window.getDigitalGoodsService(serviceUrl);
        } catch (e) {
            return null;
        }
    }

    async function isGooglePlayApp() {
        return (await getDigitalGoodsService(GOOGLE_PLAY_SERVICE)) !== null;
    }

    async function isMicrosoftStoreApp() {
        return (await getDigitalGoodsService(MICROSOFT_STORE_SERVICE)) !== null;
    }

    // Resolves the active store shell: "apple" | "google" | "microsoft" | "web".
    async function detectStore() {
        if (isNativeIosApp()) {
            return "apple";
        }
        if (await isGooglePlayApp()) {
            return "google";
        }
        if (await isMicrosoftStoreApp()) {
            return "microsoft";
        }
        // Falling back to the web (Square/CCBill/BMC) checkout. If this is logged while
        // running inside the Android TWA or Windows Store shell, the Digital Goods API
        // is not exposed to the page - usually because the installed shell was built
        // without Play Billing / Store billing, or the app is not recognised as
        // store-installed. Rebuild/reinstall the shell with billing enabled.
        if (typeof window.getDigitalGoodsService !== "function") {
            console.info("[iap] detectStore -> web: Digital Goods API unavailable in this shell.");
        } else {
            console.info("[iap] detectStore -> web: Digital Goods API present but no store backend resolved.");
        }
        return "web";
    }

    // Purchase a subscription through the Digital Goods + Payment Request APIs.
    // serviceUrl/methodUrl select the store backend. Returns the purchase token
    // (Google) or Store ID token (Microsoft) for server-side validation, or null.
    async function buyDigitalGoods(serviceUrl, methodUrl, productId) {
        var service = await getDigitalGoodsService(serviceUrl);
        if (!service) {
            return null;
        }

        // Confirm the product exists (also surfaces price/title to the store sheet).
        try {
            await service.getDetails([productId]);
        } catch (e) {
            // Non-fatal: proceed to let the payment sheet report any real error.
        }

        var methodData = [{
            supportedMethods: methodUrl,
            data: { sku: productId }
        }];

        var request = new PaymentRequest(methodData, {
            total: {
                label: "CompanioNita Premium",
                amount: { currency: "USD", value: "0" }
            }
        });

        var response = await request.show();
        var token = response.details && response.details.token ? response.details.token : null;
        await response.complete("success");
        return token;
    }

    // Convenience: purchase via Google Play and return the purchase token.
    async function buyWithGooglePlay(productId) {
        return await buyDigitalGoods(GOOGLE_PLAY_SERVICE, GOOGLE_PLAY_SERVICE, productId);
    }

    // Convenience: purchase via the Microsoft Store and return the Store ID token.
    async function buyWithMicrosoftStore(productId) {
        return await buyDigitalGoods(MICROSOFT_STORE_SERVICE, MICROSOFT_STORE_SERVICE, productId);
    }

    return {
        isNativeIosApp: isNativeIosApp,
        fetchProducts: fetchProducts,
        purchase: purchase,
        buySubscription: buySubscription,
        isGooglePlayApp: isGooglePlayApp,
        isMicrosoftStoreApp: isMicrosoftStoreApp,
        detectStore: detectStore,
        buyWithGooglePlay: buyWithGooglePlay,
        buyWithMicrosoftStore: buyWithMicrosoftStore
    };
})();
