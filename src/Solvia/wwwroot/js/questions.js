(function () {
    'use strict';
    const form = document.getElementById('vehicle-questions');
    const button = document.getElementById('read-answers');
    const status = document.getElementById('ai-reading-status');
    if (!form || !button || !status) return;

    function reset() {
        button.disabled = false;
        button.textContent = 'Read my answers';
        status.hidden = true;
        form.removeAttribute('aria-busy');
    }

    form.addEventListener('submit', function (event) {
        if (button.disabled) {
            event.preventDefault();
            return;
        }
        // Native constraint validation runs before submit. Leave the controls
        // enabled so their values are included in the normal protected POST.
        button.disabled = true;
        button.textContent = 'Reading your answers…';
        status.hidden = false;
        form.setAttribute('aria-busy', 'true');
    });

    // Restore an actionable form when Back returns a page from the cache.
    window.addEventListener('pageshow', reset);
})();
