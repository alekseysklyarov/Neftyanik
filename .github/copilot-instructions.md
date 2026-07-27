# GitHub Copilot Instructions

## Project

This repository contains the Neftyanik Gardening Association Portal.

The application manages:

* members;
* land plots;
* electricity meters and readings;
* electricity charges;
* annual membership fees;
* payments and payment allocation;
* member debt;
* association expenses;
* news;
* documents;
* financial reports.

Read the following documents before making architectural changes:

* `docs/SPECIFICATION_RU.md`
* `docs/SPECIFICATION_EN.md`
* `docs/DATABASE_DESIGN.md`

## Technology stack

Use:

* .NET 8;
* ASP.NET Core MVC;
* C#;
* Microsoft SQL Server;
* Entity Framework Core;
* ASP.NET Core Identity;
* Bootstrap 5;
* nullable reference types;
* dependency injection;
* asynchronous APIs.

Do not introduce a JavaScript SPA framework unless explicitly requested.

## Architecture

Use the existing solution structure:

```text
Neftyanik.Portal.Domain
Neftyanik.Portal.Application
Neftyanik.Portal.Infrastructure
Neftyanik.Portal.Web
```

Responsibilities:

### Domain

Contains:

* domain entities;
* enums;
* domain rules;
* value objects when useful.

Domain must not reference Infrastructure or Web.

### Application

Contains:

* service interfaces;
* use cases;
* DTOs;
* view-independent validation;
* application business logic.

Application may reference Domain.

### Infrastructure

Contains:

* EF Core DbContext;
* entity configurations;
* migrations;
* repository implementations;
* file storage implementations;
* external service implementations.

Infrastructure may reference Domain and Application.

### Web

Contains:

* MVC controllers;
* Razor views;
* view models;
* authentication configuration;
* dependency registration;
* static files.

Web may reference Application and Infrastructure.

## Coding rules

Use:

* file-scoped namespaces;
* `async` and `await` for database and I/O operations;
* `CancellationToken` in service and repository methods;
* `AsNoTracking()` for read-only EF Core queries;
* strongly typed view models;
* data annotations only for simple UI validation;
* Fluent API for EF Core database configuration;
* decimal values for money and meter readings;
* `DateTimeOffset` for audit timestamps;
* `DateOnly` for accounting dates and meter reading dates.

Avoid:

* business logic in MVC controllers;
* direct use of `ApplicationDbContext` in controllers;
* synchronous EF Core calls;
* returning EF entities directly to editable views;
* using `double` or `float` for money;
* storing calculated debt as a mutable user field;
* hard-coded passwords or secrets;
* deleting financial records physically;
* generic repository abstractions that only duplicate DbSet methods;
* large controllers;
* static service locators.

## Controllers

Controllers should:

1. validate the HTTP request;
2. call an application service;
3. map the result to a view model;
4. select a view or redirect.

Controllers should not:

* calculate electricity charges;
* calculate debts;
* allocate payments;
* write complex EF Core queries;
* make authorization decisions based only on form values.

Use authorization attributes and policy-based authorization.

Examples:

```csharp
[Authorize(Roles = RoleNames.Accountant)]
```

Prefer constants instead of string literals for role names.

## Security

Use ASP.NET Core Identity.

Roles:

```text
Administrator
Accountant
Member
```

Security rules:

* a Member may access only their own plots, meters, readings, charges and payments;
* Accountant may manage financial records and readings;
* Administrator may manage users, roles and global settings;
* ownership checks must be performed on the server;
* never trust a user ID, owner ID or amount received from a form;
* use antiforgery validation for modifying MVC actions;
* validate uploaded file type, extension and size;
* do not store secrets in `appsettings.json`;
* use User Secrets for local development;
* use environment variables in production.

## Database rules

Use EF Core migrations.

Place migrations in the Infrastructure project.

Use separate `IEntityTypeConfiguration<TEntity>` classes for entity configuration.

Create indexes and constraints described in:

```text
docs/DATABASE_DESIGN.md
```

Configure financial columns as:

```csharp
.HasPrecision(18, 2)
```

Configure meter readings as:

```csharp
.HasPrecision(18, 3)
```

Use explicit delete behavior.

Do not cascade-delete:

* users with financial history;
* plots with charges;
* meters with readings;
* charges with payment allocations;
* payments with allocations;
* expenses.

Prefer `DeleteBehavior.Restrict` for accounting relationships.

## Accounting rules

Do not store a mutable `Debt` property on the user.

Calculate debt from:

```text
charges
minus payment allocations
minus unallocated payment advance
```

A payment and its allocations must be saved in one database transaction.

An electricity charge and the related reading status update must be saved in one database transaction.

Cancelled financial records must remain in the database.

Use explicit status fields or reversal records.

Do not recalculate existing charges when a tariff changes.

## Electricity rules

Default individual tariff:

```text
5.00 UAH per kWh
```

Support tariff history.

A meter can be:

```text
Individual
Common
```

A meter tariff mode can be:

```text
SingleRate
DayNight
```

Most individual meters are single-rate.

The common association meter is day/night.

One individual meter may serve multiple plots belonging to one owner.

A plot must not have more than one active individual meter assignment at the same time.

Meter readings must not decrease.

Only approved readings may create charges.

Prevent duplicate active readings for the same meter and reading date.

## Membership fee rules

The initial annual membership fee is:

```text
500.00 UAH per active plot
```

A member owning three active plots receives three separate annual charges.

Keep a yearly membership fee rate history.

Prevent duplicate annual membership fee charges for the same plot and year.

## Payments

A payment can be allocated to multiple charges.

A charge can be paid by multiple payments.

Use a `PaymentAllocation` entity.

The sum of payment allocations must not exceed the payment amount.

The sum allocated to a charge must not exceed its remaining balance.

Unallocated payment money is an advance.

Automatic allocation should normally close the oldest charges first.

## Validation and errors

Use domain-specific exceptions or result types for expected business failures.

Examples:

* meter reading is lower than the previous reading;
* annual charge already exists;
* payment amount is invalid;
* payment allocation exceeds the available amount;
* member attempts to access another member’s data.

Do not expose exception details or database errors to end users.

Log unexpected exceptions.

## UI

Use Bootstrap 5.

The interface language is initially Russian.

Use Ukrainian currency formatting where appropriate:

```text
500,00 грн
```

The application should work well on phones.

Use:

* accessible form labels;
* validation summaries;
* clear confirmation dialogs for important operations;
* pagination for large lists;
* filters for accounting lists;
* Post/Redirect/Get after successful form submissions.

## Testing

Write unit tests for business rules, including:

* electricity consumption calculation;
* single-rate charge calculation;
* day/night charge calculation;
* annual membership fee generation;
* duplicate annual charge prevention;
* payment allocation;
* debt calculation;
* advance calculation;
* meter reading validation;
* ownership authorization.

Write integration tests for important EF Core constraints and transactions.

Use descriptive test names.

Example:

```csharp
CreateAnnualChargesAsync_CreatesOneChargeForEachActivePlot()
```

## Change discipline

For each requested task:

1. inspect existing code first;
2. preserve the current architecture;
3. implement the smallest complete change;
4. add or update tests;
5. verify compilation;
6. report changed files;
7. do not rewrite unrelated files;
8. do not invent requirements that conflict with the specification.

When a requirement is unclear, prefer the rules in `docs/DATABASE_DESIGN.md`.
