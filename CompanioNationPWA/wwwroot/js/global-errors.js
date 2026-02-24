let blazorObjectReference = null;
let handlersAttached = false;
let previousOnError = null;
let appVersion = null;

const errorCache = new Map();
const dedupeWindowMs = 30000;

function getCorrelationId() {
    try {
        const existing = sessionStorage.getItem('client_error_correlation_id');
        if (existing) return existing;
        const id = crypto.randomUUID();
        sessionStorage.setItem('client_error_correlation_id', id);
        return id;
    } catch {
        return null;
    }
}

function shouldReport(signature) {
    const now = Date.now();
    const last = errorCache.get(signature);
    if (last && now - last < dedupeWindowMs) {
        return false;
    }
    errorCache.set(signature, now);
    return true;
}

function getSource(filename) {
    if (!filename) return 'unknown';
    if (filename.includes('connect.facebook.net')) return 'third-party';
    try {
        const url = new URL(filename, window.location.href);
        return url.host && url.host !== window.location.host ? 'third-party' : 'first-party';
    } catch {
        return 'unknown';
    }
}

function safeInvoke(report) {
    if (!blazorObjectReference) {
        return;
    }

    try {
        blazorObjectReference
            .invokeMethodAsync('HandleJavaScriptError', report)
            .catch(err => console.error('Error invoking HandleJavaScriptError:', err));
    } catch (err) {
        console.error('HandleJavaScriptError invocation failed:', err);
    }
}

function buildBaseReport(eventType, event) {
    return {
        correlationId: getCorrelationId(),
        route: `${location.pathname}${location.search}${location.hash}`,
        appVersion: appVersion,
        eventType: eventType,
        isTrusted: typeof event?.isTrusted === 'boolean' ? event.isTrusted : null,
        userAgent: navigator.userAgent,
        url: location.href,
        referrer: document.referrer || null
    };
}

function handleError(event) {
    try {
        const target = event?.target;
        const filename = event?.filename || target?.src || target?.href || null;
        const lineNumber = Number.isFinite(event?.lineno) ? event.lineno : null;
        const columnNumber = Number.isFinite(event?.colno) ? event.colno : null;
        const message = event?.message || (target ? 'Resource error' : 'Unknown error');
        const stack = event?.error?.stack || null;
        const source = getSource(filename);
        const isResourceError = !event?.message && target && target.tagName && ['IMG', 'SCRIPT', 'LINK', 'VIDEO', 'AUDIO', 'SOURCE'].includes(target.tagName);

        // Always suppress resource load errors and third-party errors from Blazor
        if (isResourceError || source === 'third-party') {
            event.preventDefault();
            event.stopImmediatePropagation();

            // Swap broken images with a placeholder to avoid the ugly broken-image icon.
            // Guard with a data attribute to prevent infinite retry loops.
            if (target && target.tagName === 'IMG' && !target.dataset.fallback) {
                target.dataset.fallback = 'true';
                target.src = '/images/generic-profile.jpg';
            }
        }

        const signature = `${message}|${filename || ''}|${lineNumber || ''}|${columnNumber || ''}`;

        if (!shouldReport(signature)) {
            return;
        }

        const report = {
            ...buildBaseReport(isResourceError ? 'resource-error' : 'error', event),
            message: message,
            filename: filename,
            lineNumber: lineNumber,
            columnNumber: columnNumber,
            stack: stack,
            source: source,
            tagName: target?.tagName || null
        };

        // For resource errors, log to console only — don't call into Blazor interop
        // as that can cause cascading failures
        if (isResourceError) {
            if (source === 'first-party') {
                // First-party resource failures (e.g., missing image on Azure) should be
                // reported so they surface in server-side error logging.
                console.warn('Resource load error (first-party):', filename);
                safeInvoke(report);
            } else {
                console.warn('Resource load error (suppressed):', filename);
            }
            return;
        }

        // Don't report third-party JS errors to the server (avoids error emails)
        if (source === 'third-party') {
            console.warn('Third-party error (suppressed):', message, filename);
            return;
        }

        safeInvoke(report);
    } catch (err) {
        console.error('Global error handler failed:', err);
    }
}

function handleUnhandledRejection(event) {
    try {
        const reason = event?.reason;
        const message = reason?.message || reason?.toString?.() || 'Unhandled promise rejection';
        const stack = reason?.stack || null;
        const filename = reason?.fileName || null;
        const source = getSource(filename);
        const signature = `${message}|${filename || ''}|||unhandledrejection`;

        // Suppress third-party rejections entirely — don't report to server (avoids error emails)
        if (source === 'third-party') {
            event.preventDefault();
            if (shouldReport(signature)) {
                console.warn('Third-party rejection (suppressed):', message, filename);
            }
            return;
        }

        if (!shouldReport(signature)) {
            return;
        }

        const report = {
            ...buildBaseReport('unhandledrejection', event),
            message: message,
            filename: filename,
            stack: stack,
            source: source
        };

        safeInvoke(report);
    } catch (err) {
        console.error('Unhandled rejection handler failed:', err);
    }
}

function handleWindowOnError(message, source, lineno, colno, error) {
    try {
        const filename = source || error?.fileName || null;
        const lineNumber = Number.isFinite(lineno) ? lineno : null;
        const columnNumber = Number.isFinite(colno) ? colno : null;
        const text = message?.toString?.() || 'Unknown error';
        const stack = error?.stack || null;
        const errorSource = getSource(filename);
        const signature = `${text}|${filename || ''}|${lineNumber || ''}|${columnNumber || ''}|onerror`;

        // Suppress third-party errors entirely — don't report to server (avoids error emails)
        if (errorSource === 'third-party') {
            if (shouldReport(signature)) {
                console.warn('Third-party error (suppressed):', text, filename);
            }
            return true;
        }

        if (shouldReport(signature)) {
            const report = {
                ...buildBaseReport('window.onerror', null),
                message: text,
                filename: filename,
                lineNumber: lineNumber,
                columnNumber: columnNumber,
                stack: stack,
                source: errorSource
            };

            safeInvoke(report);
        }
    } catch (err) {
        console.error('window.onerror handler failed:', err);
    }

    try {
        if (typeof previousOnError === 'function') {
            return previousOnError(message, source, lineno, colno, error);
        }
    } catch (err) {
        console.error('Previous window.onerror handler failed:', err);
    }

    return true;
}

export function registerGlobalErrorHandler(dotNetObject, options) {
    blazorObjectReference = dotNetObject;
    appVersion = options?.appVersion ?? appVersion;

    if (typeof options?.facebookSdkEnabled === 'boolean') {
        setFacebookSdkEnabled(options.facebookSdkEnabled);
    }

    if (handlersAttached) {
        return;
    }

    window.addEventListener('error', handleError, true);
    window.addEventListener('unhandledrejection', handleUnhandledRejection);

    previousOnError = window.onerror;
    window.onerror = handleWindowOnError;

    handlersAttached = true;
}

export function clearGlobalErrorHandler() {
    blazorObjectReference = null;

    if (!handlersAttached) {
        return;
    }

    window.removeEventListener('error', handleError, true);
    window.removeEventListener('unhandledrejection', handleUnhandledRejection);

    if (window.onerror === handleWindowOnError) {
        window.onerror = previousOnError;
    }

    previousOnError = null;
    handlersAttached = false;
}

export function setFacebookSdkEnabled(enabled) {
    try {
        window.companioNationThirdParty = window.companioNationThirdParty || {};
        window.companioNationThirdParty.facebookSdkEnabled = enabled === true;
        sessionStorage.setItem('client_facebook_sdk_enabled', enabled === true ? 'true' : 'false');
    } catch (err) {
        console.error('Failed to set Facebook SDK flag:', err);
    }
}
