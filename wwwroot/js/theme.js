(() => {
    const root = document.documentElement;
    const preferred = root.dataset.preferredTheme || 'light';
    const resolve = () => preferred === 'system' ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light') : preferred;
    try {
        const key = 'edulibrary-preference:' + root.dataset.account;
        if (localStorage.getItem(key) !== preferred) {
            localStorage.setItem('edulibrary-theme', resolve());
            localStorage.setItem(key, preferred);
        }
        const theme = localStorage.getItem('edulibrary-theme');
        root.dataset.theme = theme === 'dark' ? 'dark' : 'light';
    } catch { root.dataset.theme = resolve(); }
})();
