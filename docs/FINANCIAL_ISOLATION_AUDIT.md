# Cross-association financial isolation audit

## Scope and method

Audited the current Stage 3 working tree without changing membership authorization or financial formulas. Tests create `neftyanik` (A) and `finance-b` (B) in fresh, isolated SQL Server LocalDB databases. No production database was accessed or migrated.

`tests/Neftyanik.Portal.Web.Tests/FinancialIsolationTests.cs` uses the real payment, notification, charge, member-electricity, and association-electricity services, finance PageModels, and authenticated Razor Page requests. A shared global user has a separate member in each association. Administrator/Accountant tests authenticate within A and submit B's IDs, so denial must come from financial isolation rather than a missing role.

Each association has separate charges, payments, allocations, pending notifications, expenses, cash initialization, plots/ownership, individual meters/readings/tariffs, common-meter readings/tariffs, and financial audit history. Matching plot numbers, meter numbers, dates, setting keys, and charge codes intentionally exercise tenant-scoped uniqueness and lookup.

## Confirmed defect and correction

Common-meter expense creation used global `ExpenseCategoryIds.ElectricityPayment` (ID 1). In B, that ID belongs to A. The regression reproduced a SQL Server violation of `FK_Expenses_ExpenseCategories_AssociationId_ExpenseCategoryId`. The Stage 2 composite foreign key prevented a cross-association write, but B could not complete the expense operation.

Expense classification, category archive protection, and manual-expense selectors also assumed global category ID 1. They now use a common association-scoped resolver:

- `ElectricityExpenseCategoryQueries.GetElectricityExpenseCategoryIdAsync` reads the existing tenant-owned `SystemSettings` table using key `Finance.ElectricityExpenseCategoryId`.
- The value must be a positive integer identifying an expense category belonging to the currently resolved association. Both setting and category lookups explicitly constrain `AssociationId` as well as retaining query filters.
- If no setting exists, legacy ID 1 is accepted only if it belongs to the current association. Existing Neftyanik behavior is preserved without a data migration.
- Invalid/foreign mappings and unresolved association contexts return no category. Expense creation returns a controlled failure without inserting an expense or financial audit entry.
- Each additional association must have its electricity category explicitly mapped through its own `SystemSettings` row. No category is inferred from a display name, no data is copied from another association, and no provisioning UI is introduced.
- Expense-list electricity/manual SUM calculations and filters, category labels/archive protection, and manual-expense selectors use the same mapping. Calculations and role permissions are unchanged.

No other isolation defect was reproduced in the covered paths.

## Query and operation findings

| Area | Finding / regression coverage |
| --- | --- |
| Charges, payments, allocation totals | Filtered roots and tenant-qualified foreign keys; foreign member/plot/charge/payment IDs are rejected. |
| Debt and overpayment | Existing `CalculateActiveBalanceAsync`, plot payment totals, navigation SUM projections, and finance dashboard totals remain independent. A has debt 750; B has overpayment 750. |
| Cash and expenses | Separate initialization rows with the same key; active payment/expense totals and cash calculations are isolated. Initial cash balances in the fixture are 580 for A and 4740 for B. |
| Notifications | Pending COUNT, administration/recent lists, confirmation and rejection queries are filtered. Foreign notification IDs and mixed local-member/foreign-plot confirmation fail without writes. |
| Approval, allocation, cancellation | Local approval allocates only to local charges. Local payment/charge cancellation leaves every snapshotted financial record, balance and history row in the other association unchanged. |
| Individual electricity | Member, billing plot, linked plots, latest readings and tariff selection remain local; same-date/different-rate fixtures yield local consumption and charges. Foreign meter read/update/create requests fail. |
| Common electricity | Independent initial readings and same-date tariffs are supported. Supplier amounts use local rates. Category-ID defect corrected as described above. |
| Dashboards, receipts, history, audit | Financial snapshots include every scalar field of relevant records plus calculated summaries/navigation totals. HTTP financial lists do not show foreign markers; foreign receipt, finance, expense, meter, and audit IDs return 404. |
| Raw SQL / filter bypass | No raw SQL or `IgnoreQueryFilters` was found in the runtime financial paths. Existing filter bypasses in `AssociationAccountAccess` are unrelated shared-account protection checks; migrations are outside request-time finance queries. |

Two-direction tests add payments, charges, expenses and notifications and change cash settings, tariffs and readings in B while checking A, and vice versa. Snapshots include charges, payments, allocations, notifications, expenses/categories, system settings, audit rows, both electricity models/tariffs, plots and ownership. Shared-user self-service pages and receipts are also checked against foreign records.

This is regression evidence for the inspected paths, not a claim that tests prove every possible future query safe. New financial endpoints must retain filtered roots, ownership validation and tenant-qualified relationships.

## Changes

- Added `src/Neftyanik.Portal.Infrastructure/Data/Queries/ElectricityExpenseCategoryQueries.cs`.
- Updated `src/Neftyanik.Portal.Infrastructure/Services/AssociationElectricityService.cs`.
- Updated Web `Pages/Administration/Finance/Expenses/{Index,Create,Edit}.cshtml.cs`.
- Updated Web `Pages/Administration/Finance/ExpenseCategories/{Index,Archive}.cshtml.cs`.
- Added `tests/Neftyanik.Portal.Web.Tests/FinancialIsolationTests.cs`.
- Added this audit record.

No schema, migration, accounting formula, permission, or unrelated project-file change is required.

## Verification

- Focused `FinancialIsolationTests`: **15 passed, 0 failed, 0 skipped**.
- Complete Infrastructure suite: **109 passed, 0 failed, 0 skipped**.
- Complete Web suite: **327 passed, 0 failed, 0 skipped**.
- Workspace build: passed.
- `git diff --check`: passed.

An intermediate expanded focused run was cancelled; the final 15-case focused run and both complete suites subsequently completed successfully. The original regression failed with the SQL Server category foreign-key violation before the fix. Final TRX files are in `%TEMP%/NeftyanikFinancialAudit`: `financial-isolation-final.trx`, `financial-audit-infrastructure.trx`, and `financial-audit-web.trx`.

No commit, push, deployment or production migration was performed. The existing unrelated `Neftyanik.csproj` content is preserved.
