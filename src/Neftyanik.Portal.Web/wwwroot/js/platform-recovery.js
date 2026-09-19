(() => {
    const form = document.querySelector('[data-recovery-form]');
    if (!form || !window.location.hash) return;
    const values = new URLSearchParams(window.location.hash.substring(1));
    window.history.replaceState(null, '', window.location.pathname);
    form.elements.UserId.value = values.get('userId') || '';
    form.elements.Token.value = values.get('token') || '';
})();
