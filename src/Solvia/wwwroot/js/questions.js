(function () {
    'use strict';
    const form = document.getElementById('vehicle-questions');
    const button = document.getElementById('read-answers');
    const status = document.getElementById('ai-reading-status');
    const termOptions = document.getElementById('term-options');
    if (!form || !button || !status) return;

    function syncTermOptions() {
        if (!termOptions) return;
        // Hidden choices retain their selection and still post with the form.
        termOptions.hidden = form.querySelector('input[name="permitType"]:checked')?.value !== 'Term';
    }

    function reset() {
        button.disabled = false;
        button.textContent = 'Read my answers';
        status.hidden = true;
        form.removeAttribute('aria-busy');
        syncTermOptions();
    }

    form.querySelectorAll('input[name="permitType"]').forEach(function (input) {
        input.addEventListener('change', syncTermOptions);
    });
    syncTermOptions();

    form.addEventListener('submit', function (event) {
        if (event.submitter && event.submitter.id !== 'read-answers') return;
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
