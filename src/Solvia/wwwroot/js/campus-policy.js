// Enhance the server-rendered school note without submitting or changing answers.
// On the planner, load after planner.js so its history restoration runs first.
(() => {
    for (const policy of document.querySelectorAll('[data-campus-policy]')) {
        const college = document.getElementById(policy.dataset.campusSelect);
        const message = policy.querySelector('[data-campus-policy-message]');
        if (!college || !message || !college.form) continue;
        const sync = () => { message.hidden = college.value !== policy.dataset.policyCampus; };
        sync();
        college.addEventListener('input', sync);
        college.addEventListener('change', sync);
        window.addEventListener('pageshow', sync);
        college.form.addEventListener('reset', () => queueMicrotask(sync));
        // Leave the native refresh path available if enhancement cannot initialize.
        for (const fallback of college.form.querySelectorAll('[data-update-campus], [data-campus-update-help]')) {
            fallback.hidden = true;
            // A hidden submit button must not intercept Enter on the main form.
            if (fallback.matches('[data-update-campus]')) fallback.type = 'button';
        }
    }
})();
