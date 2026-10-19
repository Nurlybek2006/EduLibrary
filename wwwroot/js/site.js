(() => {
    const icons = () => window.lucide?.createIcons();
    icons();
    if (window.bootstrap?.Tooltip) document.querySelectorAll('.icon-button[aria-label]:not([data-bs-toggle]),.icon-link[aria-label],.rail-link[aria-label]').forEach(element => {
        bootstrap.Tooltip.getOrCreateInstance(element, { title: element.getAttribute('aria-label'), trigger: 'hover focus', container: 'body', placement: element.classList.contains('rail-link') ? 'right' : 'bottom' });
    });
    const themeButton = document.querySelector('#theme-toggle');
    function describeTheme() { themeButton?.setAttribute('aria-label', document.documentElement.dataset.theme === 'dark' ? 'Ашық режимге ауысу' : 'Қараңғы режимге ауысу'); }
    describeTheme();
    themeButton?.addEventListener('click', () => {
        const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
        document.documentElement.dataset.theme = theme;
        try { localStorage.setItem('edulibrary-theme', theme); } catch { }
        describeTheme();
        const tooltip = window.bootstrap?.Tooltip.getInstance(themeButton);
        tooltip?.setContent({ '.tooltip-inner': themeButton.getAttribute('aria-label') });
    });
    const menuButton = document.querySelector('#menu-toggle');
    const navigation = document.getElementById(menuButton?.getAttribute('aria-controls'));
    const closeMenu = (restoreFocus = false) => {
        document.body.classList.remove('navigation-open');
        menuButton?.setAttribute('aria-expanded', 'false');
        if (restoreFocus) menuButton?.focus();
    };
    menuButton?.addEventListener('click', () => {
        const open = document.body.classList.toggle('navigation-open');
        menuButton.setAttribute('aria-expanded', String(open));
        if (open) navigation?.querySelector('a')?.focus();
    });
    document.querySelector('#navigation-backdrop')?.addEventListener('click', () => closeMenu(true));
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && document.body.classList.contains('navigation-open')) closeMenu(true);
        if (event.key === 'Tab' && document.body.classList.contains('navigation-open')) {
            const links = Array.from(navigation?.querySelectorAll('a,button') || []).filter(e => e.getClientRects().length);
            if (!links.length) return;
            if (event.shiftKey && document.activeElement === links[0]) { event.preventDefault(); menuButton.focus(); }
            else if (event.shiftKey && document.activeElement === menuButton) { event.preventDefault(); links.at(-1).focus(); }
            else if (!event.shiftKey && document.activeElement === links.at(-1)) { event.preventDefault(); menuButton.focus(); }
        }
    });
    window.addEventListener('resize', () => { if (innerWidth >= (document.body.classList.contains('staff-workspace') ? 768 : 1024)) closeMenu(); });
    document.querySelectorAll('[data-dismiss-notice]').forEach(button => button.addEventListener('click', () => button.closest('.toast-notice').remove()));
    document.querySelectorAll('[data-password-toggle]').forEach(button => button.addEventListener('click', () => {
        const input = document.getElementById(button.dataset.passwordToggle);
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(show));
        button.setAttribute('aria-label', show ? 'Құпиясөзді жасыру' : 'Құпиясөзді көрсету');
        button.innerHTML = '<i data-lucide="' + (show ? 'eye-off' : 'eye') + '"></i>';
        icons();
    }));
    const dialog = document.querySelector('#confirm-dialog');
    document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => {
        if (form.dataset.confirmed) return;
        event.preventDefault();
        document.querySelector('#confirm-message').textContent = form.dataset.confirm;
        dialog.returnValue = ''; dialog.showModal();
        dialog.addEventListener('close', () => { if (dialog.returnValue === 'confirm') { form.dataset.confirmed = 'true'; form.requestSubmit(); } }, { once: true });
    }));
    document.querySelectorAll('form[method="post"]').forEach(form => form.addEventListener('submit', event => {
        if (event.defaultPrevented || !form.checkValidity() || (window.jQuery?.validator && !jQuery(form).valid())) return;
        requestAnimationFrame(() => { if (event.defaultPrevented) return; form.querySelectorAll('button[type="submit"]').forEach(button => { button.disabled = true; button.classList.add('is-loading'); }); });
    }));
    // Cover art remains visible under a failed image, including AJAX catalog results.
    document.addEventListener('error', event => {
        const img = event.target;
        if (!(img instanceof HTMLImageElement)) return;
        if (img.hasAttribute('data-cover-image')) { img.hidden = true; return; }
        if (!img.dataset.fallbackAttempted) { img.dataset.fallbackAttempted = 'true'; img.src = '/images/book-placeholder.svg'; }
    }, true);
})();
