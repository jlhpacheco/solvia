// Progressive focus after a requested comparison; the form works without JS.
(() => {
    const target = document.getElementById('planner-errors') || document.getElementById('comparison-title');
    if (!target) return;
    target.focus({ preventScroll: true });
    target.scrollIntoView({ block: 'start', behavior: 'instant' });
})();
