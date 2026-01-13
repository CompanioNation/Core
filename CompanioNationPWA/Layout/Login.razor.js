// Google OAuth / Identity Services integration for Login.razor
// OAuth Authorization Code (PKCE) via manual authorization endpoint redirect (no GSI library).

let _redirectInProgress = false;
const AUTH_ENDPOINT = 'https://accounts.google.com/o/oauth2/v2/auth';

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
function buildAuthUrl(clientId, { state, codeChallenge, redirectUri }) {
    const params = new URLSearchParams({
        client_id: clientId,
        redirect_uri: redirectUri,
        response_type: 'code',
        scope: 'openid email profile',
        include_granted_scopes: 'true',
        access_type: 'offline',     // request refresh token if policy allows
        prompt: 'consent',          // ensure refresh token on subsequent logins if needed
        code_challenge: codeChallenge,
        code_challenge_method: 'S256',
        state: state
    });
    return `${AUTH_ENDPOINT}?${params.toString()}`;
}

// --- Main login function called by Blazor ---
window.googleLogin = async function () {
    if (_redirectInProgress) return;

    const clientId = window.googleClientId;
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

        // Persist for callback validation and backend token exchange
        try {
            sessionStorage.setItem('google_oauth_state', state);
            sessionStorage.setItem('google_oauth_code_verifier', codeVerifier);
            sessionStorage.setItem('google_oauth_code_challenge', codeChallenge);
            sessionStorage.setItem('google_oauth_returnUrl', location.href);
            sessionStorage.setItem('google_oauth_state_ts', Date.now().toString());
        } catch { /* best effort */ }

        // Construct the authorization URL and perform full-page redirect
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
        state: sessionStorage.getItem('google_oauth_state'),
        codeVerifier: sessionStorage.getItem('google_oauth_code_verifier'),
        codeChallenge: sessionStorage.getItem('google_oauth_code_challenge'),
        returnUrl: sessionStorage.getItem('google_oauth_returnUrl')
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
