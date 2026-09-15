# Multitenancy Stage 2: runtime tenant isolation

## Scope and boundary

Stage 2 builds on the unchanged Stage 1 schema (`506c537`). It replaces Stage 1's temporary single-association write fallback with explicit runtime association resolution, query filtering and write enforcement. No tenant management, membership, tenant roles, branding, onboarding, deployment or commit is part of this work.

**This is a data-scoping boundary, not association-membership authorization.** A globally authorized user can currently navigate to any valid active association slug. Each request still sees only the association selected by that URL. Do not mistake global Administrator/Accountant roles for association membership; Stage 3 must address that before onboarding mutually untrusted associations.

## Association context

`Neftyanik.Portal.Application.Associations.IAssociationContext` exposes `AssociationId`, `Slug`, `Association` and `IsResolved` without any dependency on HTTP.

`AssociationContext` starts unresolved. `Resolve` accepts an existing active association with a valid slug and positive ID, and can be called only once. ID/slug are captured for the scope; callers cannot switch the scope to another tenant. Accessing `Association` while unresolved fails clearly.

DI registers one scoped `AssociationContext` and maps `IAssociationContext` to that same instance. The middleware and `ApplicationDbContext` therefore share a request-scoped context. A DbContext constructed without a context remains unresolved, not implicitly Neftyanik.

## Resolution, routing and public assets

`AssociationRoutingMiddleware` runs before endpoint routing, authentication and authorization. It looks up the first URL segment in the global `Associations` table with `AsNoTracking`, comparing `Slug` in SQL using the database column's collation and requiring `IsActive`.

For `/neftyanik/Administration/Members`:

- `Request.PathBase` becomes `/neftyanik` (appended to any existing host path base);
- `Request.Path` becomes `/Administration/Members`;
- the existing Razor Pages routes are unchanged;
- the resolved context is available before pages, services, layout components or Identity flows can query business data;
- the middleware restores the original path/base when the downstream request completes.

The relevant pipeline is:

1. forwarded headers, HTTPS/cookie/localization handling;
2. static files for existing unprefixed public assets;
3. association-prefix middleware;
4. static files again, now seeing the extracted path;
5. routing, authentication, authorization and endpoints.

The second static-file pass serves `/neftyanik/css/site.css`, `/neftyanik/js/site.js` and all vendor assets without copies or per-file URL changes. Layout `~/...` URLs naturally include `PathBase`. Public web-root directory names and filenames are not interpreted as association slugs when an unprefixed asset is missing. Public asset directory names are reserved from tenant routing.

Only public content belongs in `wwwroot`. This static-file arrangement is not an authorization system for private tenant documents or uploads.

## Canonical, legacy and global URLs

- `/` explicitly redirects to `/neftyanik/`.
- Existing case-sensitive page-root paths such as `/Administration/Members?search=x` explicitly redirect to `/neftyanik/Administration/Members?search=x`.
- `/neftyanik` redirects to `/neftyanik/`; equivalent tenant roots do the same.
- A slug matched by SQL with noncanonical casing redirects to the stored casing.
- These are HTTP 307 redirects: method and query string are preserved.
- Unknown or inactive associations return 404 with no fallback or redirect, including tenant-prefixed asset requests.
- `/health`, the error endpoint, and public unprefixed assets remain global.

Some conventional Razor `Index` links/redirects (including logout) generate the tenant root without a trailing slash. That root is normalized by the explicit 307 redirect; home-page requests and tests use `/neftyanik/` as the canonical URL.

The legacy page-root names and public asset roots must be considered when Stage 3 adds association-slug provisioning.

## Tenant-aware navigation and authentication flows

The existing Razor Pages organization, route templates (including absolute page templates), `asp-page` links/forms, `Url.Page`, and `RedirectToPage` remain in place. ASP.NET Core combines their paths with the current `PathBase`. This also preserves the tenant in navbar links, finance/member navigation, pagination, search forms, shared partials and cookie login/AccessDenied redirects.

Login and language-switch `returnUrl` values additionally pass `TenantReturnUrls.IsWithinAssociation` as well as MVC local-URL checks. Cross-tenant paths, external URLs, backslashes and dot-segment traversal are rejected. Invalid return URLs use the existing tenant-local default destination. Logout returns to the current tenant root, not another association.

Identity roles and cookie authentication were not redesigned. The HTTP tests deliberately reuse the same globally authorized identity under both tenant URLs to prove that roles do not bypass data filters.

## EF filters and model caching

Every model entity implementing `IAssociationOwned` gets the predicate:

`IsAssociationResolved && entity.AssociationId == CurrentAssociationId`

The expression references properties on the DbContext instance, not a local numeric ID captured at model creation. EF Core recognizes the context reference and parameterizes those properties for the current context when executing queries. The cached model can consequently be shared across unresolved, Neftyanik and second-tenant contexts.

Tests assert that two contexts share the same model object and repeatedly run identical queries, Includes and finance aggregates with different results for their scopes. Includes from global Identity users to tenant-owned members also respect the member filter.

An unresolved scope returns no association-owned rows. `Association`, `ApplicationUser`, all Identity entities and `UserLoginHistory` remain unfiltered/global. All 22 Stage 1 business entities retain their tenant filters; no schema or model-snapshot migration is required.

## SaveChanges ownership enforcement

The application uses asynchronous save paths. `ApplicationDbContext.SaveChangesAsync(bool, CancellationToken)` covers both async overloads:

- association-owned entries require a resolved context;
- Added entries with unassigned `AssociationId` receive the current context ID;
- explicitly mismatched IDs or association navigations are rejected;
- current and original ownership on existing entries must match the current tenant;
- ownership is assigned/validated before change detection, then checked again after navigation fix-up;
- for Modified/Deleted entries, `GetDatabaseValuesAsync` verifies the persisted association before saving. An attached stub with another tenant's primary ID and forged current-tenant OriginalValues is therefore rejected;
- the caller's change-detection setting is restored even if validation fails;
- the old runtime `neftyanik` lookup and implicit fallback are gone.

Synchronous association-owned saves fail explicitly and direct callers to the async path; they cannot bypass persisted-ownership verification. Existing global-only synchronous saves are unchanged. No current application business writer uses synchronous SaveChanges.

`AssociationIsolationException` is mapped to HTTP 403 without exposing exception details. Normal page lookup of another tenant's ID yields 404 first. Existing business validation, antiforgery and role authorization remain enabled.

The Stage 1 restrictive composite FKs `(AssociationId, ForeignId) -> (AssociationId, Id)` remain the final protection for all 18 tenant-to-tenant relationships, including allocations, readings, meters and ownership. Tests still verify every FK with intentional cross-tenant SQL operations.

## Query-filter bypass review and non-HTTP work

The complete `src` and `tests` C#/Razor source scan found **zero `IgnoreQueryFilters` occurrences**. No application `FromSql`, `ExecuteSql`, `ExecuteUpdate` or `ExecuteDelete` path was found. Migration and constraint tests use raw SQL intentionally in disposable test databases only.

The existing generic repository uses EF `FindAsync`. Database lookups apply the model filter; a request context cannot change tenant while retaining its tracked entities. Do not introduce filter bypasses or attach entities loaded by privileged cross-tenant code to a normal request context.

The legacy import command explicitly requires `--association=<slug>`, resolves an active association in its service scope, then invokes the existing import. Global bootstrap and schema-migration commands do not need tenant ownership. Future background jobs must likewise resolve an explicit association; never restore an implicit default.

Future raw/bulk mutation code must be reviewed separately because EF bulk SQL does not execute SaveChanges validation.

## Tests

`TenantIsolationTests` adds SQL Server tests for:

- shared-model cache safety across distinct tenant contexts;
- scoped DbSets, Includes, finance totals and Identity-to-member navigation;
- global Association/Identity availability and unresolved-scope fail-closed behavior;
- forged explicit ownership and navigation rejection;
- detached foreign-ID updates and deletes claiming the current tenant;
- immutable ownership even with automatic change detection disabled;
- immutable resolved scopes and rejection of synchronous tenant-write bypasses.

`TenantHttpIsolationTests` covers both directions with two associations sharing plot number `10` and the same global identity:

- member/plot listings, details, member dashboard and finance read isolation;
- foreign IDs returning 404;
- creates with forged query/form tenant IDs still belonging to the URL tenant;
- forged edits, archives and financial cancellations leaving foreign rows unchanged;
- unknown/inactive tenants, canonical/legacy redirects and SQL Server slug casing;
- CSS, JS, Bootstrap and jQuery serving under tenant prefixes;
- public missing-asset handling;
- navigation/forms/pagination and login/logout/language return URLs.

Existing tests retain their business/authorization assertions. Fixtures now provide explicit test associations; HTTP expectations use tenant paths. Stage 1 schema/data-preservation and composite-FK tests remain in the suites.

Verification artifacts are in `artifacts/` and `artifacts/TestResults/`. SQL Server tests create and remove uniquely named LocalDB databases and do not use production connection settings.

### Final verification

- `dotnet build Neftyanik.Portal.sln --no-restore`: passed, 0 errors.
- Web/test project build: passed, 0 errors; existing test-project warnings remain (none in the new isolation tests/shared helper).
- Complete Infrastructure suite: 94 passed, 0 failed, 0 skipped.
- Complete Web suite: 261 passed, 0 failed, 0 skipped.
- Focused foundation/migration/EF isolation tests: 34 passed.
- Focused HTTP isolation/routing/asset tests: 36 passed.
- EF pending-model check: no changes since the Stage 1 migration.
- `git diff --check`: passed. Domain entities and migration/snapshot files are unchanged.
- The current Visual Studio debugging session must be restarted; the interface/override changes cannot be applied through Hot Reload. No running application was restarted or deployed by this task.

## Stage 3 requirements and limitations

1. Add explicit user-to-association membership and enforce it before granting access to tenant endpoints. Global roles currently authorize actions in any valid active URL tenant.
2. Design tenant-specific roles, invitations/onboarding and any privileged cross-tenant administration explicitly; none exists in Stage 2.
3. Provision per-association tariffs/settings/reference data. The original seeded expense-category ID constants remain legacy assumptions and must not become a universal cross-tenant reference; composite FKs prevent connecting them to another tenant's rows. There is no association provisioning UI in this stage.
4. Design authorization for private tenant documents/file storage, separate from public static assets, and distinguish platform audit/settings data from association-owned data where needed.
5. Preserve explicit context resolution for jobs/imports and review future raw/bulk SQL for tenant restrictions.

Do not begin these Stage 3 changes as part of Stage 2. No commit or deployment was performed.
