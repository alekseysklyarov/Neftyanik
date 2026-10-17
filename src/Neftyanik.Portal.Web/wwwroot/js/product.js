// Display saved, inert page previews at the same desktop width in every card.
(() => {
    const resize = element => {
        const frame = element.querySelector('iframe');
        if (frame) frame.style.transform = `scale(${element.clientWidth / 1440})`;
    };
    const previews = document.querySelectorAll('.screen-preview');
    previews.forEach(resize);
    if ('ResizeObserver' in window) {
        const observer = new ResizeObserver(entries => entries.forEach(entry => resize(entry.target)));
        previews.forEach(element => observer.observe(element));
    } else {
        window.addEventListener('resize', () => previews.forEach(resize));
    }
})();
