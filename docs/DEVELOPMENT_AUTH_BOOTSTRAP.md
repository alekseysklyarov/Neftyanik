# Локальная инициализация учетной записи администратора
Это руководство показывает безопасный способ временно подготовить учетные данные администратора для локальной разработки.

Локальные переменные окружения подходят только для разработки и не предназначены для передачи в продакшен или через веб-интерфейс.

Команды для PowerShell:

```powershell
$env:NEFTYANIK_ADMIN_EMAIL = "admin@example.local"
$env:NEFTYANIK_ADMIN_PASSWORD = "<enter-a-strong-local-password>"
$env:NEFTYANIK_ADMIN_NAME = "Local Administrator"

dotnet run --project src/Neftyanik.Portal.Web -- create-admin

Remove-Item Env:NEFTYANIK_ADMIN_EMAIL
Remove-Item Env:NEFTYANIK_ADMIN_PASSWORD
Remove-Item Env:NEFTYANIK_ADMIN_NAME -ErrorAction SilentlyContinue
```

Если необходимо не создавать, а назначить роль администратора уже существующему пользователю, используйте:

```powershell
dotnet run --project src/Neftyanik.Portal.Web -- create-admin --allow-existing-user-role-assignment
```
