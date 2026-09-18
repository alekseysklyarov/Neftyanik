# Stage 3: association membership and authorization

## Scope

Stage 3 adds explicit membership and tenant authorization to the existing .NET 8 Razor Pages application. Stage 2 URL resolution, query filters, model caching, composite accounting foreign keys, and write ownership enforcement remain intact. There is no provisioning, association selector, invitation system, public registration, platform administrator bypass, branding, or SaaS billing.

`ApplicationUser`, Identity usernames/email addresses, credentials, and `UserLoginHistory` remain global. A user can have a separate `Member` in each association. No `ApplicationUser.AssociationId` was introduced.

## Schema

- `AssociationUserMembership`: `Id`, `AssociationId`, `ApplicationUserId`, `Role`, `IsActive`, `CreatedAtUtc` (`DateTimeOffset`). One row represents one role assignment. Multiple roles in an association are allowed. The unique index is `(AssociationId, ApplicationUserId, Role)`. A check constraint restricts role names to `Administrator`, `Accountant`, and `Member`. Association and user foreign keys use restrictive deletion.
- `AssociationLoginEvent`: association-owned attribution link to a global `UserLoginHistory`. Its history foreign key is globally unique: a login event cannot be attributed to multiple associations. Both foreign keys use restrictive deletion.
- Both new types implement `IAssociationOwned`, acquiring the existing query filters and stored-ownership checks in `SaveChangesAsync`. All original 22 filtered entities remain filtered; the total is now 24. Global Identity and login-history entities remain unfiltered.

Role revocation removes that role-assignment row. Local account disabling retains assignments with `IsActive = false`; unlocking explicitly re-enables those assignments for an active member. Ordinary profile edits do not silently reactivate disabled memberships unless member activation changes. Financial records are never deleted by these operations.

## Production migration

Migration: `20260918151504_AddAssociationMemberships`, after `20260915193703_AddAssociationFoundation`. Historical migrations are unchanged.

The SQL Server migration:

1. Resolves the association by slug `neftyanik`, not a numeric identifier.
2. Performs preflight checks before creating tables or granting permissions.
3. Copies canonical `AspNetUserRoles`/`AspNetRoles` assignments to that association only. Each recognized role produces a separate membership; administrators/accountants need not have a `Member` record.
4. Copies `AspNetUsers.IsActive` to membership activation. Existing global lockout state is unchanged and is checked during authorization.
5. Uses `DISTINCT`, `NOT EXISTS`, and a unique index to prevent duplicate assignments. EF's idempotent script also guards migration history.
6. Does not modify existing user IDs, password hashes, security stamps, member links, role assignments, or financial/content records.

Preflight SQL diagnostics:

| Number | Data requiring review |
| --- | --- |
| 51030 | Missing `neftyanik` association |
| 51031 | Assigned noncanonical or unrecognized Identity role |
| 51032 | Legacy user/role role-claims, including projected-role claims |
| 51033 | Linked member without an Identity role |
| 51034 | Legacy user-to-member links outside `neftyanik` |
| 51035 | Multiple members for one user within an association |

A member link never implies Administrator, Accountant, or Member privileges. A linked member with recognized Administrator/Accountant roles retains exactly those roles, not an invented Member role. Unlinked users without role assignments receive no membership. The checks intentionally require manual review instead of guessing which permissions ambiguous records should receive. Diagnostics were tested on isolated synthetic databases; production data was not inspected.

Automatic `Down` is deliberately blocked with `NotSupportedException`: dropping memberships would destroy tenant-specific grants/revocations that global roles cannot reconstruct. Rollback requires a reviewed backup/restore and application rollback plan.

Before production migration, back up the database, run the preflight review against a restored copy, inspect the idempotent SQL, resolve any diagnostics with an approved data correction, and schedule a maintenance window without concurrent account/role writes. Deploy schema and application together. This development task did not apply migrations to production.

## Historical login attribution

Legacy `UserLoginHistory` has no association provenance. A current membership or member link cannot prove which URL was used historically. Consequently no historical attribution links are generated. Original history is preserved globally but not shown to tenant administrators.

New successful tenant logins create history and an `AssociationLoginEvent` together through the existing save operation. Recording requires active local membership. `UserActivityService` restricts user lists to current-association assignments and login summaries to explicit current-association links. Shared users' logins in another association do not appear in local summaries. Historical unlinked events require a separately authorized, evidence-based attribution process; none is implemented in Stage 3.

## Authorization and authentication

Pipeline order:

1. Association routing resolves an active URL association or returns 404 for unknown/inactive slugs.
2. ASP.NET Core Identity authenticates the global cookie.
3. `AssociationAuthorizationMiddleware` creates a new request-local principal. It removes each identity's role claims and all existing `dachahub:association-role` claims. It sets the role-claim type to `dachahub:association-role` and adds only roles returned by scoped `IAssociationMembershipService`.
4. ASP.NET Core authorization evaluates policies and endpoint attributes against that principal.

The original authentication ticket is not mutated or bound to an association. `User.IsInRole` and `[Authorize(Roles = ...)]` retain their previous permission combinations but consume only freshly projected tenant roles. No roles are appended to an otherwise globally privileged principal. Missing/unresolved membership, inactive assignments, inactive associations/users, and current global lockouts return no roles. There is no cross-request role cache: removal/deactivation takes effect on the next request with the same valid cookie. An already-running request is not retroactively cancelled.

The default `[Authorize]` policy requires authentication and one of the three local roles. `RequireAdministrator`, `RequireAccountant`, and `RequireMember` policies use the same request-local roles. Named policies and role attributes do not add an Administrator bypass or expand Accountant/Member permissions.

Login keeps Identity password validation, existing lockout-on-failure behavior, password-change requirements, antiforgery, and cookie security settings. A correct password without active tenant membership receives the generic login failure and no new session. Redirects use current tenant roles and tenant-local return URL validation. Root-path authentication cookies remain `Path = /`; legacy tenant-cookie cleanup remains in place. Logout remains available even after membership revocation and clears the global session.

## Global-role compatibility audit

- There are no remaining tenant authorization calls to `UserManager.IsInRoleAsync` or `UserManager.GetRolesAsync`, nor application service checks against `AspNetUserRoles`.
- Existing Identity role definitions/seeds, `RoleManager` registration, and administrator-bootstrap role-definition existence/creation remain for Identity/migration compatibility. Bootstrap grants a local membership, not a global role. The role-management PageModel retains its constructor's `RoleManager` dependency for compatibility but performs no global role assignment.
- The Stage 3 migration reads global role assignments once for controlled backfill and rejects ambiguous role claims. It leaves Identity data intact.
- Test fixtures deliberately retain/add global roles and even stale projected claims to prove they cannot grant access.
- Existing Razor checks in the shared layout, public home, Member dashboard, administration lists/handlers, financial administration, payment receipt administration mode, audit/user-activity pages, and notification bell all receive the projected principal. The notification component also checks local roles before querying private counts.

Financial/electricity services continue to use Stage 2 filtered data and ownership queries. Request-facing authorization is performed at the Razor Page boundary; internal services/CLI are not independently exposed public endpoints. Member lookups combine the authenticated user's ID with the implicit current-association filter. Tenant-local financial/audit actor references may display the global actor's name because the stored tenant record authorizes that reference; this does not expose a global user directory.

## Account management

- Creation saves a new global Identity account, its local Member role, and the member link in the existing database transaction. Duplicate global usernames are rejected, not silently linked or granted membership in another association.
- Role changes modify only filtered local assignments. There is no user-controlled association ID on the account forms. Foreign member IDs fail through filtered member lookups. No arbitrary account-linking UI or user deletion endpoint is introduced.
- Locking and archiving disable local assignments, not global Identity accounts. Local unlocking requires an active member. Self-locking through the dedicated lock action remains rejected.
- Legacy global lockouts may be cleared only if `AssociationAccountAccess` confirms a current assignment and no membership or member link in any other association, including inactive ones. Shared global lockouts cannot be changed by a tenant administrator.
- Details distinguish global account inactivity/lockout from local membership-disabled status.
- Profile edits and administrative password resets require exclusive association ownership of the account. Existing exclusive-account profile/activation and password-reset behavior is preserved. Shared-account global profile/password changes are denied; local roles and disabling remain manageable without affecting the other association.
- User self-service remains limited to the authenticated global account and current association's member record. A user's own password/profile changes are global by design.
- The existing `create-admin` CLI explicitly resolves active `neftyanik`; its service requires a resolved context. Assignment to an existing non-administrator requires the existing explicit option. This is an operator CLI, not an HTTP/platform-superadmin bypass. Legacy import still explicitly resolves its requested association.

## Public and private routes

Anonymous home, login, Privacy, AccessDenied, localization, health, and existing public/static routes retain their behavior. Anonymous private requests challenge to tenant-local login. Authenticated users lacking membership are redirected to tenant-local AccessDenied without private data. The denial page does not require membership, preventing redirect loops. Unknown/inactive associations return 404. Root/legacy redirects and tenant-prefixed assets retain Stage 2 behavior.

## Tests and verification

Final verification: Infrastructure **107 passed**, Web **312 passed**, focused HTTP security **41 passed**, focused Infrastructure membership/migration **13 passed** (including **8 populated migration tests**). Every run had zero failures and zero skipped tests. The solution build passed; existing compiler warnings remain. EF reported no pending model changes and `git diff --check` passed. The generated idempotent SQL was inspected and executed only by the isolated migration tests. Final TRX logs and generated SQL are under `%TEMP%/NeftyanikStage3Results`.

- `AssociationMembershipMigrationTests`: real isolated SQL Server LocalDB databases migrated from the populated Stage 2 schema. Verifies every existing column, passwords/IDs, all 22 business tables, multi-role/inactive assignments, a nondefault numeric `neftyanik` ID, unassigned users, untouched historical history, migration order, repeated idempotent script execution, and all preflight diagnostics.
- `AssociationMembershipTests`: role uniqueness, multiple roles, fail-closed unresolved context, live disabling, and forged foreign membership insert/update/delete rejection.
- `AssociationMembershipSecurityTests`: two associations, distinct and shared roles, valid encrypted Identity cookies, stale/global role claims, real logins, revocation/removal without logout, foreign IDs, account creation/roles/reset/lock/archive/edit attacks, shared account protection, duplicate global usernames, login attribution isolation, anonymous routes, legacy lockout compatibility, and account-status display.
- Existing Infrastructure/Web suites retain business and authorization assertions. Original Stage 2 schema tests explicitly target their own migration rather than accidentally including new tables. Cookie-path, filters, ownership, composite foreign key, static asset, and routing tests remain in the complete suites.

Reproduction commands (migration tests create only isolated `NeftyanikAssociationTests_*` / `NeftyanikPortalTests_*` LocalDB databases):

- `dotnet build Neftyanik.Portal.sln`
- `dotnet test tests/Neftyanik.Portal.Infrastructure.Tests`
- `dotnet test tests/Neftyanik.Portal.Web.Tests`
- `dotnet test tests/Neftyanik.Portal.Infrastructure.Tests --filter FullyQualifiedName~AssociationMembership`
- `dotnet test tests/Neftyanik.Portal.Web.Tests --filter FullyQualifiedName~AssociationMembershipSecurityTests`
- `dotnet ef migrations has-pending-model-changes --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext`
- `git diff --check`

## Limitations and prerequisites before a second real association

- No onboarding/provisioning or existing-user linking flow is supplied. Any future linking must prove the global account owner's consent; never attach a user solely by an email/username supplied by a tenant administrator.
- Shared global account recovery, global profile editing by administrators, and clearing shared global lockouts require a separate trusted workflow. The current UI can display links whose server-side shared-account guard denies the operation.
- Membership/role records are security data; database/operator access must be restricted. No platform role bypass or automatic future-association grants exist.
- Exclusive-account checks and subsequent Identity writes are not a substitute for serializing future provisioning/link operations. Before introducing concurrent linking/provisioning, add transaction/concurrency coordination so an account cannot become shared during a global reset/profile/unlock operation.
- Global username availability remains observable through the existing duplicate-login validation. Identity usernames/emails/passwords remain global; their privacy/recovery design needs review before public multi-association onboarding.
- Legacy login history is deliberately absent from tenant activity totals until trustworthy attribution exists.
- Membership changes take effect on subsequent requests, not in-flight operations. Existing password/lockout rules are preserved rather than redesigned here.
- Production data was not inspected or migrated. Rehearse on a restored production backup and resolve preflight diagnostics before deployment.

## Changed-file inventory

Existing unrelated changes to `Neftyanik.csproj` are excluded and must remain untouched.

### Domain/Application
- `src/Neftyanik.Portal.Domain/Entities/AssociationUserMembership.cs`
- `src/Neftyanik.Portal.Domain/Entities/AssociationLoginEvent.cs`
- `src/Neftyanik.Portal.Application/Associations/IAssociationMembershipService.cs`

### Infrastructure
- `Data/ApplicationDbContext.cs`
- `Data/Configurations/AssociationUserMembershipConfiguration.cs`
- `Data/Configurations/AssociationLoginEventConfiguration.cs`
- `DependencyInjection.cs`
- `Identity/AssociationAccountAccess.cs`
- `Identity/AdminBootstrapService.cs`
- `Services/AssociationMembershipService.cs`
- `Services/UserActivityService.cs`
- `Migrations/20260918151504_AddAssociationMemberships.cs` and `.Designer.cs`
- `Migrations/ApplicationDbContextModelSnapshot.cs`

### Web
- `Program.cs`, `Security/AssociationAuthorizationMiddleware.cs`
- `Pages/Account/Login.cshtml.cs`, `Pages/Account/LoginPageModelBase.cs`, `Pages/Index.cshtml.cs`
- `Pages/Administration/Index.cshtml.cs`
- `Pages/Administration/Members/Account/Create.cshtml.cs`, `Lock.cshtml.cs`, `ResetPassword.cshtml.cs`, `Roles.cshtml.cs`
- `Pages/Administration/Members/Archive.cshtml.cs`, `Details.cshtml.cs`, `Edit.cshtml.cs`
- `ViewComponents/PaymentNotificationBellViewComponent.cs`

### Tests
- Infrastructure: `AdminBootstrapServiceTests.cs`, `AssociationFoundationTests.cs`, `AssociationMigrationTests.cs`, `AssociationMembershipMigrationTests.cs`, `AssociationMembershipTests.cs`
- Web: `AdministrationFinanceCashInitializationTests.cs`, `AdministrationMemberAccountTests.cs`, `AdministrationMemberResetPasswordTests.cs`, `AuthenticationCookieTests.cs`, `HomePageLoginTests.cs`, `TenantHttpIsolationTests.cs`, `UserActivityServiceTests.cs`, `AssociationMembershipSecurityTests.cs`, `Infrastructure/PortalWebApplicationFactory.cs`

This document is `docs/MULTITENANCY_STAGE3.md`. Test result logs from earlier runs may remain untracked; they are not application source changes. No commit, push, production migration, deployment, or Stage 4 work is part of this task.
