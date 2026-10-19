(() => {
    if (window.lucide) lucide.createIcons();
    document.querySelector('#theme-toggle')?.addEventListener('click', () => {
        const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
        document.documentElement.dataset.theme = theme;
        try { localStorage.setItem('edulibrary-theme', theme); } catch { }
    });
    const closeMenu = () => { document.body.classList.remove('sidebar-open'); document.querySelector('#menu-toggle')?.setAttribute('aria-expanded', 'false'); };
    document.querySelector('#menu-toggle')?.addEventListener('click', () => { const open = document.body.classList.toggle('sidebar-open'); document.querySelector('#menu-toggle').setAttribute('aria-expanded', String(open)); });
    document.querySelector('#sidebar-backdrop')?.addEventListener('click', closeMenu);
    document.addEventListener('keydown', e => { if(e.key === 'Escape') closeMenu(); });
    document.querySelectorAll('[data-dismiss-notice]').forEach(button => button.addEventListener('click', () => button.closest('.toast-notice').remove()));
    const dialog = document.querySelector('#confirm-dialog');
    document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => {
        if(form.dataset.confirmed) return;
        event.preventDefault();
        document.querySelector('#confirm-message').textContent = form.dataset.confirm;
        dialog.returnValue = ''; dialog.showModal();
        dialog.addEventListener('close', () => { if(dialog.returnValue === 'confirm') { form.dataset.confirmed = 'true'; form.requestSubmit(); } }, {once:true});
    }));
    document.querySelectorAll('form[method="post"]').forEach(form => form.addEventListener('submit', event => {
        if(event.defaultPrevented || !form.checkValidity() || (window.jQuery?.validator && !jQuery(form).valid())) return;
        requestAnimationFrame(() => { if(event.defaultPrevented) return; form.querySelectorAll('button[type="submit"]').forEach(button => { button.disabled = true; button.classList.add('is-loading'); }); });
    }));
    document.querySelectorAll('img').forEach(img=>img.addEventListener('error',()=>{img.src='/images/book-placeholder.svg';},{once:true}));
})();
