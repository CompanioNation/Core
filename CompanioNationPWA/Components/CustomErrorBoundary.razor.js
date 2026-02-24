import { registerGlobalErrorHandler, clearGlobalErrorHandler } from "/js/global-errors.js";

let blazorObjectReference = null;
let listenersAttached = false;
let lastOptions = null;

export function setObjectReference(dotNetObject, options) {
    blazorObjectReference = dotNetObject;
    lastOptions = options || null;

    if (!blazorObjectReference) {
        return;
    }

    try {
        registerGlobalErrorHandler(blazorObjectReference, lastOptions);
        listenersAttached = true;
    } catch (err) {
        console.error('Failed to register global error handler:', err);
    }
}

export function clearObjectReference() {
    blazorObjectReference = null;

    if (listenersAttached) {
        try {
            clearGlobalErrorHandler();
        } catch (err) {
            console.error('Failed to clear global error handler:', err);
        }
        listenersAttached = false;
    }
}

