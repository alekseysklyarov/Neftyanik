# Database design

## Current EF Core migration baseline

- The only current EF Core migration is `20260729185604_InitialCleanSchema`.
- Migration files are stored in `src/Neftyanik.Portal.Infrastructure/Migrations`.
- The current model snapshot is `src/Neftyanik.Portal.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`.

## Local development database

- SQL Server: `localhost\SQLEXPRESS`
- Database: `NeftyanikPortalDb`
- Apply the current schema with:

```powershell
dotnet ef database update --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext
```

- Generate SQL scripts with:

```powershell
dotnet ef migrations script 0 InitialCleanSchema --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext -o docs/sql/NeftyanikPortal.sql
dotnet ef migrations script --idempotent --project src/Neftyanik.Portal.Infrastructure --startup-project src/Neftyanik.Portal.Web --context ApplicationDbContext -o docs/sql/NeftyanikPortal.Idempotent.sql
```

## Identity and users

- `AspNetUsers`
- `AspNetRoles`
- `AspNetUserRoles`
- `AspNetUserClaims`
- `AspNetUserLogins`
- `AspNetUserTokens`
- `AspNetRoleClaims`
- `Members`

## Plots and ownership

- `Plots`
- `PlotOwnerships`
- `PlotOwnershipHistories`

## Electricity

- `AssociationElectricityReadings`
- `AssociationElectricityTariffs`
- `MemberElectricityMeters`
- `MemberElectricityMeterPlots`
- `MemberElectricityReadings`
- `MemberElectricityTariffs`

Legacy electricity tables are not part of the current schema:

- `ElectricityMeters`
- `MeterPlots`
- `MeterReadings`
- `ElectricityReadings`
- `ElectricityTariffs`

## Finance and membership

- `MembershipFeeRates`
- `ChargeTypes`
- `Charges`
- `Payments`
- `PaymentAllocations`
- `ExpenseCategories`
- `Expenses`

## Content and system

- `NewsArticles`
- `AssociationDocuments`
- `SystemSettings`
- `AuditLogs`

## Electricity rules

- the association meter is day/night and uses only `AssociationElectricityTariffs`;
- member meters are single-rate and use only `MemberElectricityTariffs`;
- one member meter may serve multiple plots and has one billing plot;
- member electricity charges are linked through `MemberElectricityReading -> Charge`;
- shared supplier expenses are linked through `AssociationElectricityReading -> Expense`.

## SQL scripts

- `docs/sql/NeftyanikPortal.sql`
- `docs/sql/NeftyanikPortal.Idempotent.sql`
