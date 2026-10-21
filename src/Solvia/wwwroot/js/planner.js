// Progressive focus after a requested comparison; the form works without JS.
(() => {
    const form = document.getElementById('planner-form');
    if (form) {
        const names = ['Campus', 'ScheduleText', 'StartDate', 'Weeks', 'Weekdays', 'Term'];
        const controls = Array.from(form.elements).filter(input => names.includes(input.name));
        const snapshot = () => controls.map(input => ({ name: input.name, value: input.value,
            checked: input.type === 'checkbox' || input.type === 'radio' ? input.checked : null }));
        const rendered = JSON.stringify(snapshot());
        const invalidate = () => {
            const result = document.querySelector('[data-testid="plan-result"]');
            if (result) {
                result.hidden = true;
                document.getElementById('plan-edited').hidden = false;
            }
        };
        // Each history entry keeps its own form values, including edits not
        // yet submitted. No localStorage, cross-tab storage or action replay.
        const save = () => {
            try {
                history.replaceState({ ...history.state, solviaPlannerDraft: {
                    owner: form.dataset.person, fields: snapshot()
                } }, '');
            } catch { /* Browser history can be unavailable; server drafts still work. */ }
        };
        const restore = () => {
            const draft = history.state?.solviaPlannerDraft;
            if (!draft || draft.owner !== form.dataset.person || !Array.isArray(draft.fields)) return;
            for (const input of controls) {
                const field = draft.fields.find(item => item.name === input.name &&
                    (input.type !== 'checkbox' && input.type !== 'radio' || item.value === input.value));
                if (!field) continue;
                if (field.checked === null) input.value = field.value;
                else input.checked = field.checked;
            }
            if (JSON.stringify(snapshot()) !== rendered) invalidate();
        };
        restore();
        window.addEventListener('pageshow', restore);
        form.addEventListener('input', () => { invalidate(); save(); });
        form.addEventListener('change', () => { invalidate(); save(); });
        form.addEventListener('submit', save);
        window.addEventListener('pagehide', save);
    }
    const comparison = document.querySelector('[data-testid="plan-result"]');
    const target = document.getElementById('planner-errors') ||
        (comparison && !comparison.hidden ? document.getElementById('comparison-title') : null) ||
        (document.querySelector('[data-source="manual"]') ? document.getElementById('confirm-schedule') : null);
    if (!target) return;
    target.focus({ preventScroll: true });
    target.scrollIntoView({ block: 'start', behavior: 'instant' });
})();
