# Neftyanik Portal

## Документация

- `docs/PORTAL_DOCUMENTATION_RU.md` — рабочая документация по разделам портала и основным сценариям.
- `docs/PORTAL_USER_QUICK_GUIDE_RU.md` — короткая инструкция для пользователей, готовая для размещения на сайте.
- `docs/DEVELOPMENT_AUTH_BOOTSTRAP.md` — локальное создание администратора.

## Local administrator bootstrap

Use temporary PowerShell environment variables and run the explicit command:

```powershell
$env:NEFTYANIK_ADMIN_EMAIL = "admin@example.local"
$env:NEFTYANIK_ADMIN_PASSWORD = "<enter-a-strong-local-password>"
$env:NEFTYANIK_ADMIN_NAME = "Local Administrator"

dotnet run --project src/Neftyanik.Portal.Web -- create-admin

Remove-Item Env:NEFTYANIK_ADMIN_EMAIL
Remove-Item Env:NEFTYANIK_ADMIN_PASSWORD
Remove-Item Env:NEFTYANIK_ADMIN_NAME -ErrorAction SilentlyContinue
```
