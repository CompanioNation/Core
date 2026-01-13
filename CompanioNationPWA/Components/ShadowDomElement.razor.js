function loadHtmlIntoShadowDOM (elementRef, htmlContent) {
    const element = elementRef; // Direct reference to ensure DOM access
    if (!element || !element.attachShadow) {
        console.warn("Element not available.");
        return;
    }

    // Check and attach Shadow DOM if not present
    if (!element.shadowRoot) {
        element.attachShadow({ mode: "open" });
    }

    // Inject HTML content
    element.shadowRoot.innerHTML = htmlContent;
};

window.loadHtmlIntoShadowDOM = loadHtmlIntoShadowDOM;