(() => {
    "use strict";
    const root = document.querySelector("[data-guide]");
    if (!root) return;
    const input = root.querySelector("#guide-search");
    const role = root.querySelector("#guide-role");
    const status = root.querySelector("[data-guide-status]");
    const empty = root.querySelector("[data-guide-empty]");
    const normalize = value => value.normalize("NFKC").toLocaleLowerCase().replace(/ё/g, "е").replace(/\s+/g, " ").trim();
    const articles = Array.from(root.querySelectorAll("[data-guide-article]")).map(node => ({
        node, id: node.id, roles: node.dataset.roles.split(" "), text: normalize(node.textContent + " " + node.dataset.keywords)
    }));
    const navItems = Array.from(root.querySelectorAll("[data-guide-nav-item]"));
    const resetButtons = Array.from(root.querySelectorAll("[data-guide-clear]"));
    const params = new URL(window.location.href).searchParams;
    input.value = (params.get("q") || "").slice(0, 200);
    role.value = ["member", "accountant", "administrator"].includes(params.get("role")) ? params.get("role") : "all";
    input.maxLength = 200;

    const apply = () => {
        const words = normalize(input.value).split(" ").filter(Boolean);
        const visible = new Set();
        articles.forEach(article => {
            article.node.hidden = !(role.value === "all" || article.roles.includes(role.value)) || !words.every(word => article.text.includes(word));
            if (!article.node.hidden) visible.add(article.id);
        });
        navItems.forEach(item => { item.hidden = !visible.has(item.dataset.guideNavItem); });
        root.querySelectorAll("[data-guide-nav-group]").forEach(group => {
            group.hidden = !Array.from(group.querySelectorAll("[data-guide-nav-item]")).some(item => !item.hidden);
        });
        status.textContent = status.dataset.template.replace("{count}", String(visible.size)).replace("{total}", String(articles.length));
        empty.hidden = visible.size > 0;
        resetButtons.forEach(button => { button.disabled = input.value.length === 0 && role.value === "all"; });
        const url = new URL(window.location.href);
        if (input.value.trim()) url.searchParams.set("q", input.value.trim()); else url.searchParams.delete("q");
        if (role.value !== "all") url.searchParams.set("role", role.value); else url.searchParams.delete("role");
        window.history.replaceState(null, "", url);
    };
    const reset = () => { input.value = ""; role.value = "all"; apply(); input.focus(); };
    const revealTarget = () => {
        const id = window.location.hash.slice(1);
        const target = articles.find(article => article.id === id);
        if (target?.node.hidden) { input.value = ""; role.value = "all"; apply(); }
        navItems.forEach(item => {
            const link = item.querySelector("a");
            if (item.dataset.guideNavItem === id) link.setAttribute("aria-current", "location"); else link.removeAttribute("aria-current");
        });
        if (target) { target.node.scrollIntoView({ block: "start" }); target.node.querySelector("h2").focus({ preventScroll: true }); }
    };
    input.addEventListener("input", apply);
    role.addEventListener("change", apply);
    input.addEventListener("keydown", event => { if (event.key === "Escape") reset(); });
    resetButtons.forEach(button => button.addEventListener("click", reset));
    root.querySelector("[data-guide-print]").addEventListener("click", () => window.print());
    window.addEventListener("hashchange", revealTarget);
    const compact = window.matchMedia("(max-width: 800px)");
    const toc = root.querySelector("[data-guide-toc]");
    const sizeToc = () => { toc.open = !compact.matches; };
    compact.addEventListener("change", sizeToc);
    sizeToc();
    root.querySelector("[data-guide-controls]").hidden = false;
    apply();
    revealTarget();
})();
