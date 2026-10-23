// Progressive focus after a requested comparison; the form works without JS.
(() => {
    const form = document.getElementById('planner-form');
    if (form) {
        const names = ['Campus', 'ScheduleText', 'SingleVisit', 'StartDate', 'Weeks', 'Weekdays', 'Term'];
        const controls = Array.from(form.elements).filter(input => names.includes(input.name));
        const applyVisitType = () => {
            const single = form.querySelector('input[name="SingleVisit"]:checked')?.value === 'true';
            for (const id of ['repeat-weeks', 'repeat-weekdays']) {
                const group = document.getElementById(id);
                group.hidden = single;
                group.disabled = single;
            }
            document.getElementById('Weeks').required = !single;
            document.getElementById('planning-fields').dataset.singleVisit = String(single);
            document.getElementById('planning-date-label').textContent = single ? 'Visit date' : 'Start of your planning period';
            document.getElementById('single-visit-summary').hidden = !single;
            const dateInput = document.getElementById('StartDate');
            const date = dateInput.value ? new Date(dateInput.value + 'T12:00:00') : null;
            const summary = document.getElementById('single-visit-date');
            if (date && Number.isFinite(date.getTime())) {
                summary.dateTime = dateInput.value;
                summary.textContent = new Intl.DateTimeFormat('en-US', { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' }).format(date);
            } else {
                summary.removeAttribute('datetime');
                summary.textContent = 'the date you choose';
            }
        };
        document.getElementById('update-visit-type').hidden = true;
        document.getElementById('visit-type-help').hidden = true;
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
            applyVisitType();
            if (JSON.stringify(snapshot()) !== rendered) invalidate();
        };
        applyVisitType();
        restore();
        window.addEventListener('pageshow', restore);
        form.addEventListener('input', () => { applyVisitType(); invalidate(); save(); });
        form.addEventListener('change', () => { applyVisitType(); invalidate(); save(); });
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
