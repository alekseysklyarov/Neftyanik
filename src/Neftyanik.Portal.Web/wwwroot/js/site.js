// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.querySelectorAll('[data-auto-submit="culture-selector"]').forEach((select) => {
  select.addEventListener("change", () => {
    const form = select.form;

    if (!form) {
      return;
    }

    if (typeof form.requestSubmit === "function") {
      form.requestSubmit();
      return;
    }

    form.submit();
  });
});

document.querySelectorAll("tr[data-row-url]").forEach((row) => {
  row.addEventListener("click", (event) => {
    if (event.defaultPrevented || event.button !== 0 || !(event.target instanceof Element)) {
      return;
    }

    if (event.target.closest('a, button, input, select, textarea, label, summary, [role="button"], [role="link"], [role="checkbox"], [role="menuitem"], [role="combobox"], [tabindex], [contenteditable]:not([contenteditable="false"]), [data-bs-toggle], .dropdown-menu')) {
      return;
    }

    window.location.assign(row.dataset.rowUrl);
  });
});
