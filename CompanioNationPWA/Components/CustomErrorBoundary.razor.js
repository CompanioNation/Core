let blazorObjectReference = null;
let listenersAttached = false;

const handleError = (event) => {
    console.error('Global error caught: ', event.error);
    const errorMessage = formatErrorMessage(event);
    queueMicrotask(() => invokeIfAvailable(errorMessage));
};

const handleUnhandledRejection = (event) => {
    console.error('Unhandled promise rejection: ', event.reason);
    const reason = event.reason ? event.reason.toString() : 'Unknown reason';
    const errorMessage = formatRejectionMessage(event, reason);
    queueMicrotask(() => invokeIfAvailable(errorMessage));
};

export function setObjectReference(dotNetObject) {
    blazorObjectReference = dotNetObject;
    attachListeners();
}

export function clearObjectReference() {
    blazorObjectReference = null;

    if (listenersAttached) {
        window.removeEventListener('error', handleError);
        window.removeEventListener('unhandledrejection', handleUnhandledRejection);
        listenersAttached = false;
    }
}

function attachListeners() {
    if (listenersAttached) {
        return;
    }

    window.addEventListener('error', handleError);
    window.addEventListener('unhandledrejection', handleUnhandledRejection);
    listenersAttached = true;
}

function invokeIfAvailable(errorMessage) {
    if (!blazorObjectReference) {
        return;
    }

    blazorObjectReference.invokeMethodAsync('HandleJavaScriptError', errorMessage)
        .catch(err => console.error('Error invoking HandleJavaScriptError:', err));
}

function formatErrorMessage(event) {
    const message = event.message || 'Unknown error message';
    const filename = event.filename || 'Unknown file';
    const line = event.lineno || 'Unknown line';
    const column = event.colno || 'Unknown column';
    const stack = event.error?.stack || 'No stack trace available';

    return `Error: ${message}\nFile: ${filename}\nLine: ${line}, Column: ${column}\nStack Trace: ${stack}`;
}

function formatRejectionMessage(event, reason) {
    const stack = event.reason?.stack || 'No stack trace available';
    return `Unhandled Promise Rejection: ${reason}\nStack Trace: ${stack}`;
}

