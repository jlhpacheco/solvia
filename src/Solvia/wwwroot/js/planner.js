// Progressive focus after a requested comparison; the form works without JS.
(() => {
    const form = document.getElementById('planner-form');
    if (form) {
        const names = ['Campus', 'ScheduleText', 'SingleVisit', 'StartDate', 'EndDate', 'Weeks', 'Weekdays', 'Term'];
        const controls = Array.from(form.elements).filter(input => names.includes(input.name));
        // Date-only arithmetic uses UTC calendar components to avoid DST or
        // local timezone shifts. The server independently validates the range.
        const calendarDate = value => {
            if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return null;
            const [year, month, day] = value.split('-').map(Number);
            if (year < 1000) return null;
            const date = new Date(Date.UTC(year, month - 1, day));
            return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 &&
                date.getUTCDate() === day ? date : null;
        };
        const syncDuration = changed => {
            if (form.querySelector('input[name="SingleVisit"]:checked')?.value === 'true') return;
            const start = calendarDate(document.getElementById('StartDate').value);
            const endInput = document.getElementById('EndDate');
            const weeksInput = document.getElementById('Weeks');
            if (!start || !endInput) return;
            if (changed === 'Weeks') {
                const weeks = Number(weeksInput.value);
                if (!Number.isInteger(weeks) || weeks < 1 || weeks > 26) return;
                const end = new Date(start.getTime() + (weeks * 7 - 1) * 86400000);
                if (end.getUTCFullYear() <= 9999) endInput.value = end.toISOString().slice(0, 10);
            } else if (changed === 'StartDate' || changed === 'EndDate') {
                const end = calendarDate(endInput.value);
                if (!end || end < start) return;
                weeksInput.value = String(Math.ceil(((end - start) / 86400000 + 1) / 7));
            }
        };
        const applyVisitType = () => {
            const single = form.querySelector('input[name="SingleVisit"]:checked')?.value === 'true';
            for (const id of ['repeat-end', 'repeat-weeks', 'repeat-weekdays']) {
                const group = document.getElementById(id);
                if (!group) continue;
                group.hidden = single;
                group.disabled = single;
            }
            document.getElementById('planning-fields').dataset.singleVisit = String(single);
            document.getElementById('planning-date-label').textContent = single ? 'Visit date' : 'Start of your planning period';
            document.getElementById('single-visit-summary').hidden = !single;
            const dateInput = document.getElementById('StartDate');
            const endDate = calendarDate(document.getElementById('EndDate')?.value ?? '');
            const startDate = calendarDate(dateInput.value);
            const duration = document.getElementById('schedule-duration-summary');
            if (duration) duration.hidden = single || !startDate || !endDate || endDate < startDate;
            if (duration && !duration.hidden) {
                const days = (endDate - startDate) / 86400000 + 1;
                duration.textContent = `${days} calendar ${days === 1 ? 'day' : 'days'} across ${Math.ceil(days / 7)} ${days <= 7 ? 'week' : 'weeks'}. Only your selected weekdays count as visits.`;
            }
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
        const snapshot = (defaults = false) => controls.map(input => ({
            name: input.name,
            value: defaults
                ? input instanceof HTMLSelectElement
                    ? Array.from(input.options).find(option => option.defaultSelected)?.value ?? input.options[0]?.value ?? ''
                    : input.defaultValue
                : input.value,
            checked: input.type === 'checkbox' || input.type === 'radio'
                ? defaults ? input.defaultChecked : input.checked
                : null
        }));
        // WebKit can restore edited control properties before this script runs.
        // HTML defaults still describe the server's comparison, so a restored
        // edit cannot accidentally become that comparison's new baseline.
        const rendered = JSON.stringify(snapshot(true));
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
            if (draft && draft.owner === form.dataset.person && Array.isArray(draft.fields)) {
                for (const input of controls) {
                    const field = draft.fields.find(item => item.name === input.name &&
                        (input.type !== 'checkbox' && input.type !== 'radio' || item.value === input.value));
                    if (!field) continue;
                    if (field.checked === null) input.value = field.value;
                    else input.checked = field.checked;
                }
            }
            applyVisitType();
            if (JSON.stringify(snapshot()) !== rendered) invalidate();
        };
        applyVisitType();
        restore();
        window.addEventListener('pageshow', restore);
        form.addEventListener('input', event => { syncDuration(event.target.name); applyVisitType(); invalidate(); save(); });
        form.addEventListener('change', event => { syncDuration(event.target.name); applyVisitType(); invalidate(); save(); });
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
