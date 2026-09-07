// preboot-actions.js
//
// Captures clicks on SSR-rendered buttons before the Blazor WASM runtime is ready,
// so the user's intent is never lost during the cold-boot window. Buttons opt in by
// carrying a data-cn-preclick attribute (rendered by ActionButton via attribute
// splatting). A slow cold boot also reveals the button's localized "Loading…" state
// without a click, while a fast boot never flashes it.

(function () {
    'use strict';

    // False until Blazor signals readiness (MainLayout.setDotNetObjectReference).
    window.cnBlazorReady = false;

    // The pending pre-boot action name, flushed by MainLayout after boot.
    window._cnPendingAction = null;

    var SLOW_BOOT_DELAY_MS = 800;

    function isReady() {
        return window.cnBlazorReady === true;
    }

    function isBotRenderer() {
        return typeof window.cnIsBotRenderer === 'function' && window.cnIsBotRenderer();
    }

    // Reveal the SSR-rendered busy visual on a pre-boot button. The normal label is
    // hidden by CSS (button.loading .button-content) and the localized busy fragment
    // is shown by removing its hidden attribute. aria-label is updated to match so
    // screen readers announce "Loading…" rather than the stale normal label.
    function revealLoading(btn) {
        if (!btn) return;
        btn.classList.add('loading');
        btn.setAttribute('aria-busy', 'true');
        var busy = btn.querySelector('.cn-busy-content');
        if (busy) {
            busy.removeAttribute('hidden');
            var busyText = busy.textContent || '';
            if (busyText.trim()) btn.setAttribute('aria-label', busyText.trim());
        }
    }

    // Undo any JS-applied busy state once the WASM runtime has rendered. Blazor's
    // diffing may not rewrite attributes that its model still considers unchanged
    // (the SSR markup was already "not loading"), so this is the authoritative reset
    // that guarantees the button never stays stuck on "Loading…" after boot.
    function resetPrebootButtons() {
        document.querySelectorAll('[data-cn-preclick]').forEach(function (btn) {
            btn.classList.remove('loading');
            btn.setAttribute('aria-busy', 'false');
            var busy = btn.querySelector('.cn-busy-content');
            if (busy) busy.setAttribute('hidden', '');
        });
    }
    window.cnResetPrebootButtons = resetPrebootButtons;

    // Capture phase so it runs before Blazor's own handler (once attached) and before
    // any default browser action.
    document.addEventListener('click', function (e) {
        if (isReady()) return; // normal Blazor @onclick handles it now

        var target = e.target;
        if (!target || typeof target.closest !== 'function') return;

        var btn = target.closest('[data-cn-preclick]');
        if (!btn) return;

        var action = btn.getAttribute('data-cn-preclick');
        if (!action) return;

        e.preventDefault();
        e.stopPropagation();

        // Re-clicking while still booting is idempotent: keep the latest intent.
        window._cnPendingAction = action;
        revealLoading(btn);
    }, true);

    // Passive slow-boot feedback: reveal loading only if the runtime still isn't
    // ready after a short threshold, so fast boots never flash the spinner. Bot
    // renderers never start Blazor, so skip them to keep their snapshots clean.
    function scheduleSlowBootReveal() {
        if (isBotRenderer()) return;
        setTimeout(function () {
            if (isReady()) return;
            document.querySelectorAll('[data-cn-preclick]').forEach(revealLoading);
        }, SLOW_BOOT_DELAY_MS);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scheduleSlowBootReveal);
    } else {
        scheduleSlowBootReveal();
    }
})();
