# Neftyanik Portal

## Документация

- `docs/PORTAL_DOCUMENTATION_RU.md` — рабочая документация по разделам портала и основным сценариям.
- `docs/PORTAL_USER_QUICK_GUIDE_RU.md` — короткая инструкция для пользователей, готовая для размещения на сайте.
- `docs/DEVELOPMENT_AUTH_BOOTSTRAP.md` — локальное создание администратора.
- `docs/DATABASE_DESIGN.md` — актуальная структура базы данных и правила схемы.

## Текущая схема БД и миграции EF Core

- В проекте поддерживается одна актуальная миграция EF Core: `20260729185604_InitialCleanSchema`.
- Файлы миграции находятся в стандартной папке EF Core: `src/Neftyanik.Portal.Infrastructure/Migrations`.
- Снимок модели находится в `src/Neftyanik.Portal.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`.
- Устаревшая история миграций из `src/Neftyanik.Portal.Infrastructure/Data/Migrations` удалена.

### Локальная база разработки

- SQL Server: `localhost\SQLEXPRESS`
- База данных: `NeftyanikPortalDb`
- Строка подключения для разработки хранится в `src/Neftyanik.Portal.Web/appsettings.Development.json`.

### Полезные команды

```powershell
dotnet ef migrations list --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext
dotnet ef database update --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext
dotnet ef migrations script 0 InitialCleanSchema --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext -o docs/sql/NeftyanikPortal.sql
dotnet ef migrations script --idempotent --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext -o docs/sql/NeftyanikPortal.Idempotent.sql
```

## Текущая архитектура электроэнергии

- `AssociationElectricityReading` и `AssociationElectricityTariff` используются только для общего двухзонного счётчика товарищества.
- `MemberElectricityMeter`, `MemberElectricityMeterPlot`, `MemberElectricityReading` и `MemberElectricityTariff` используются для счётчиков членов товарищества.
- Начисления членов сохраняются через связь `MemberElectricityReading -> Charge`.
- Расходы по общему счётчику сохраняются через связь `AssociationElectricityReading -> Expense`.
- В исходной модели, миграции и SQL-скриптах отсутствуют устаревшие сущности и таблицы `ElectricityMeters`, `MeterPlots`, `MeterReadings`, `ElectricityReadings`, `ElectricityTariffs`.

## SQL-скрипты схемы

- Полный скрипт создания: `docs/sql/NeftyanikPortal.sql`
- Идемпотентный скрипт: `docs/sql/NeftyanikPortal.Idempotent.sql`

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
