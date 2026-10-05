(() => {
    const histories = () => document.querySelectorAll('[data-finance-history]');
    const setCurrentPages = url => histories().forEach(section =>
        url.searchParams.set(section.dataset.pageParameter, section.dataset.page));

    document.addEventListener('click', async event => {
        const link = event.target.closest('[data-history-more]');
        if (!link || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        const section = link.closest('[data-finance-history]');
        if (section.getAttribute('aria-busy') === 'true') return;

        const url = new URL(link.href);
        setCurrentPages(url);
        url.searchParams.set(section.dataset.pageParameter, Number(section.dataset.page) + 1);
        const error = section.querySelector('[data-history-error]');
        error.hidden = true;
        section.setAttribute('aria-busy', 'true');
        link.setAttribute('aria-disabled', 'true');
        link.classList.add('disabled');
        try {
            const response = await fetch(url, { headers: { Accept: 'text/html' } });
            if (!response.ok) throw new Error('History request failed');
            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            const replacement = page.getElementById(section.id);
            if (!replacement) throw new Error('History section missing');
            const restoreFocus = document.activeElement === link;
            section.replaceWith(replacement);

            const currentUrl = new URL(window.location.href);
            setCurrentPages(currentUrl);
            window.history.replaceState(window.history.state, '', currentUrl);
            // Keep fallback links in both lists consistent with the expanded state.
            histories().forEach(history => {
                document.querySelectorAll(`[data-history-page-input="${history.dataset.pageParameter}"]`)
                    .forEach(input => { input.value = history.dataset.page; });
                const next = history.querySelector('[data-history-more]');
                if (!next) return;
                const nextUrl = new URL(next.href);
                setCurrentPages(nextUrl);
                nextUrl.searchParams.set(history.dataset.pageParameter, Number(history.dataset.page) + 1);
                next.href = nextUrl;
            });
            document.querySelectorAll('[data-history-reset]').forEach(reset => {
                const resetUrl = new URL(reset.href);
                setCurrentPages(resetUrl);
                resetUrl.searchParams.delete('chargePage');
                reset.href = resetUrl;
            });
            if (restoreFocus) {
                const target = replacement.querySelector('[data-history-more]') || replacement.querySelector('h2');
                target.tabIndex = -1;
                target.focus({ preventScroll: true });
                if (target.matches('[data-history-more]')) target.removeAttribute('tabindex');
            }
        } catch {
            error.hidden = false;
        } finally {
            section.removeAttribute('aria-busy');
            link.removeAttribute('aria-disabled');
            link.classList.remove('disabled');
        }
    });
})();
