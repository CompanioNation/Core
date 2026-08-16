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

    // The caller passes pre-isolated markup (curated .cn-advice <style> + extracted
    // body). Injecting it inside the shadow root guarantees the AI-authored content
    // can never restyle the host page, while the curated editorial design still
    // applies via the scoped stylesheet.
    element.shadowRoot.innerHTML = htmlContent;
};

window.loadHtmlIntoShadowDOM = loadHtmlIntoShadowDOM;