// chat-composer.js
// Google Messages-style message composer: the textarea word-wraps and grows
// vertically as the user types, capped by the CSS max-height (beyond which it
// scrolls internally). Invoked from Home.razor via JS interop on every input.
window.cnAutoResizeTextArea = function (id) {
    const el = document.getElementById(id);
    if (!el) return;

    // Reset to 'auto' first so scrollHeight reports the height required by the
    // current content; CSS max-height caps the final rendered size.
    el.style.height = 'auto';
    el.style.height = el.scrollHeight + 'px';
};


