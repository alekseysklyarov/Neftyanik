# First platform administrator onboarding

This is an explicit operator-only operation, not a Web endpoint or a startup seed. Do not run it against production without deployment approval. The running local smoke host is not updated or restarted by the onboarding implementation.

## Before running

- Use a reviewed build and an interactive terminal under an authorized deployment/OS account. Access to this executable plus its database credentials is the operator trust boundary; an association Administrator is not an operator.
- Configure the intended database through the existing secure environment configuration. For local verification use a newly isolated database, never the ordinary development or production database. Do not put database secrets or passwords in command-line arguments or tracked files.
- The EF schema, including `20260919185049_AddPermanentPlatformBootstrapState`, must already be installed through a separately approved deployment. This command neither applies migrations nor starts the Web server. Never start the application in `Development` against an existing database merely to check configuration: normal Development startup automatically applies migrations.
- Run from the Web content directory (local source Web directory, or the deployed/published release directory), so the displayed connection target is the one intended.
- Verify the server and database printed by the command. It performs a read-only identity query first and requires the operator to type the exact database name before any provisioning write.

## Ordinary bootstrap: pristine installation only

`create-first-platform-admin` is only for a pristine Identity store without any permanent marker or platform traces. **Any marker, including `LegacyReviewRequired`, blocks this command.** If a marker is absent but unrelated existing users make the store non-pristine, ordinary bootstrap refuses creation and seals the inconsistent state rather than treating it as a new installation. Existing-account collisions never promote or overwrite those accounts.

## Local build and command

Build to a separate artifact directory to avoid overwriting the running smoke server's binaries:

```powershell
dotnet build src/Neftyanik.Portal.Web/Neftyanik.Portal.Web.csproj -c Release --artifacts-path artifacts/platform-onboarding-build
Set-Location src/Neftyanik.Portal.Web
# Only after configuring this terminal for the explicitly approved isolated target:
dotnet ../../../artifacts/platform-onboarding-build/bin/Neftyanik.Portal.Web/release/Neftyanik.Portal.Web.dll create-first-platform-admin
```

## VPS command (only after separate production approval)

In an interactive SSH terminal, from the approved published release directory, with its trusted environment loaded without printing secrets:

```sh
dotnet ./Neftyanik.Portal.Web.dll create-first-platform-admin
```

No arguments after the command are accepted. Redirected input/output is rejected before application startup. Do not use a pipe, shell assignment, command argument or a configuration file to supply the temporary password. Use the command's hidden password and confirmation prompts; entered password characters are never echoed. Supply a strong unique password accepted by the configured Identity validators. No actual passwords are included here.

The command creates a **new dedicated account only**, with `MustChangePassword=true`, lockout enabled and 2FA disabled. It assigns only the global `PlatformAdministrator` role, not an association membership or tenant role. A login/email collision with an existing account is rejected; there is no promotion/overwrite option.

At the administrator-email prompt enter `alsklyr@gmail.com` for the intended first administrator. This is a recipient/mailbox identity, **not an SMTP sending account**. Identity stores the email and its normalized lookup value with `EmailConfirmed=false`. Protect the Identity database, backups and operator access as personal data; the email is not application-layer encrypted.

After the transaction commits, the command requests a confirmation email. Delivery is not part of the database transaction. Missing configuration or delivery failure does not undo the permanent marker, confirm the mailbox, clear `MustChangePassword`, or change the password. The controlled console message says that delivery was requested, not that it succeeded. Configure delivery and use the confirmation-resend button; never rerun bootstrap to repair email delivery.

## One-time semantics

User creation, role creation/assignment, the temporary-password expiry and the consumed marker are one transaction. SQL Server commands acquire a transaction-owned exclusive application lock before checking provisioning state, so concurrent bootstrap commands cannot both succeed.

The permanent marker is the singleton row (`Id=1`) in the global `PlatformBootstrapStates` table. `Disposition` is `Consumed` (0, the fail-closed default) or `LegacyReviewRequired` (1). There are no user, role or association foreign keys. Deleting/recreating `PlatformAdministrator`, revoking membership, or deleting its user cannot delete the marker or reopen provisioning. A role or legacy onboarding trace without a marker causes ordinary bootstrap to persist `Consumed` before refusing the request. The old `ConsumedAtUtc` column is retained: for review-required migration rows it records closure/classification time, not proof that an administrator existed. Successful initialization updates it and records `InitializedAtUtc` separately.

Migration `20260919185049_AddPermanentPlatformBootstrapState` creates the table under the same transaction-owned `DachaHub.FirstPlatformAdministrator` SQL Server application lock. Platform evidence takes precedence: any platform role (even empty), legacy bootstrap claim, platform-valued role/user claim, or platform onboarding token results in `Consumed`; existing claims are preserved. Existing Identity users without those traces result in `LegacyReviewRequired`, which still blocks ordinary bootstrap. A pristine Identity store with no users or platform traces receives no marker. The migration never grants roles or creates accounts. An entirely erased legacy history cannot be distinguished from a pristine store; audit evidence and backups must be reviewed before approving provisioning.

### Evidence supporting this unreleased migration revision

At implementation start, both migration files were untracked, `git log --all -- <migration-path>` and `git ls-files -- <migration-path>` returned no entries, and the local HEAD was `00ad0d5`. The repository's `deploy-production.yml` builds a committed `github.sha`, so it cannot include this untracked migration. Prior recorded migration verification used only disposable LocalDB fixtures. On that repository evidence the unreleased migration and its designer/snapshot were revised in place; no durable database was inspected or migrated.

Repository evidence cannot prove that an operator never ran an out-of-band migration. **Before release, confirm deployment/change records. If this migration ID was applied to any durable/shared database, stop: do not deploy this revised definition, delete migration history, or reclassify a consumed marker. A separately reviewed additive migration is required, preserving existing markers as closed by default.**

The migration changes no existing account, financial record or association membership. Its `Down` operation deliberately refuses to drop the security barrier. Do not use migration rollback to reopen bootstrap. Before an approved deployment, stop/drain old application instances and disable access to old bootstrap binaries: the legacy implementation does not understand the new marker. Restrict database write/DDL permissions; a database administrator can bypass application invariants by editing or dropping security state.

On a network/commit acknowledgement failure, verify provisioning state rather than assuming the command can safely be repeated. Repetition cannot replace an existing account or reopen a consumed bootstrap.

## Reviewed legacy initialization: existing tenant-only installation

The dedicated command is `initialize-first-platform-admin-for-existing-installation`. It is **not** a recovery command, a second bootstrap, a `--force` override, or a Web endpoint. Its application service is registered only in this CLI process, not in the normal Web host. Tenant Administrator membership supplies no authority to run it. The interactive guard and approval reference are safety/audit controls, **not authorization credentials**; controlled OS/deployment execution and database access are the authorization boundary. Restrict executable/configuration access, shell access and database credentials to approved operators. A compromised OS/database administrator remains outside the application's protection.

### Eligibility and approval, before execution

1. Independently verify the intended installation using deployment history, old release versions, audit/change records and protected historical backups. **Absence of current platform accounts, roles or claims does not prove that an administrator never existed.** `LegacyReviewRequired` only records a need for review, not successful review or permission to create an account.
2. Require an independent authorized approver to confirm the conclusion that this installation has never had a platform administrator. Record the evidence and approval/change reference outside the application. If evidence is incomplete, contradictory, or shows previous provisioning, stop and use the emergency-review process below; do not supply a reference merely to bypass the check.
3. Take a protected backup through the approved operations procedure. Rehearse the reviewed release and migration against an **isolated restored copy**, using fake email delivery or otherwise isolated credentials so the rehearsal cannot send to real users. Verify before/after tenant-user, membership and financial snapshots, including other associations. Keep production secrets and restored personal data within the approved access boundary.
4. Schedule an approved maintenance window. Disable old operator binaries/commands and concurrent administrative maintenance. Apply the approved schema separately; neither initialization command migrates the database or starts the Web listener. Configure production SMTP, trusted HTTPS origin and persistent Data Protection keys as described below.

### Operator execution, only after separate deployment approval

From the reviewed published Web content directory in a trusted interactive OS terminal, with the approved environment loaded without printing secrets:

```sh
dotnet ./Neftyanik.Portal.Web.dll initialize-first-platform-admin-for-existing-installation
```

No additional arguments or redirected input/output are accepted. The command:

1. Requires SQL Server and no resolved tenant context. Reads `SERVERPROPERTY('ServerName')` and `DB_NAME()` from the actual connection; displays them and requires **both exact names to be typed separately**. Compare these to the approved target inventory, not merely the configured connection-string alias. Abort on any mismatch. This name confirmation is not cryptographic database attestation; trusted connection configuration, TLS/network controls and operator review remain required.
2. Requires the explicit `REVIEWED` historical-eligibility attestation and a nonblank approval/change reference (maximum 100 characters, no control characters or secrets).
3. Prompts for a new dedicated login, administrator email (`alsklyr@gmail.com` for the intended first administrator), hidden temporary password and confirmation. Login/email collisions with existing accounts are refused, including case-normalized and login/email cross-collisions. No tenant account is promoted or modified.
4. Captures the executing OS domain/user identity (maximum 256 characters), not an identity supplied by a Web form. The database audit stores that operator, approval reference, initialization timestamp, and created Identity user ID (maximum 450 characters, no foreign key). The OS identity may be a deployment service account; the external change record must identify the human operator and approver. Audit fields must never contain passwords, reset tokens or SMTP secrets.

### Transaction and permanent protection

Inside a serializable transaction, the command's service acquires the transaction-owned exclusive `DachaHub.FirstPlatformAdministrator` lock, shared with ordinary bootstrap and migration. It requires exactly one marker with ID 1, disposition `LegacyReviewRequired`, a valid classification timestamp, and no existing initialization/operator/approval metadata. It also requires existing Identity users and rechecks all available platform evidence. Missing/multiple/unknown/inconsistent markers, a pristine store, or `Consumed` are refused. If surviving platform evidence is discovered, the marker is permanently sealed as `Consumed`, without modifying Identity or tenant records, so deleting the evidence afterwards cannot make the command eligible.

Only after these checks does Identity create one new active, unconfirmed-email account, create/assign the global platform role, and store its 24-hour temporary-password expiry with `MustChangePassword=true`. The marker transitions to `Consumed` and receives bounded audit metadata in the **same transaction**. Account/role/token/marker failure rolls back creation; the eligible state is not consumed by a rolled-back creation attempt. Concurrent commands cannot both succeed. There is no persistent unlocked or approved-to-bootstrap state between commands.

No association membership or financial data is changed. Successful provisioning permanently prevents either command from creating another administrator, even after deleting/recreating the user or role. Unknown commit outcomes require read-only operator inspection of the marker and Identity state before any retry, never marker deletion.

### Email and first login after initialization

Email delivery is attempted **after commit**. A delivery failure does not unconfirm/confirm the mailbox, change passwords, undo the consumed marker, or create another user. The command reports that creation committed and instructs the operator not to repeat initialization for delivery repair. Configure SMTP and resend confirmation from `/Platform/Account/ForgotPassword`; do not rerun the initializer. Email contents and transport exceptions are not printed.

The account reuses the existing email-confirmation and first-login flows below. Confirm ownership before email recovery can work. Sign in and replace the temporary password within 24 hours, using only the restricted onboarding cookie; if that password expires, confirm the email and use the existing 15-minute reset flow. Neither initialization nor recovery grants tenant memberships.

## First sign-in

1. Visit `/Platform/Account/Login` over HTTPS (HTTP is only for isolated loopback development).
2. Enter the temporary credentials within 24 hours of bootstrap.
3. Identity checks the password, account state and lockout. Only a separate, nonpersistent onboarding cookie is issued; any old application session is signed out.
4. This ticket expires after ten minutes, is not renewed, and grants only `/Platform/Account/ChangeInitialPassword`. It cannot authenticate to the ordinary Identity application scheme or tenant pages.
5. Each request rechecks the database role, active/lockout state, required-change flag, security stamp and temporary-password expiry.
6. Submit the current temporary password, a different new password and confirmation. Antiforgery is enforced. Password rules use the existing Identity validators. Incorrect current passwords count toward lockout.
7. Identity password change, clearing `MustChangePassword`, and removal of the expiry token commit atomically. Identity changes the security stamp; replaying an old onboarding ticket fails.
8. The onboarding ticket and old application session are cleared. Normal Identity password sign-in with the new password issues a fresh, nonpersistent application session, after platform access is rechecked. If that final sign-in cannot succeed, the user is sent to platform login without being promoted.

On validation/persistence failure, no unrestricted session is issued. The original flag/password remain if the transaction did not commit. A completed password change with a failed final sign-in is recovered by signing in normally with the new password.

## 2FA and other limits

A complete platform enrollment/challenge/recovery flow is not implemented. Platform accounts with `TwoFactorEnabled=true` are denied both full platform access and onboarding; the code does not disable an existing user's 2FA or bypass Identity to sign them in. Enable 2FA only as part of a separately reviewed complete flow.

The existing application password policy is reused (currently relatively permissive). Use a strong, unique operator password; any stronger platform-only minimum or breached-password screening needs a separate policy decision rather than silently changing tenant password rules.

CLI logging providers are disabled and only controlled status/error messages are printed. Temporary credentials are necessarily present briefly in process memory for Identity hashing; no claim is made to protect them from an administrator inspecting process memory. Use a trusted terminal without input recording, and distribute credentials only through an approved secure channel.

## Email verification

1. Open the bootstrap confirmation email, or visit `/Platform/Account/ForgotPassword`, enter the administrator email, and select **Повторить подтверждение email**.
2. Open the HTTPS link in a trusted browser with JavaScript enabled. Press **Подтвердить мой email**. GET does not confirm anything; the modifying POST requires antiforgery.
3. Identity validates its email-confirmation token. Only a currently active, unlocked platform-role holder with a unique email can confirm. The confirmation changes only `EmailConfirmed`; it does not sign in, grant roles, or clear the initial-password requirement.
4. The page redirects to password recovery. After confirmation, either finish the normal temporary-password flow or request a password-reset email. Email confirmation uses the existing Identity email provider (default lifetime one day), separate from the 15-minute recovery provider.

If SMTP is unavailable, normal temporary-password onboarding still works within its 24-hour window; email recovery stays unavailable until mailbox ownership has been proven. Legacy accounts with no email need approved operator-assisted email enrollment; this feature does not silently attach the intended Gmail address to an existing account.

## Password recovery

The platform login includes **Забыли пароль?**. Enter the confirmed administrator email and request recovery. The response and redirect are generic for nonexistent, tenant-only, duplicate-email, unconfirmed, disabled, locked, revoked, or otherwise ineligible accounts. Check Spam as well as Inbox. Delivery failures produce the same response; application warnings omit recipients, tokens, message bodies and exception details.

Open the emailed link and submit a new password and its confirmation. The dedicated `PlatformRecovery` provider uses `DachaHub.PlatformRecovery.v1`, purpose `PlatformPasswordReset`, and a **15-minute** lifetime. It does not change tenant Identity token settings. Identity password validators still apply.

The service rechecks email confirmation, unique email, active state, lockout, 2FA state and current global role inside a serializable transaction. Identity resets the password and rotates the security stamp; the transaction also clears `MustChangePassword` and removes temporary-password expiry. Thus a confirmed mailbox plus a newly chosen valid password securely completes initial-password onboarding even if the temporary password has expired. It never reenables a disabled user, clears an intentional lockout, creates an account, grants a role, or changes tenant memberships.

On success, the browser's application/onboarding cookies are cleared and it redirects to `/Platform/Account/Login`; there is no automatic full-session sign-in. Rotating the stamp invalidates the used token and all other outstanding reset tokens for that stamp. Every protected platform request checks the ticket stamp against current Identity state, independently of periodic cookie validation. Old application cookies cannot regain platform access after `MustChangePassword` becomes false; onboarding cookies are also revalidated on each request.

### Link handling and secret hygiene

- Links are built only from `PlatformRecovery:BaseUrl`, never from Host or forwarded Host. It must be an HTTPS origin with no credentials, path, query or fragment.
- The email uses `#userId=...&token=...`, not query parameters. Browsers do not send fragments in HTTP request URLs or Referer headers. `platform-recovery.js` decodes the fragment with `URLSearchParams`, replaces the current history URL with its token-free pathname, and places the values in hidden POST fields. It does not navigate or create another history entry. A missing fragment on a failed POST preserves the server-rendered fields.
- Confirmation/reset responses use `Cache-Control: no-store` and `Referrer-Policy: no-referrer`. They contain no third-party scripts. JavaScript is required to transfer the emailed fragment; do not manually move tokens into query strings or paste links into support tickets.
- Configure reverse proxies, APM, SMTP diagnostics and client telemetry **not to capture request bodies, form values, email bodies or full email links**. Do not enable EF sensitive-data logging or verbose Identity/MVC model-binding diagnostics in production. Never log raw transport exceptions; they may contain secrets.
- Fragments still exist in the original email and briefly in browser memory/history before the script runs. JavaScript failure, extensions, mailbox access, browser sync or endpoint compromise are outside this protection. No promise of secure deletion from those systems is made. Disable email link tracking/rewriting for these messages where possible; verify that the chosen delivery provider preserves fragments.

## Required production configuration

Use environment variables or a deployment secret store, never tracked `appsettings.json`, command-line passwords or the administrator's personal Gmail password. Set the following in the Web service **and** in the separately approved interactive bootstrap process:

| Environment variable | Required value / purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` (no automatic database migration) |
| `ConnectionStrings__DefaultConnection` | Approved SQL Server connection from the existing secret store |
| `PlatformRecovery__BaseUrl` | Actual public HTTPS origin, e.g. `https://portal.example.org` (replace the example; no path/query/fragment) |
| `PlatformSmtp__Host` | Your transactional email provider's authenticated STARTTLS SMTP hostname |
| `PlatformSmtp__Port` | Normally `587`; use the provider's STARTTLS port, not implicit-TLS port 465 |
| `PlatformSmtp__UserName` | Provider-issued sending credential / SMTP username |
| `PlatformSmtp__Password` | Provider-issued SMTP secret, injected securely; never the recipient's Gmail password |
| `PlatformSmtp__From` | A sender address authorized and verified by that provider |
| `DataProtection__KeysDirectory` | Stable absolute path to the protected, persistent key ring, outside release directories |
| `Security__RequireHttps` | `true` |
| `AllowedHosts` | The actual public host name(s), not `*` |
| `ReverseProxy__KnownProxies__0` | Actual trusted ingress IP when using a reverse proxy; additional entries as needed |

Do not leave trusted proxy/network lists empty on an exposed deployment. Restrict direct access to the application port and have ingress discard untrusted forwarded headers. This is important for both HTTPS handling and IP-based rate limits. Use `ReverseProxy__KnownNetworks__0` only for deliberately trusted CIDRs.

SMTP always requires TLS with ordinary certificate validation and explicit credentials. There is no plaintext fallback, default OS-credential fallback, credential-in-code, or development email dump. Sending is bounded by a 20-second timeout. Configure the provider's domain verification, SPF/DKIM and DMARC alignment, production sending approval (not sandbox-only delivery), outbound firewall access, and a reasonable sending quota. The recipient entered at bootstrap is `alsklyr@gmail.com`; the sending identity is independent. No live delivery test was performed during this task. A separately approved end-to-end delivery test is required before enabling production recovery.

### Persistent Data Protection keys

Web instances and the bootstrap process must use the same persistent key ring and the existing application discriminator `Neftyanik.Portal`. Preserve keys across restarts/releases and share the ring across instances. Restrict directory access to the service and authorized operators; back it up through the secret-backup process. The configured filesystem persistence alone does not provide key encryption at rest: use a protected/encrypted volume or a separately reviewed key-encryption integration. Do not delete old keys to invalidate one user's sessions; use Identity security-stamp rotation instead.

Missing/mismatched/lost keys make previously issued links and cookies unusable. Fix persistence and request a new email rather than extending token lifetime or disabling validation. SQL availability is also required on protected platform requests because authorization is checked against live state.

### Rate limits and troubleshooting

- Per application instance and client IP: recovery-email POSTs (both reset and confirmation) share five requests per 15 minutes; reset and confirmation POST endpoints each allow 20 per 15 minutes. Case/trailing-slash variants share their respective quota. A further 100-per-15-minute instance-wide limit applies to platform POSTs. There is no queue; rejection is HTTP 429. GET is not quota-consuming.
- Limits are in-memory, reset on restart, and are not a distributed anti-abuse system. Configure ingress/provider-wide limits for multiple instances and monitor abuse. Shared-IP users may share a quota.
- Generic responses prevent direct account enumeration, but synchronous SMTP work is not constant-time; eligible requests can take longer. Edge throttling helps but does not eliminate timing inference. A durable asynchronous delivery/outbox design would require separate scope/review.
- If a token is expired, invalid, already used, or predates a password/stamp change, request a new link. Keep the 15-minute recovery lifetime unchanged, verify UTC clock synchronization and key-ring consistency, and use the latest email after any account change. Requesting another token alone does not revoke earlier unused tokens; successful password reset does.
- If the initial password expired, first confirm the email, then use recovery. No temporary-password login is required for this path.
- If a link displays an empty/invalid form, enable JavaScript and reopen the original email link. Check whether mail software stripped the fragment. Do not append the token to an HTTP query string.
- If no email arrives, check sender configuration, provider delivery dashboards and safe generic application warnings. Do not troubleshoot by printing credentials, tokens or message bodies. Resolve duplicate emails, revoked roles, disabled/locked accounts or legacy missing-email enrollment through the authorized operator process, not through public recovery.

## Emergency recovery: operator-controlled, no second bootstrap

There is **no emergency-recovery command** in this change. Reviewed legacy initialization is only for a historically verified first administrator and cannot be used to repair or replace a previous one. Public recovery cannot solve a lost mailbox, deleted account/role, or intentional access revocation. If historical eligibility cannot be established, stop and obtain independent incident review and evidence preservation; an approval reference alone does not justify initialization. Do not remove `PlatformBootstrapStates`, edit old claims, change `Consumed` to `LegacyReviewRequired`, rerun either provisioning command, silently create a replacement administrator, or restore a whole production database merely to reset one account.

1. Open an audited incident/change request. Independently verify the operator and administrator through an approved out-of-band channel; require a second authorized approver. Explicitly distinguish accidental loss from intentional disabling/revocation, which must remain in force unless separately reversed by its authority.
2. Preserve a protected backup and audit evidence. Identify the exact existing Identity user ID, current global roles, email/confirmation state, bootstrap marker, and relevant history. Use an isolated restored copy for planning; no modification based only on a claimed email address or tenant-administrator permission.
3. Prefer restoring access to the original mailbox through its provider. If a mailbox change is approved, an independently reviewed maintenance operation must target only that existing user, use Identity APIs to set the new address **unconfirmed**, rotate the security stamp, invalidate outstanding tokens/sessions, and require fresh mailbox confirmation and a new password. Do not mark a replacement mailbox confirmed merely because an operator entered it.
4. If a global role was accidentally deleted/revoked, require explicit separate approval to restore only the approved role assignment on that existing account. Email/password recovery itself must never restore it. Preserve the consumed marker and all tenant data. An intentionally disabled or revoked administrator is not eligible for routine credential recovery.
5. If the account was deleted or legacy bootstrap history is ambiguous, stop. Obtain a separately reviewed identity-restoration plan from protected backups/audit evidence, including conflict and financial-reference checks. Do not silently create a replacement identity or reopen bootstrap.
6. Any future maintenance command requires approval before implementation. Its design must be operator-only, require interactive target and exact user-ID confirmation, support a read-only inspection phase, make narrowly scoped transactional Identity changes, retain the permanent marker, log approver/change IDs without secrets, and refuse tenant-account promotion. No general-purpose Web recovery backdoor or bootstrap override is acceptable.
7. After the approved repair, independently verify mailbox ownership, reset the password, verify that old cookies/tokens fail, review role/tenant diffs, and record the result. Remove temporary operator access and rotate exposed operational credentials if compromise is suspected.

## Verification and release gate

Use `dotnet test tests/Neftyanik.Portal.Infrastructure.Tests` and `dotnet test tests/Neftyanik.Portal.Web.Tests`; fixtures use unique disposable LocalDB databases or in-memory SQLite. Use `node --test tests/browser/platform-recovery.test.cjs` for the fragment-handling unit tests. These script tests execute the real JavaScript in an isolated DOM/history fixture; they do not constitute a live-browser or SMTP-provider acceptance test.

Build `Neftyanik.Portal.sln` in Release with `--artifacts-path artifacts/legacy-release`, run the EF pending-model-change check using the design-time factory with an inert connection configuration, and run `git diff --check`. Do not run `database update`, normal Development startup, bootstrap, or legacy initialization against an existing development/production database as a verification step. Preserve the separate smoke host. Regression coverage includes all-table snapshots for two associations, concurrent SQL commands, multiple/inconsistent markers, transaction failure injection, post-commit SMTP failure with a fake sender, no Web-service registration or endpoint, and replay after Identity deletion.

Before deployment approval: review the fail-closed migration effect on the target's existing bootstrap history, back up, approve the irreversible migration, provision SMTP and persistent keys, configure HTTPS/ingress limits, drain old binaries, and approve a real delivery/browser acceptance test. Passing automated tests alone does not authorize deployment.
