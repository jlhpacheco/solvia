(function () {
    'use strict';
    const form = document.querySelector('form[data-permit-draft]');
    if (!form) return;
    const flow = form.dataset.flowId;
    const owner = form.dataset.draftOwner;
    const kind = form.dataset.permitDraft;
    if (!flow || !owner) return;
    const prefix = 'solvia.permit.v1:';
    const key = prefix + owner + ':' + flow;
    const lifetime = 2 * 60 * 60 * 1000;
    const vehicleNames = { Plate: 20, CarMake: 50, CarModel: 50, CarColor: 30, CarState: 10 };
    function field(name) { return form.elements.namedItem(name); }
    function value(name) { return field(name)?.value ?? ''; }
    function safe(text, limit) { return typeof text === 'string' && text.length <= limit; }
    function read() {
        try {
            const saved = JSON.parse(sessionStorage.getItem(key) || 'null');
            if (saved?.owner === owner && saved.flow === flow && saved.expires > Date.now()) return saved;
            sessionStorage.removeItem(key);
        } catch (_) { /* Storage may be blocked. Protected server drafts still work. */ }
        const saved = history.state?.solviaPermit;
        return saved?.owner === owner && saved.flow === flow && saved.expires > Date.now() ? saved : null;
    }
    function write(saved) {
        saved.owner = owner;
        saved.flow = flow;
        saved.expires = Date.now() + lifetime;
        try {
            // Only short editable facts: never CSRF, source, application/order,
            // capture, receipt or authorization values. This ends with the tab.
            for (const name of Object.keys(sessionStorage)) {
                if (!name.startsWith(prefix)) continue;
                try {
                    const other = JSON.parse(sessionStorage.getItem(name) || 'null');
                    if (!other || other.expires <= Date.now()) sessionStorage.removeItem(name);
                } catch (_) { sessionStorage.removeItem(name); }
            }
            sessionStorage.setItem(key, JSON.stringify(saved));
            const keys = Object.keys(sessionStorage).filter(name => name.startsWith(prefix) && name !== key);
            while (keys.length > 3) sessionStorage.removeItem(keys.shift());
        } catch (_) { /* Private browsing/full storage: server flow is the fallback. */ }
        try { history.replaceState({ ...history.state, solviaPermit: saved }, ''); } catch (_) { }
    }
    function save() {
        const saved = read() || {};
        if (kind === 'questions') {
            const sentence = value('carText');
            if (!safe(sentence, 1000)) return;
            saved.questions = { sentence, permitType: value('permitType'), term: value('term') };
        } else {
            const sentence = value('CarText');
            if (!safe(sentence, 1000)) return;
            const vehicle = { forText: sentence };
            for (const [name, limit] of Object.entries(vehicleNames)) {
                const text = value(name);
                if (!safe(text, limit)) return;
                vehicle[name] = text;
            }
            saved.vehicle = vehicle;
        }
        write(saved);
    }
    function setValue(name, text) {
        const input = field(name);
        if (!input || typeof text !== 'string') return;
        if (input instanceof RadioNodeList) input.value = text;
        else input.value = text;
    }
    function restore() {
        const saved = read();
        if (!saved) return;
        const navigation = performance.getEntriesByType('navigation')[0];
        const isBack = navigation?.type === 'back_forward' || history.state?.solviaPermit?.flow === flow;
        if (kind === 'questions' && isBack && saved.questions && safe(saved.questions.sentence, 1000)) {
            setValue('carText', saved.questions.sentence);
            if (['Term', 'Daily'].includes(saved.questions.permitType)) setValue('permitType', saved.questions.permitType);
            if (['Fall', 'Spring', 'Summer'].includes(saved.questions.term)) setValue('term', saved.questions.term);
        }
        if (kind === 'understood' && saved.vehicle?.forText === value('CarText')) {
            for (const [name, limit] of Object.entries(vehicleNames)) {
                if (safe(saved.vehicle[name], limit)) setValue(name, saved.vehicle[name]);
            }
        }
        // Sync existing progressive enhancements after restoring editable values.
        form.dispatchEvent(new Event('change', { bubbles: true }));
        field('Plate')?.dispatchEvent(new Event('input', { bubbles: true }));
    }
    form.addEventListener('input', save);
    form.addEventListener('change', save);
    form.addEventListener('submit', function () {
        save();
        if (kind !== 'questions') return;
        // bfcache may retain the old generated nodes through repeated Back.
        // Rebuild once, including when the sentence now requires fresh reading.
        form.querySelectorAll('input[data-permit-revision]').forEach(input => input.remove());
        const vehicle = read()?.vehicle;
        if (!vehicle || vehicle.forText !== value('carText')) return;
        for (const [name, limit] of Object.entries({ CarText: 1000, ...vehicleNames })) {
            const text = name === 'CarText' ? vehicle.forText : vehicle[name];
            if (!safe(text, limit)) continue;
            const input = document.createElement('input');
            input.type = 'hidden'; input.name = 'revision.' + name; input.value = text;
            input.dataset.permitRevision = 'true';
            form.appendChild(input);
        }
    });
    restore();
    window.addEventListener('pageshow', restore);
})();
