(function () {
    'use strict';
    const status = document.getElementById('ai-operation-status');
    if (!status) return;
    const label = status.querySelector('[data-ai-label]');
    const description = status.querySelector('[data-ai-description]');
    const model = status.querySelector('[data-ai-model]');
    const announcement = document.getElementById('ai-status-announcement');
    const refreshButton = status.querySelector('[data-ai-refresh]');
    const allowed = new Set(['unconfigured', 'reading', 'fallback', 'interrupted', 'unchecked', 'stale', 'recent']);
    let pending = false, timer, lastRequest = 0;
    function time(selector, value) {
        const element = status.querySelector(selector);
        const date = typeof value === 'string' ? new Date(value) : null;
        if (date && Number.isFinite(date.getTime())) {
            element.dateTime = date.toISOString();
            element.textContent = date.toISOString().replace('T', ' ').replace(/\.\d{3}Z$/, ' UTC');
        } else { element.removeAttribute('datetime'); element.textContent = 'None observed'; }
    }
    function state(value, text) {
        const changed = status.dataset.state !== value;
        status.dataset.state = value;
        label.textContent = text;
        // One polite announcement only when the meaningful state changes.
        if (changed && announcement) announcement.textContent = text;
    }
    function schedule() { window.clearTimeout(timer); if (!document.hidden) timer = window.setTimeout(refresh, 60_000); }
    async function refresh(force = false) {
        if (pending || document.hidden) return;
        // A fast page restore can fall within the request throttle after
        // pagehide cleared its timer. Keep the next read-only refresh alive.
        if (!force && Date.now() - lastRequest < 1000) { schedule(); return; }
        pending = true; lastRequest = Date.now();
        const controller = new AbortController();
        const timeout = window.setTimeout(() => controller.abort(), 5000);
        try {
            const response = await fetch('/api/ai/status', { cache: 'no-store', credentials: 'same-origin',
                headers: { Accept: 'application/json' }, signal: controller.signal });
            if (!response.ok) throw new Error('Status unavailable');
            const data = await response.json();
            if (!allowed.has(data.state) || typeof data.label !== 'string' || typeof data.description !== 'string' ||
                typeof data.provider !== 'string' || typeof data.model !== 'string') throw new Error('Invalid status');
            state(data.state, data.label);
            description.textContent = data.description;
            model.textContent = data.provider + ' · ' + data.model;
            time('[data-ai-success]', data.lastSuccessAtUtc);
            time('[data-ai-attempt]', data.lastAttemptAtUtc);
        } catch {
            state('unavailable', 'AI status unavailable');
            description.textContent = 'This page could not refresh its status. Earlier observed timestamps remain below; current availability is not confirmed.';
        } finally { window.clearTimeout(timeout); pending = false; schedule(); }
    }
    refreshButton.hidden = false;
    refreshButton.addEventListener('click', () => refresh(true));
    document.addEventListener('visibilitychange', () => { if (document.hidden) window.clearTimeout(timer); else refresh(); });
    window.addEventListener('pageshow', () => refresh());
    window.addEventListener('pagehide', () => window.clearTimeout(timer));
    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        // Without the attribute, a button's formAction reflects this page's
        // URL in browsers, rather than the enclosing form's action.
        const action = new URL(event.submitter?.hasAttribute('formaction')
            ? event.submitter.formAction : form.action, window.location.href);
        if (action.origin === window.location.origin && /^\/(Permits\/Understand|Planner\/Read)\/?$/i.test(action.pathname)) {
            // The user's own read is starting; this does not claim model success.
            state('reading', 'Reading your draft…');
            description.textContent = 'Your draft read is starting. The next screen will identify the model or built-in rules that answered.';
        }
    });
    refresh();
}());
