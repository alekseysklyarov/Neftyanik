# First platform administrator onboarding

This is an explicit operator-only operation, not a Web endpoint or a startup seed. Do not run it against production without deployment approval. The running local smoke host is not updated or restarted by the onboarding implementation.

**SMTP is optional.** Approved CLI provisioning and SSH password recovery require neither email delivery nor confirmed email. The account must still complete the restricted web password-change flow before platform access. Email addresses remain unconfirmed unless mailbox ownership is proven through the optional confirmation flow.

## Production command summary (after separate approval)

Connect to the approved VPS through SSH under an authorized operator account and change to the reviewed published Web directory with its trusted environment loaded. Do not copy production credentials into these examples.

| Situation | Exact command |
| --- | --- |
| First administrator on the existing tenant-only installation, after historical review | `dotnet ./Neftyanik.Portal.Web.dll initialize-first-platform-admin-for-existing-installation` |
| First administrator on a genuinely pristine installation | `dotnet ./Neftyanik.Portal.Web.dll create-first-platform-admin` |
| Forgotten password or expired temporary password for an existing active platform administrator | `dotnet ./Neftyanik.Portal.Web.dll reset-platform-admin-password` |

After creation or reset, open `/Platform/Account/Login` over HTTPS, enter the exact login and temporary password, and complete `/Platform/Account/ChangeInitialPassword` within 24 hours. Only then can the administrator access `/Platform`. No email is sent by these CLI commands. Never rerun first provisioning to recover a password.

## Before running

- Use a reviewed build and an interactive terminal under an authorized deployment/OS account. Access to this executable plus its database credentials is the operator trust boundary; an association Administrator is not an operator.
- Configure the intended database through the existing secure environment configuration. For local verification use a newly isolated database, never the ordinary development or production database. Do not put database secrets or passwords in command-line arguments or tracked files.
- The EF schema, including `20260919185049_AddPermanentPlatformBootstrapState` and `20260920163502_AddPlatformPasswordRecoveryAudit`, must already be installed through a separately approved deployment. These commands neither apply migrations nor start the Web server. Never start the application in `Development` against an existing database merely to check configuration: normal Development startup automatically applies migrations.
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

After the transaction commits, the command prints web onboarding instructions and does **not** attempt SMTP. Missing SMTP configuration or a failed optional confirmation email cannot prevent CLI-authorized onboarding. It does not confirm the mailbox, clear `MustChangePassword`, or undo the permanent marker. If email functionality is wanted later, configure delivery and request confirmation from the recovery page; never rerun bootstrap to repair email delivery.

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
4. Schedule an approved maintenance window. Disable old operator binaries/commands and concurrent administrative maintenance. Apply the approved schema separately; neither initialization command migrates the database or starts the Web listener. Configure HTTPS and persistent Data Protection keys as described below. SMTP and the recovery-link origin are needed only if enabling optional email functionality.

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

No email delivery is attempted by initialization. The command reports that creation committed and directs the operator to web password change without SMTP. Optional confirmation can later be requested from `/Platform/Account/ForgotPassword`. Delivery failure cannot unconfirm/confirm the mailbox, change passwords, undo the consumed marker, or create another user. Do not rerun the initializer. Email contents and transport exceptions are not printed.

Sign in and replace the temporary password within 24 hours using only the restricted onboarding cookie. If it expires, use the SSH reset command below; optional verified-email recovery remains available if configured. Email ownership must be proven before email-based recovery can work, but is not a prerequisite for CLI onboarding. Neither initialization nor password recovery grants tenant memberships.

## First sign-in

1. Visit `/Platform/Account/Login` over HTTPS (HTTP is only for isolated loopback development).
2. Enter the temporary credentials within 24 hours of bootstrap.
3. Identity checks the password, account state and lockout. Only a separate, nonpersistent onboarding cookie is issued; any old application session is signed out.
4. This ticket expires after ten minutes, is not renewed, and grants only `/Platform/Account/ChangeInitialPassword`. It cannot authenticate to the ordinary Identity application scheme or tenant pages.
5. Each request rechecks the database role, active/lockout state, required-change flag, security stamp and temporary-password expiry. It also requires CLI onboarding authorization bound to the current stamp (`AspNetUserTokens`, provider `DachaHub.PlatformOnboarding`, name `CliOnboardingSecurityStamp`). For pending accounts created by the previous CLI release, the permanent consumed marker's `InitializedUserId` supplies backward-compatible provenance when this token is absent. An unrelated account with a role and `MustChangePassword` alone cannot enter this path. CLI reset can explicitly authorize an existing eligible platform account; it never grants the role.
6. Submit the current temporary password, a different new password and confirmation. Antiforgery is enforced. Password rules use the existing Identity validators. Incorrect current passwords count toward lockout.
7. Identity password change, clearing `MustChangePassword`, and removal of both the expiry and CLI-authorization tokens commit atomically. Identity changes the security stamp; replaying an old onboarding ticket fails. Email-confirmation status is preserved.
8. The onboarding ticket and old application session are cleared. Normal Identity password sign-in with the new password issues a fresh, nonpersistent application session, after platform access is rechecked. If that final sign-in cannot succeed, the user is sent to platform login without being promoted.

On validation/persistence failure, no unrestricted session is issued. The original flag/password remain if the transaction did not commit. A completed password change with a failed final sign-in is recovered by signing in normally with the new password.

## SSH password recovery

`reset-platform-admin-password` is registered only in its CLI process, not in the Web host. It is a narrowly scoped credential-recovery operation, **not** a means to create accounts, promote tenant users, unlock disabled accounts, restore roles, or reopen bootstrap. Interactive prompts and a change reference are not authorization: restrict SSH/OS execution and database credentials to approved operators. The service requires SQL Server and refuses a tenant-resolved context.

1. Obtain operator approval and confirm the intended existing account through the approved out-of-band process. Retain a protected backup and follow the change-management procedure. Enter from the reviewed published directory; do not run a second Web host or apply migrations through this command.
2. Run `dotnet ./Neftyanik.Portal.Web.dll reset-platform-admin-password` with **no extra arguments**. Redirected input/output is rejected before application startup. Type the exact SQL Server instance and database names returned by the actual connection, after comparing them to the approved inventory.
3. Enter the **exact, case-sensitive existing username**, not an email alias. A username that itself is an email is accepted only as that exact username. Missing users, ordinary tenant accounts, login/email ambiguity, inactive or locked accounts, revoked platform roles, and unsupported 2FA-enabled accounts are refused. An expired temporary password does not disqualify an otherwise eligible account.
4. Enter a non-secret approval/change reference (maximum 100 characters). The command records the executing OS domain/user, not an Identity user selected from a form. The external approval record must identify the human operator if execution uses a service account.
5. Enter and confirm a strong new temporary password using hidden terminal input. It must satisfy the existing Identity validators. No password is read from arguments, configuration, environment variables, pipes or files, and no password/reset token is printed or audited.
6. The service takes a serializable transaction and the shared transaction-owned exclusive `DachaHub.FirstPlatformAdministrator` SQL Server lock. It rechecks the exact account, current platform role, active/lockout state and stamp captured before password entry. Concurrent attempts prepared against the same stamp cannot both commit: a stale attempt reports a conflict. A later separately reviewed attempt may prepare against the new state. Do not automatically retry a conflict with a refreshed stamp.
7. Identity generates and consumes an internal password-reset token in the CLI process. Identity rotates the security stamp; the service sets `MustChangePassword=true`, a fresh 24-hour temporary-password expiry and current-stamp CLI onboarding authorization. The same transaction inserts `PlatformPasswordRecoveryAudits` with target user ID, OS operator, approval reference and UTC timestamp. Passwords, reset tokens and security stamps are not audit fields. It leaves email confirmation, role assignments, lockout counters, active state, bootstrap marker and tenant/financial data unchanged.
8. Audit/token/password persistence failure rolls the entire transaction back. An uncertain commit acknowledgement requires operator inspection of audit/account state before another attempt; never delete the bootstrap marker. Audit records have no cascading user foreign key and survive account deletion. There is no HTTP recovery endpoint for this operation.
9. All application cookies now have their security stamp validated against Identity on **every request**, not just platform requests or periodically. This includes tickets predating a platform-role claim. Onboarding cookies retain their separate per-request validation. Old tickets fail on their next request and remain invalid after the new mandatory password change is completed. Already-running requests cannot be recalled. This adds a database validation per authenticated cookie request.
10. Sign in with the temporary password at `/Platform/Account/Login` and complete mandatory password change within 24 hours. SMTP, mailbox access and email confirmation are not required for this CLI-authorized path. Email remains unconfirmed unless the owner separately completes mailbox verification.

### Temporary-password handling

Use a strong randomly generated temporary password and a trusted terminal with input recording disabled. Hidden input prevents echo, not observation by compromised terminals, SSH session-recording software, OS administrators or debuggers. Do not put the password in shell history, a script, an environment variable, a ticket, chat, email or a screenshot. If another person must receive it, use the organization's approved confidential out-of-band channel, then have them immediately choose a different password in the web flow. Clear clipboard/password-manager temporary entries according to policy after use. Secrets necessarily exist briefly in process memory; no secure-erasure claim is made for managed strings. If disclosure is suspected, perform a newly approved CLI reset and treat the old temporary password as compromised.

### Additive recovery-audit migration

`20260920163502_AddPlatformPasswordRecoveryAudit` adds only the global `PlatformPasswordRecoveryAudits` table and an index on `(UserId, OccurredAtUtc)`. It does not modify the committed bootstrap migration, existing accounts, bootstrap dispositions or tenant records. Its `Down` refuses destructive audit-history removal. CLI authorization uses existing Identity token storage and requires no user-column migration. Apply the additive migration only through separately approved deployment; this implementation was verified only in disposable test databases.

## 2FA and other limits

A complete platform enrollment/challenge/recovery flow is not implemented. Platform accounts with `TwoFactorEnabled=true` are denied both full platform access and onboarding; the code does not disable an existing user's 2FA or bypass Identity to sign them in. Enable 2FA only as part of a separately reviewed complete flow.

The existing application password policy is reused (currently relatively permissive). Use a strong, unique operator password; any stronger platform-only minimum or breached-password screening needs a separate policy decision rather than silently changing tenant password rules.

CLI logging providers are disabled and only controlled status/error messages are printed. Temporary credentials are necessarily present briefly in process memory for Identity hashing; no claim is made to protect them from an administrator inspecting process memory. Use a trusted terminal without input recording, and distribute credentials only through an approved secure channel.

## Email verification

1. If optional SMTP is configured, visit `/Platform/Account/ForgotPassword`, enter the administrator email, and select **Повторить подтверждение email**. CLI provisioning itself no longer sends an email.
2. Open the HTTPS link in a trusted browser with JavaScript enabled. Press **Подтвердить мой email**. GET does not confirm anything; the modifying POST requires antiforgery.
3. Identity validates its email-confirmation token. Only a currently active, unlocked platform-role holder with a unique email can confirm. The confirmation changes only `EmailConfirmed`; it does not sign in, grant roles, or clear the initial-password requirement.
4. The page redirects to password recovery. After confirmation, either finish the normal temporary-password flow or request a password-reset email. Email confirmation uses the existing Identity email provider (default lifetime one day), separate from the 15-minute recovery provider.

If SMTP is unavailable, CLI temporary-password onboarding still works within its 24-hour window; expired or forgotten passwords can be recovered through SSH. Email recovery stays unavailable until mailbox ownership has actually been proven. Legacy accounts with no email need approved operator-assisted email enrollment if email recovery is wanted; this feature does not silently attach the intended Gmail address to an existing account.

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

## Production configuration and optional email

Use environment variables or a deployment secret store for service configuration, never tracked secrets or the administrator's personal Gmail password. These configuration variables are **not** accepted as sources of administrator passwords. Set the database/HTTPS/key configuration for the Web service and approved CLI process. Leave all `PlatformSmtp` settings unset if email delivery is not wanted; provisioning and SSH reset still work. `PlatformRecovery__BaseUrl` and all SMTP entries below are required **only for optional email functionality**:

| Environment variable | Required value / purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Production` (no automatic database migration) |
| `ConnectionStrings__DefaultConnection` | Approved SQL Server connection from the existing secret store |
| `PlatformRecovery__BaseUrl` | Email-only: actual public HTTPS origin, e.g. `https://portal.example.org` (replace the example; no path/query/fragment) |
| `PlatformSmtp__Host` | Email-only: transactional provider's authenticated STARTTLS SMTP hostname |
| `PlatformSmtp__Port` | Email-only: normally `587`, not implicit-TLS port 465 |
| `PlatformSmtp__UserName` | Email-only: provider-issued SMTP username |
| `PlatformSmtp__Password` | Email-only: provider-issued SMTP secret, injected securely; never the recipient's Gmail password |
| `PlatformSmtp__From` | Email-only: sender address authorized and verified by that provider |
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
- If the initial password expired, use the SSH reset command. Alternatively, if optional email is configured and ownership can be confirmed, use email recovery. Neither path requires the old temporary password.
- If a link displays an empty/invalid form, enable JavaScript and reopen the original email link. Check whether mail software stripped the fragment. Do not append the token to an HTTP query string.
- If no email arrives, check sender configuration, provider delivery dashboards and safe generic application warnings. Do not troubleshoot by printing credentials, tokens or message bodies. Resolve duplicate emails, revoked roles, disabled/locked accounts or legacy missing-email enrollment through the authorized operator process, not through public recovery.

## Emergency recovery: operator-controlled, no second bootstrap

The approved `reset-platform-admin-password` command recovers credentials for an existing eligible platform account even when email is unavailable. It is **not** a general identity/role-restoration command. Reviewed legacy initialization is only for a historically verified first administrator and cannot be used to repair or replace a previous one. Deleted accounts/roles, intentional access revocation or disabled/locked accounts require separate incident review; this reset command will not bypass those states. If historical eligibility cannot be established, stop and preserve evidence; an approval reference alone does not justify initialization. Do not remove `PlatformBootstrapStates`, edit old claims, change `Consumed` to `LegacyReviewRequired`, rerun either provisioning command, silently create a replacement administrator, or restore a whole production database merely to reset one account.

1. Open an audited incident/change request. Independently verify the operator and administrator through an approved out-of-band channel; require a second authorized approver. Explicitly distinguish accidental loss from intentional disabling/revocation, which must remain in force unless separately reversed by its authority.
2. Preserve a protected backup and audit evidence. Identify the exact existing Identity user ID, current global roles, email/confirmation state, bootstrap marker, and relevant history. Use an isolated restored copy for planning; no modification based only on a claimed email address or tenant-administrator permission.
3. Prefer restoring access to the original mailbox through its provider. If a mailbox change is approved, an independently reviewed maintenance operation must target only that existing user, use Identity APIs to set the new address **unconfirmed**, rotate the security stamp, invalidate outstanding tokens/sessions, and require fresh mailbox confirmation and a new password. Do not mark a replacement mailbox confirmed merely because an operator entered it.
4. If a global role was accidentally deleted/revoked, require explicit separate approval to restore only the approved role assignment on that existing account. Email/password recovery itself must never restore it. Preserve the consumed marker and all tenant data. An intentionally disabled or revoked administrator is not eligible for routine credential recovery.
5. If the account was deleted or legacy bootstrap history is ambiguous, stop. Obtain a separately reviewed identity-restoration plan from protected backups/audit evidence, including conflict and financial-reference checks. Do not silently create a replacement identity or reopen bootstrap.
6. Any future maintenance command requires approval before implementation. Its design must be operator-only, require interactive target and exact user-ID confirmation, support a read-only inspection phase, make narrowly scoped transactional Identity changes, retain the permanent marker, log approver/change IDs without secrets, and refuse tenant-account promotion. No general-purpose Web recovery backdoor or bootstrap override is acceptable.
7. After the approved repair, independently verify mailbox ownership, reset the password, verify that old cookies/tokens fail, review role/tenant diffs, and record the result. Remove temporary operator access and rotate exposed operational credentials if compromise is suspected.

## Verification and release gate

Use `dotnet test tests/Neftyanik.Portal.Infrastructure.Tests` and `dotnet test tests/Neftyanik.Portal.Web.Tests`; fixtures use unique disposable LocalDB databases or in-memory SQLite. Use `node --test tests/browser/platform-recovery.test.cjs` for the fragment-handling unit tests. These script tests execute the real JavaScript in an isolated DOM/history fixture; they do not constitute a live-browser or SMTP-provider acceptance test.

Build `Neftyanik.Portal.sln` in Release with `--artifacts-path artifacts/ssh-release`, run the EF pending-model-change check using the design-time factory with an inert connection configuration, and run `git diff --check`. Do not run `database update`, normal Development startup, bootstrap, legacy initialization or password reset against an existing development/production database as a verification step. Preserve the separate smoke host. Regression coverage includes tenant/marker snapshots, concurrent SQL resets, audit/token failure rollback, no-SMTP web onboarding, immediate old-cookie rejection even after onboarding completes, exact-login ambiguity, disabled/locked/revoked accounts, and existing optional email confirmation/recovery.

Before deployment approval: review target bootstrap history, back up, approve the additive audit migration (and the bootstrap migration if not yet applied), provision persistent keys, configure HTTPS/ingress limits, drain old binaries, and approve an operator/browser acceptance test. SMTP and a real delivery test are needed only if enabling optional email recovery. Passing automated tests alone does not authorize deployment.
