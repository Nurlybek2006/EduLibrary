(() => {
    'use strict';
    document.querySelectorAll('[data-report-filter]').forEach(form => {
        const range = form.querySelector('[data-period-range]');
        const update = () => form.querySelectorAll('[data-period-custom]').forEach(group => {
            const custom = range.value === 'custom';
            group.hidden = !custom;
            group.querySelectorAll('input').forEach(input => { input.disabled = !custom; input.required = custom; });
        });
        range?.addEventListener('change', update);
        if (range) update();
    });
    document.querySelector('[data-print-report]')?.addEventListener('click', () => window.print());
})();
