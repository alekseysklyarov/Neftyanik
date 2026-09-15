# Multitenancy Stage 1: database foundation

## Scope

The application remains single-association. No routing, middleware, tenant context, query filters, authorization, Identity membership, UI, or branding changes are included. Do not onboard another association through the running application until Stage 2 implements isolation. Reads and business-identifier validation queries are still unfiltered.

## Tenant ownership

All 22 business entities implement `IAssociationOwned` with required `AssociationId` and an `Association` navigation:

- `Plot`, `Member`, `PlotOwnership`, `PlotOwnershipHistory`;
- `MemberElectricityMeter`, `MemberElectricityReading`, `MemberElectricityTariff`;
- `AssociationElectricityReading`, `AssociationElectricityTariff`;
- `MembershipFeeRate`, `ChargeType`, `Charge`;
- `Payment`, `PaymentAllocation`, `PaymentNotification`;
- `ExpenseCategory`, `Expense`;
- `NewsArticle`, `AssociationDocument`, `SystemSetting`;
- `AuditLog`, `FinancialAuditLog`.

Dependent readings, allocations, and ownership history retain direct ownership for future filtering and composite FK enforcement. The old documented `MemberElectricityMeterPlots` join table is not in the current EF model: `Plot.MemberElectricityMeterId` is the current assignment.

`ApplicationUser`, all Identity tables and `UserLoginHistory` remain global. A login is global authentication activity, not association membership. Existing user references in business records do not establish authorization or membership. Both audit types and all current system settings are treated as association-owned; future platform audit/settings records need an explicit separate design.

## Migration and data preservation

Migration: `20260915193703_AddAssociationFoundation`.

1. Create `Associations` with an identity-generated integer primary key and insert `Нефтяник` / `neftyanik`, active, at the deterministic model seed timestamp.
2. Use seed ID `1` only for the new, empty table and existing model seed configuration. SQL Server identity generation remains enabled for future associations. Runtime code resolves by slug, never by numeric ID.
3. Drop replaced business FKs and indexes, without deleting their rows or changing primary IDs.
4. Add nullable integer `AssociationId` columns to all 22 existing business tables, without defaults.
5. Backfill every existing row using the association ID selected by slug `neftyanik`. Backfill SQL uses dynamic execution so new columns also bind correctly in idempotent scripts.
6. After all backfills, alter the 22 columns to `NOT NULL`, create eight alternate keys, recreate scoped indexes and install all restrictive FKs.

EF originally generated deletion/reinsertion of expense-category seeds. Those operations were removed: customized categories, membership rates, all business values, and related rows are preserved. Migration operations use the normal EF transaction; they do not suppress it. Plan the normal maintenance window and production backup because backfills/index changes can lock large tables. No deployment was performed.

`Down` preserves the original business rows and restores the old constraints while only the original association exists. It explicitly refuses rollback after another association exists; removing ownership then would risk merging unrelated data.

## Transitional writes

`ApplicationDbContext.SaveChangesAsync(bool, CancellationToken)` is the one compatibility hook. Both async overloads pass through it. The repository/application source scan found no synchronous `SaveChanges` callers.

For newly added business entities with neither an explicit association ID nor association navigation, it queries the persisted active `neftyanik` row and assigns its actual ID. Explicit IDs and new/existing association navigations are retained. A missing/inactive seed fails clearly; it is never silently recreated. There is no database default.

Assignment happens before automatic change detection, preventing EF fix-up from copying an unassigned new meter's tenant key into an existing plot. The caller's change-detection setting is restored even on failure. Association slugs are checked for lowercase ASCII URL-safe segments and a maximum length of 100 characters.

Remove the fallback when Stage 2 makes ownership explicit. Bulk/direct SQL writers and any future synchronous writers must provide ownership explicitly (or use the existing asynchronous save path).

## Unique business indexes

Each following key is now prefixed by `AssociationId`; existing filters are unchanged:

| Entity | Remaining key columns | Filter |
| --- | --- | --- |
| `Plot` | `Number` | none |
| `PlotOwnership` | `PlotId` | `ValidTo IS NULL` |
| `AssociationElectricityReading` | `ReadingDate` | none |
| `AssociationElectricityReading` | `IsInitialReading` | `IsInitialReading = 1` |
| `AssociationElectricityTariff` | `EffectiveFrom` | none |
| `MemberElectricityTariff` | `EffectiveFrom` | none |
| `MembershipFeeRate` | `Year` | none |
| `ChargeType` | `Code` | `Code IS NOT NULL` |
| `ChargeType` | `IsDefault` | `IsDefault = 1 AND IsActive = 1` |
| `MemberElectricityReading` | `MemberElectricityMeterId, ReadingDate` | none |
| `MemberElectricityReading` | `MemberElectricityMeterId, IsInitialReading` | `IsInitialReading = 1` |
| `MemberElectricityReading` | `ChargeId` | `ChargeId IS NOT NULL` |
| `PaymentNotification` | `PaymentId` | `PaymentId IS NOT NULL` |
| `Expense` | `AssociationElectricityReadingId` | `AssociationElectricityReadingId IS NOT NULL` |
| `SystemSetting` | `Key` | none |

`Association.Slug` and all Identity uniqueness remain global. Existing business list/date/lookup indexes were tenant-prefixed; actor-user indexes remain available for global user lookups. No redundant standalone tenant indexes are added where a prefixed index/key already suffices.

## Composite tenant FKs

All 18 tenant-to-tenant FKs use `(AssociationId, ForeignId)` to `(AssociationId, Id)`, with restrictive deletion:

- `PlotOwnership` -> `Plot`, `Member`;
- `PlotOwnershipHistory` -> `Plot`;
- `Plot` -> `MemberElectricityMeter`;
- `MemberElectricityMeter` -> `Member`, billing `Plot`;
- `MemberElectricityReading` -> `MemberElectricityMeter`, `Charge`;
- `Charge` -> `Plot`, `ChargeType`;
- `Payment` -> `Member`, `Plot`;
- `PaymentAllocation` -> `Payment`, `Charge`;
- `PaymentNotification` -> `Member`, `Payment`;
- `Expense` -> `ExpenseCategory`, `AssociationElectricityReading`.

Eight referenced principals have alternate `(AssociationId, Id)` keys: `Plot`, `Member`, `MemberElectricityMeter`, `Charge`, `ChargeType`, `Payment`, `ExpenseCategory`, `AssociationElectricityReading`.

An explicit identity integer converter for `AssociationId` resolves EF Core's mapping-inference cycle through the bidirectional plot/billing-meter relationships. It does not change the stored integer or add a database default.

Tariffs and membership rates belong directly to an association. Historical applied rates are value snapshots, not tariff FKs. `Charge` has no direct member relationship in this model: accounting ownership goes through its plot.

## Stage 2 risks

- No request isolation exists yet: filter reads, writes, uniqueness checks, reports, tariff selection, setting lookups and user-linked member selection before onboarding tenants.
- Identity actor/owner FKs cannot ensure association membership; server-side membership and ownership validation are still required.
- Composite FKs prevent different associations from being connected, but do not prove the same member owns both a meter and every assigned plot, or that an allocation belongs to the correct member within an association. Existing business rules still apply.
- Audit `EntityType`/`EntityId` strings and stored document/file paths cannot be protected by relational tenant FKs. Audit generation and storage access require tenant-aware validation.
- Existing seeded expense-category ID constants and unscoped reference-data lookups must be reviewed before multiple associations are used. Only the original association's reference data is seeded here.
- `AssociationId` participates in alternate keys on principal records; tenant transfer is not a supported normal entity update.
- Restart the current debugging session: entity interface additions and the save override cannot be hot-reloaded.

## Verification

- `dotnet build Neftyanik.Portal.sln`: succeeded, 0 errors, 12 existing warnings.
- Infrastructure suite: 85 passed, 0 failed, 0 skipped.
- Web suite: 225 passed, 0 failed, 0 skipped; no Web source changes.
- New tests use uniquely named disposable SQL Server LocalDB databases, never production connection settings.
- Tested all 18 composite tenant FKs with cross-association SQL updates.
- Tested compatibility, explicit ownership, missing/inactive seed, slug lookup with a non-seed numeric ID, scoped uniqueness and invalid slugs.
- Migration tests populate every tenant table at the previous migration, customize existing seeds, compare every original mapped column before/after, verify all rows are backfilled, verify trusted FKs/no defaults/no nullable tenant columns, and test normal migration, idempotent script replay and guarded rollback.
- EF reports no pending model changes; `git diff --check` passes.

Review artifacts are under `artifacts/`: `Stage1AssociationFoundation.Idempotent.sql`, build/test logs and `TestResults/*.trx`. Nothing was deployed or committed.
