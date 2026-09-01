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

const paymentReceiptText = (() => {
  const language = (document.documentElement.lang || "").toLowerCase();

  if (language.startsWith("uk")) {
    return { print: "Друк", printPayment: "Роздрукувати платіж" };
  }

  if (language.startsWith("en")) {
    return { print: "Print", printPayment: "Print payment" };
  }

  return { print: "Печать", printPayment: "Распечатать платеж" };
})();

function createPaymentReceiptButton(paymentId, memberId) {
  const link = document.createElement("a");
  const receiptUrl = new URL(`/Payments/${paymentId}/Receipt`, window.location.origin);

  if (memberId) {
    receiptUrl.searchParams.set("memberId", memberId);
  }

  link.href = `${receiptUrl.pathname}${receiptUrl.search}`;
  link.className = "btn btn-sm btn-outline-secondary payment-receipt-link";
  link.textContent = paymentReceiptText.print;
  link.title = paymentReceiptText.printPayment;
  link.setAttribute("aria-label", paymentReceiptText.printPayment);
  return link;
}

function getPaymentReceiptPageContext() {
  const path = window.location.pathname.replace(/\/$/, "");
  const paymentPage = Math.max(1, Number.parseInt(new URLSearchParams(window.location.search).get("paymentPage") || "1", 10) || 1);

  if (path === "/Member") {
    return {
      scope: "member-dashboard",
      paymentPage,
      table: document.querySelector("#member-dashboard-finance-pane .col-xl-6:nth-child(2) table"),
      memberId: null,
      appendColumn: true,
      rowSelector: "tbody > tr:not(.member-dashboard-table-note-row)"
    };
  }

  const memberPlotMatch = path.match(/^\/Member\/Plots\/(\d+)\/Finance$/i);
  if (memberPlotMatch) {
    const tables = Array.from(document.querySelectorAll("table"));
    return {
      scope: "member-plot",
      paymentPage,
      plotId: memberPlotMatch[1],
      table: tables.at(-1) || null,
      memberId: null,
      appendColumn: true,
      rowSelector: "tbody > tr"
    };
  }

  const administrationMemberMatch = path.match(/^\/Administration\/Members\/(\d+)\/Finance$/i);
  if (administrationMemberMatch) {
    const tables = Array.from(document.querySelectorAll("table"));
    const paymentTable = tables.find((table) =>
      Array.from(table.querySelectorAll("thead th")).some((cell) => {
        const text = cell.textContent.trim().toLowerCase();
        return text === "документ" || text === "document";
      }));

    return {
      scope: "admin-member",
      paymentPage,
      memberId: administrationMemberMatch[1],
      table: paymentTable || null,
      appendColumn: false,
      rowSelector: "tbody > tr"
    };
  }

  return null;
}

async function initializePaymentReceiptLinks() {
  const context = getPaymentReceiptPageContext();
  if (!context || !context.table || context.table.dataset.paymentReceiptInitialized === "true") {
    return;
  }

  const endpoint = new URL("/Payments/ReceiptLinks", window.location.origin);
  endpoint.searchParams.set("scope", context.scope);
  endpoint.searchParams.set("paymentPage", context.paymentPage);

  if (context.plotId) {
    endpoint.searchParams.set("plotId", context.plotId);
  }

  if (context.memberId) {
    endpoint.searchParams.set("memberId", context.memberId);
  }

  try {
    const response = await fetch(`${endpoint.pathname}${endpoint.search}`, {
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    });

    if (!response.ok) {
      return;
    }

    const result = await response.json();
    const paymentIds = Array.isArray(result.paymentIds) ? result.paymentIds : [];
    const receiptMemberId = result.memberId || context.memberId || null;

    if (paymentIds.length === 0) {
      context.table.dataset.paymentReceiptInitialized = "true";
      return;
    }

    let rows = Array.from(context.table.querySelectorAll(context.rowSelector));
    if (context.scope === "admin-member") {
      rows = rows.filter((row) => row.children.length > 1);
    }

    if (context.appendColumn) {
      const headerRow = context.table.querySelector("thead tr");
      if (headerRow && !headerRow.querySelector(".payment-receipt-header")) {
        const headerCell = document.createElement("th");
        headerCell.className = "text-end payment-receipt-header";
        headerCell.textContent = paymentReceiptText.print;
        headerRow.appendChild(headerCell);
      }
    }

    rows.slice(0, paymentIds.length).forEach((row, index) => {
      const paymentId = paymentIds[index];
      if (!paymentId || row.querySelector(".payment-receipt-link")) {
        return;
      }

      const button = createPaymentReceiptButton(paymentId, receiptMemberId);

      if (context.appendColumn) {
        const cell = document.createElement("td");
        cell.className = "text-end";
        cell.appendChild(button);
        row.appendChild(cell);
      } else {
        const actionCell = row.lastElementChild;
        if (!actionCell) {
          return;
        }

        button.classList.add("ms-1");
        actionCell.appendChild(button);
      }
    });

    if (context.appendColumn) {
      context.table.querySelectorAll("tbody > tr").forEach((row) => {
        if (rows.includes(row)) {
          return;
        }

        const singleCell = row.children.length === 1 ? row.firstElementChild : null;
        if (singleCell && singleCell.hasAttribute("colspan")) {
          const currentColspan = Number.parseInt(singleCell.getAttribute("colspan") || "0", 10);
          if (currentColspan > 0) {
            singleCell.setAttribute("colspan", String(currentColspan + 1));
          }
        }
      });
    }

    context.table.dataset.paymentReceiptInitialized = "true";
  } catch {
    // Receipt printing is an enhancement; finance pages must remain usable if it cannot be initialized.
  }
}

void initializePaymentReceiptLinks();
