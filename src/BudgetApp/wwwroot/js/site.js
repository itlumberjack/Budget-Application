// Header button: switch between the dark (default) and light themes and remember the choice.
document.getElementById('themeToggle')?.addEventListener('click', () => {
    const root = document.documentElement;
    const theme = root.dataset.bsTheme === 'dark' ? 'light' : 'dark';
    root.dataset.bsTheme = theme;
    try { localStorage.setItem('budget.theme', theme); } catch { }
});
