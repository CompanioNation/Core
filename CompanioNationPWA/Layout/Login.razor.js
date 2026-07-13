// Google OAuth / Identity Services integration for Login.razor
// OAuth Authorization Code (PKCE) via manual authorization endpoint redirect (no GSI library).

let _redirectInProgress = false;
const AUTH_ENDPOINT = 'https://accounts.google.com/o/oauth2/v2/auth';

// Native iOS Google OAuth uses a DEDICATED iOS OAuth client (not the web client) whose
// redirect_uri is the reversed client ID custom scheme. This is Google's required, supported
// path for native apps and avoids the "400 malformed" that occurs when the multi-step 2FA
// flow runs inside the embedded WKWebView. The iOS client uses PKCE and NO client_secret.
const GOOGLE_IOS_CLIENT_ID = '184112114846-rbdfshh3b7l9hg9n5d8kspm7bvvf27j1.apps.googleusercontent.com';
const GOOGLE_IOS_REDIRECT_SCHEME = 'com.googleusercontent.apps.184112114846-rbdfshh3b7l9hg9n5d8kspm7bvvf27j1';
const GOOGLE_IOS_REDIRECT_URI = GOOGLE_IOS_REDIRECT_SCHEME + ':/oauth2redirect';

// When the user returns from an external OAuth flow (PWA / native app scenarios
// where the page stays alive), reset the redirect guard so they can try again.
document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') {
        _redirectInProgress = false;
    }
});

// --- Random state generator ---
function generateState(bytes = 32) {
    const arr = new Uint8Array(bytes);
    crypto.getRandomValues(arr);
    let bin = '';
    for (let i = 0; i < arr.length; i++) bin += String.fromCharCode(arr[i]);
    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

// --- PKCE helpers ---
function base64UrlFromArrayBuffer(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function generateCodeVerifier(byteLen = 64) {
    const arr = new Uint8Array(byteLen);
    crypto.getRandomValues(arr);
    let bin = '';
    for (let i = 0; i < arr.length; i++) bin += String.fromCharCode(arr[i]);
    // base64url-encode to an RFC7636-compliant, high-entropy string (43-128 chars)
    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

async function pkceChallengeFromVerifier(verifier) {
    const data = new TextEncoder().encode(verifier);
    const digest = await crypto.subtle.digest('SHA-256', data);
    return base64UrlFromArrayBuffer(digest);
}

// --- Build the OAuth 2.0 authorization request URL ---
// Minimal OpenID Connect Authorization Code + PKCE request.
// We intentionally omit access_type=offline / prompt=consent / include_granted_scopes:
// the backend never uses a refresh token, and forcing the offline + consent flow on a
// fresh sign-in inflates Google's multi-step sign-in chain and triggers a generic 400 on
// the first authorization (it works on retry once the Google session already exists).
// We also omit prompt=select_account: when 2-factor authentication is required, forcing
// the account chooser rebuilds Google's multi-step sign-in chain across the 2FA
// continuation and produces the same generic 400 AFTER the user completes 2FA but before
// returning to the app (again, it only succeeds on the second attempt once Google's
// session already exists). Omitting the prompt lets Google complete the 2FA flow in a
// single continuation. Users can still switch accounts from Google's own sign-in screen.
function buildAuthUrl(clientId, { state, codeChallenge, redirectUri }) {
    const params = new URLSearchParams({
        client_id: clientId,
        redirect_uri: redirectUri,
        response_type: 'code',
        scope: 'openid email profile',
        code_challenge: codeChallenge,
        code_challenge_method: 'S256',
        state: state
    });
    return `${AUTH_ENDPOINT}?${params.toString()}`;
}

// --- Main login function called by Blazor ---
window.googleLogin = async function () {
    if (_redirectInProgress) return;

    const isNativeIos = !!(window.isNativeIosApp && window.isNativeIosApp());

    // On native iOS the web client can't be used inside the WKWebView (2FA "400"). Use the
    // dedicated iOS client; on web/Android keep the existing web client.
    const clientId = isNativeIos ? GOOGLE_IOS_CLIENT_ID : window.googleClientId;
    if (!clientId) {
        console.error('Google Client ID not configured.');
        return;
    }

    try {
        _redirectInProgress = true;

        // Prepare PKCE and state
        const codeVerifier = generateCodeVerifier(64);
        const codeChallenge = await pkceChallengeFromVerifier(codeVerifier);
        const state = generateState();

        // Persist for callback validation and backend token exchange.
        // Use localStorage instead of sessionStorage because iOS PWA WebViews
        // clear sessionStorage during cross-origin redirects (Google OAuth).
        try {
            localStorage.setItem('google_oauth_state', state);
            localStorage.setItem('google_oauth_code_verifier', codeVerifier);
            localStorage.setItem('google_oauth_code_challenge', codeChallenge);
            localStorage.setItem('google_oauth_returnUrl', location.href);
            localStorage.setItem('google_oauth_state_ts', Date.now().toString());
        } catch { /* best effort */ }

        // --- Native iOS: run OAuth in ASWebAuthenticationSession, then feed the returned
        // code+state into the existing callback route so GoogleCallback.razor handles it. ---
        if (isNativeIos) {
            // Persist the iOS redirect_uri so the callback page can pass it to the server-side
            // token exchange (the server branches to the iOS client for this redirect_uri).
            try { localStorage.setItem('google_oauth_redirect_uri', GOOGLE_IOS_REDIRECT_URI); } catch { }

            const authUrl = buildAuthUrl(clientId, { state, codeChallenge, redirectUri: GOOGLE_IOS_REDIRECT_URI });
            try {
                const result = await window.companioNation_startGoogleOAuth(authUrl, GOOGLE_IOS_REDIRECT_SCHEME);
                _redirectInProgress = false;
                const params = new URLSearchParams({ code: result.code || '', state: result.state || '' });
                // Navigate to the callback route (client-side) so the existing Blazor page runs.
                location.href = `${location.origin}/auth/google/callback?${params.toString()}`;
            } catch (e) {
                _redirectInProgress = false;
                console.error('Native iOS Google OAuth failed:', e);
                // Surface a Google-style error to the callback page for logging/UI.
                const params = new URLSearchParams({ error: 'native_oauth_failed', error_description: (e && e.message) ? e.message : 'unknown' });
                location.href = `${location.origin}/auth/google/callback?${params.toString()}`;
            }
            return;
        }

        // --- Web / Android: full-page redirect using the web client + https callback. ---
        const redirectUri = `${location.origin}/auth/google/callback`;
        const authUrl = buildAuthUrl(clientId, { state, codeChallenge, redirectUri });
        location.href = authUrl;
    } catch (e) {
        console.error('OAuth redirect error:', e);
        _redirectInProgress = false;
    }
};

// Optional helpers: retrieve PKCE values and parsed auth result (useful on callback page)
window.googleGetPendingPkce = function () {
    return {
        state: localStorage.getItem('google_oauth_state'),
        codeVerifier: localStorage.getItem('google_oauth_code_verifier'),
        codeChallenge: localStorage.getItem('google_oauth_code_challenge'),
        returnUrl: localStorage.getItem('google_oauth_returnUrl')
    };
};

window.googleParseAuthCallback = function () {
    const url = new URL(window.location.href);
    return {
        code: url.searchParams.get('code'),
        state: url.searchParams.get('state'),
        error: url.searchParams.get('error'),
        errorDescription: url.searchParams.get('error_description')
    };
};

// --- Apple Sign In ---
// Apple uses response_mode=form_post (no PKCE needed).
// The server endpoint at /auth/apple/callback receives the POST and redirects to Blazor.

const APPLE_AUTH_ENDPOINT = 'https://appleid.apple.com/auth/authorize';

window.appleLogin = async function () {
    if (_redirectInProgress) return;

    const serviceId = window.appleServiceId;
    if (!serviceId) {
        console.error('Apple Service ID not configured.');
        return;
    }

    try {
        _redirectInProgress = true;

        const state = generateState();

        // Persist for callback validation. Use localStorage (not sessionStorage):
        // iOS PWA WebViews clear sessionStorage during cross-origin OAuth redirects,
        // which would wipe the Apple state and cause a false "Invalid login state".
        try {
            localStorage.setItem('apple_oauth_state', state);
            localStorage.setItem('apple_oauth_state_ts', Date.now().toString());
        } catch { /* best effort */ }

        const redirectUri = `${location.origin}/auth/apple/callback`;
        const params = new URLSearchParams({
            client_id: serviceId,
            redirect_uri: redirectUri,
            response_type: 'code',
            scope: 'name email',
            response_mode: 'form_post',
            state: state
        });
        location.href = `${APPLE_AUTH_ENDPOINT}?${params.toString()}`;
    } catch (e) {
        console.error('Apple OAuth redirect error:', e);
        _redirectInProgress = false;
    }
};
