# Neftyanik Portal

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
