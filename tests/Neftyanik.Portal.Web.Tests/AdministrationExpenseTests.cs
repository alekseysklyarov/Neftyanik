#if WEB_TESTS
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using ExpenseCreateModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.CreateModel;
using ExpenseCancelModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.CancelModel;
using ExpenseDetailsModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.DetailsModel;
using ExpenseEditModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.EditModel;
using ElectricityExpenseIndexModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.Electricity.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationExpenseTests
{
    [Fact]
    public async Task GetElectricityExpensePage_WithoutHistory_ShowsInitialReadingForm()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = TestPageModelContext.CreatePageContext("accountant-user", "accountant").HttpContext
        };

        var service = new AssociationElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new ElectricityExpenseIndexModel(dbContext, service, userManager);

        await model.OnGetAsync(CancellationToken.None);

        Assert.False(model.HasHistory);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), model.Input.ReadingDate);
    }

    [Fact]
    public async Task OnGetExpenseDetailsAsync_UsesFinancialHistoryAndPreservesLegacyRowsNewestFirst()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        dbContext.ExpenseCategories.AddRange(
            new ExpenseCategory { Id = 2202, Name = "Category 2", IsActive = true },
            new ExpenseCategory { Id = 2203, Name = "Category 3", IsActive = true });
        dbContext.Expenses.AddRange(
            new Expense
            {
                Id = 201,
                ExpenseCategoryId = 2202,
                ExpenseDate = new DateOnly(2026, 8, 1),
                Amount = 1000m,
                Description = "Основной расход",
                CreatedByUserId = "user-1",
                CreatedAt = new DateTimeOffset(2026, 8, 1, 9, 0, 0, TimeSpan.Zero)
            },
            new Expense
            {
                Id = 202,
                ExpenseCategoryId = 2202,
                ExpenseDate = new DateOnly(2026, 8, 2),
                Amount = 500m,
                Description = "Чужой расход",
                CreatedByUserId = "user-1",
                CreatedAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.Zero)
            });

        dbContext.FinancialAuditLogs.AddRange(
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
                UserId = "user-1",
                UserName = "expense-user-1",
                Action = FinancialAuditLogActions.Created,
                EntityType = nameof(Expense),
                EntityId = "201",
                Description = "Создан расход #201.",
                NewValuesJson = "{\"ExpenseDate\":\"2026-08-01\",\"Amount\":1000,\"ExpenseCategoryId\":2202,\"Description\":\"Основной расход\"}"
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc),
                UserId = "user-1",
                UserName = "expense-user-1",
                Action = FinancialAuditLogActions.Updated,
                EntityType = nameof(Expense),
                EntityId = "201",
                Description = "Обновлен расход #201.",
                OldValuesJson = "{\"ExpenseDate\":\"2026-08-01\",\"Amount\":1000,\"ExpenseCategoryId\":2202}",
                NewValuesJson = "{\"ExpenseDate\":\"2026-08-02\",\"Amount\":1200,\"ExpenseCategoryId\":2203}"
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 3, 11, 0, 0, DateTimeKind.Utc),
                UserId = "user-1",
                UserName = "expense-user-1",
                Action = FinancialAuditLogActions.Cancelled,
                EntityType = nameof(Expense),
                EntityId = "201",
                Description = "Отменен расход #201.",
                OldValuesJson = "{\"IsCancelled\":false}",
                NewValuesJson = "{\"IsCancelled\":true,\"CancellationReason\":\"Ошибка\"}"
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc),
                UserId = "user-1",
                UserName = "expense-user-1",
                Action = FinancialAuditLogActions.Restored,
                EntityType = nameof(Expense),
                EntityId = "201",
                Description = "Восстановлен расход #201.",
                OldValuesJson = "{\"IsCancelled\":true}",
                NewValuesJson = "{\"IsCancelled\":false}"
            },
            new FinancialAuditLog
            {
                CreatedAtUtc = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc),
                UserId = "user-1",
                UserName = "expense-user-1",
                Action = FinancialAuditLogActions.Updated,
                EntityType = nameof(Expense),
                EntityId = "202",
                Description = "Обновлен расход #202.",
                OldValuesJson = "{\"Amount\":500}",
                NewValuesJson = "{\"Amount\":700}"
            });

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = "user-1",
            Action = "Edit",
            EntityType = nameof(Expense),
            EntityId = "201",
            NewValues = "{\"Amount\":900,\"Description\":\"Старое изменение\"}",
            CreatedAt = new DateTimeOffset(2026, 7, 31, 8, 0, 0, TimeSpan.Zero)
        });

        await dbContext.SaveChangesAsync();

        var model = new ExpenseDetailsModel(dbContext);

        var result = await model.OnGetAsync(201, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(5, model.History.Count);
        Assert.Collection(model.History,
            item => Assert.Equal(FinancialAuditLogActions.Restored, item.Action),
            item => Assert.Equal(FinancialAuditLogActions.Cancelled, item.Action),
            item => Assert.Equal(FinancialAuditLogActions.Updated, item.Action),
            item => Assert.Equal(FinancialAuditLogActions.Created, item.Action),
            item => Assert.Equal(FinancialAuditLogActions.Updated, item.Action));
        Assert.Contains(model.History, item => item.UserName == "expense-user-1");
        Assert.Contains(model.History, item => item.UserName == "Expense User");
        Assert.DoesNotContain(model.History, item => item.Description.Contains("#202", StringComparison.Ordinal));

        var updatedEntry = model.History.Single(item => item.Action == FinancialAuditLogActions.Updated
            && item.Changes.Any(change => change.Label == "Сумма" && change.OldValue == "1000" && change.NewValue == "1200"));
        Assert.Contains(updatedEntry.Changes, item => item.Label == "Сумма" && item.OldValue == "1000" && item.NewValue == "1200");
        Assert.Contains(updatedEntry.Changes, item => item.Label == "Дата" && item.OldValue == "01.08.2026" && item.NewValue == "02.08.2026");

        var cancelledEntry = model.History.Single(item => item.Action == FinancialAuditLogActions.Cancelled);
        Assert.Contains(cancelledEntry.Changes, item => item.Label == "Статус отмены" && item.OldValue == "Нет" && item.NewValue == "Да");

        var legacyEntry = model.History.Last();
        Assert.Equal(FinancialAuditLogActions.Updated, legacyEntry.Action);
        Assert.Contains(legacyEntry.Changes, item => item.Label == "Сумма" && item.NewValue == "900");
    }

    [Fact]
    public async Task CreateAssociationElectricityReadingAsync_SavesReading_AndCreateExpenseAsync_CreatesSupplierExpense()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "accountant-user",
            UserName = "accountant",
            NormalizedUserName = "ACCOUNTANT",
            FirstName = "Test",
            LastName = "Accountant",
            DisplayName = "Test Accountant",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        dbContext.AssociationElectricityTariffs.Add(new AssociationElectricityTariff
        {
            EffectiveFrom = new DateOnly(2026, 1, 1),
            DayRate = 4.50m,
            NightRate = 2.25m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "accountant-user"
        });

        await dbContext.SaveChangesAsync();

        var service = new AssociationElectricityService(dbContext);

        var initialResult = await service.CreateInitialReadingAsync(
            new CreateAssociationElectricityInitialReadingRequest(
                new DateOnly(2026, 1, 1),
                1000m,
                500m,
                "accountant-user"),
            CancellationToken.None);

        Assert.True(initialResult.Succeeded);

        var readingResult = await service.CreateReadingAsync(
            new CreateAssociationElectricityReadingRequest(
                new DateOnly(2026, 2, 1),
                1100m,
                540m,
                "accountant-user"),
            CancellationToken.None);

        Assert.True(readingResult.Succeeded);
        Assert.Equal(540m, readingResult.TotalAmount);

        Assert.Empty(await dbContext.Expenses.AsNoTracking().ToListAsync());

        var expenseResult = await service.CreateExpenseAsync(
            new CreateAssociationElectricityExpenseRequest(readingResult.ReadingId!.Value, "accountant-user"),
            CancellationToken.None);

        Assert.True(expenseResult.Succeeded);
        Assert.Equal(540m, expenseResult.TotalAmount);

        var expense = await dbContext.Expenses
            .Include(item => item.AssociationElectricityReading)
            .SingleAsync();

        Assert.Equal(1, expense.ExpenseCategoryId);
        Assert.Equal(new DateOnly(2026, 2, 1), expense.ExpenseDate);
        Assert.Equal(540m, expense.Amount);
        Assert.Equal("accountant-user", expense.CreatedByUserId);
        Assert.Equal("Поставщик электроэнергии", expense.Payee);
        Assert.NotNull(expense.AssociationElectricityReading);
        Assert.Equal(100m, expense.AssociationElectricityReading!.DayConsumption);
        Assert.Equal(40m, expense.AssociationElectricityReading.NightConsumption);
        Assert.Equal(140m, expense.AssociationElectricityReading.TotalConsumption);

        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(Expense), auditEntry.EntityType);
        Assert.Equal(expense.Id.ToString(), auditEntry.EntityId);
        Assert.Contains("\"AssociationElectricityReadingId\":" + expense.AssociationElectricityReadingId, auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Empty(await dbContext.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateAssociationElectricityReadingAsync_ReturnsValidationError_WhenAssociationTariffIsMissing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "accountant-user",
            UserName = "accountant",
            NormalizedUserName = "ACCOUNTANT",
            FirstName = "Test",
            LastName = "Accountant",
            DisplayName = "Test Accountant",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        await dbContext.SaveChangesAsync();

        var service = new AssociationElectricityService(dbContext);

        var initialResult = await service.CreateInitialReadingAsync(
            new CreateAssociationElectricityInitialReadingRequest(
                new DateOnly(2026, 1, 1),
                1000m,
                500m,
                "accountant-user"),
            CancellationToken.None);

        Assert.True(initialResult.Succeeded);

        var readingResult = await service.CreateReadingAsync(
            new CreateAssociationElectricityReadingRequest(
                new DateOnly(2026, 2, 1),
                1100m,
                540m,
                "accountant-user"),
            CancellationToken.None);

        Assert.False(readingResult.Succeeded);
        Assert.Equal("Для указанной даты не найден тариф поставщика.", readingResult.ErrorMessage);
    }

    [Fact]
    public async Task CreateAssociationElectricityReadingAsync_ReturnsValidationError_WhenReadingsAreNotWholeNumbers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "accountant-user",
            UserName = "accountant",
            NormalizedUserName = "ACCOUNTANT",
            FirstName = "Test",
            LastName = "Accountant",
            DisplayName = "Test Accountant",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        dbContext.AssociationElectricityTariffs.Add(new AssociationElectricityTariff
        {
            EffectiveFrom = new DateOnly(2026, 1, 1),
            DayRate = 4.50m,
            NightRate = 2.25m,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = "accountant-user"
        });

        await dbContext.SaveChangesAsync();

        var service = new AssociationElectricityService(dbContext);

        var initialResult = await service.CreateInitialReadingAsync(
            new CreateAssociationElectricityInitialReadingRequest(
                new DateOnly(2026, 1, 1),
                1000m,
                500m,
                "accountant-user"),
            CancellationToken.None);

        Assert.True(initialResult.Succeeded);

        var readingResult = await service.CreateReadingAsync(
            new CreateAssociationElectricityReadingRequest(
                new DateOnly(2026, 2, 1),
                1100.5m,
                540m,
                "accountant-user"),
            CancellationToken.None);

        Assert.False(readingResult.Succeeded);
        Assert.Equal("Дневное показание должно быть целым числом.", readingResult.ErrorMessage);
    }

    [Fact]
    public async Task OnPostEditExpenseAsync_UpdatesManualExpense()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        dbContext.Expenses.Add(new Expense
        {
            Id = 101,
            ExpenseCategoryId = 2,
            ExpenseDate = new DateOnly(2026, 1, 10),
            Amount = 500m,
            Description = "Исходный расход",
            Payee = "Поставщик",
            DocumentNumber = "DOC-1",
            CreatedByUserId = "user-1"
        });

        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor();
        var model = new ExpenseEditModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.ExpenseInputModel
            {
                ExpenseCategoryId = 3,
                ExpenseDate = new DateOnly(2026, 1, 11),
                Amount = 750m,
                Description = "Обновленный расход",
                Payee = "Новый поставщик",
                DocumentNumber = "DOC-2"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(101, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Finance/Expenses/Details", redirect.PageName);

        var expense = await dbContext.Expenses.SingleAsync();
        Assert.Equal(3, expense.ExpenseCategoryId);
        Assert.Equal(new DateOnly(2026, 1, 11), expense.ExpenseDate);
        Assert.Equal(750m, expense.Amount);
        Assert.Equal("Обновленный расход", expense.Description);
        Assert.Equal("Новый поставщик", expense.Payee);
        Assert.Equal("DOC-2", expense.DocumentNumber);

        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Updated, auditEntry.Action);
        Assert.Equal(nameof(Expense), auditEntry.EntityType);
        Assert.Equal("user-1", auditEntry.UserId);
        Assert.Equal("expense-user-1", auditEntry.UserName);
        Assert.Contains("\"ExpenseCategoryId\":2", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"ExpenseCategoryId\":3", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"Amount\":500", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"Amount\":750", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Empty(await dbContext.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostEditExpenseAsync_WhenMeaningfulValuesDidNotChange_DoesNotCreateAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        dbContext.Expenses.Add(new Expense
        {
            Id = 103,
            ExpenseCategoryId = 2,
            ExpenseDate = new DateOnly(2026, 1, 10),
            Amount = 500m,
            Description = "Исходный расход",
            Payee = "Поставщик",
            DocumentNumber = "DOC-1",
            CreatedByUserId = "user-1"
        });

        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor();
        var model = new ExpenseEditModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.ExpenseInputModel
            {
                ExpenseCategoryId = 2,
                ExpenseDate = new DateOnly(2026, 1, 10),
                Amount = 500m,
                Description = "Исходный расход",
                Payee = "Поставщик",
                DocumentNumber = "DOC-1"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(103, CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCancelExpenseAsync_TogglesExpenseStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });

        dbContext.Expenses.Add(new Expense
        {
            Id = 102,
            ExpenseCategoryId = 2,
            ExpenseDate = new DateOnly(2026, 1, 12),
            Amount = 300m,
            Description = "Расход для отмены",
            CreatedByUserId = "user-1"
        });
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor();
        var model = new ExpenseCancelModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            Input = new ExpenseCancelModel.InputModel
            {
                CancellationReason = "Ошибка в документе"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var firstResult = await model.OnPostAsync(102, CancellationToken.None);
        var firstRedirect = Assert.IsType<RedirectToPageResult>(firstResult);
        Assert.Equal("/Administration/Finance/Expenses/Details", firstRedirect.PageName);
        Assert.True((await dbContext.Expenses.SingleAsync()).IsCancelled);
        Assert.Equal("Ошибка в документе", (await dbContext.Expenses.SingleAsync()).CancellationReason);

        var auditEntriesAfterCancel = await dbContext.FinancialAuditLogs.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Single(auditEntriesAfterCancel);
        Assert.Equal(FinancialAuditLogActions.Cancelled, auditEntriesAfterCancel[0].Action);
        Assert.Equal(nameof(Expense), auditEntriesAfterCancel[0].EntityType);
        Assert.Contains("\"IsCancelled\":false", auditEntriesAfterCancel[0].OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"IsCancelled\":true", auditEntriesAfterCancel[0].NewValuesJson, StringComparison.Ordinal);

        var secondResult = await model.OnPostAsync(102, CancellationToken.None);
        var secondRedirect = Assert.IsType<RedirectToPageResult>(secondResult);
        Assert.Equal("/Administration/Finance/Expenses/Details", secondRedirect.PageName);
        Assert.False((await dbContext.Expenses.SingleAsync()).IsCancelled);
        Assert.Null((await dbContext.Expenses.SingleAsync()).CancellationReason);

        var auditEntries = await dbContext.FinancialAuditLogs.AsNoTracking().OrderBy(item => item.Id).ToListAsync();
        Assert.Equal(2, auditEntries.Count);
        Assert.Equal(FinancialAuditLogActions.Restored, auditEntries[1].Action);
        Assert.Equal(nameof(Expense), auditEntries[1].EntityType);
        Assert.DoesNotContain(auditEntries, item => item.Action == FinancialAuditLogActions.Updated && item.Description == "Восстановлен расход #102.");
        Assert.Empty(await dbContext.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateExpenseAsync_CreatesExactlyOneExpenseAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        dbContext.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = 2001,
            Name = "Хозяйственные расходы",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor();
        var model = new ExpenseCreateModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.ExpenseInputModel
            {
                ExpenseCategoryId = 2001,
                ExpenseDate = new DateOnly(2026, 2, 15),
                Amount = 345.67m,
                Description = "Покупка инвентаря",
                Payee = "Магазин",
                DocumentNumber = "INV-15"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Finance/Expenses/Index", redirect.PageName);

        var expense = await dbContext.Expenses.AsNoTracking().SingleAsync();
        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();

        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(Expense), auditEntry.EntityType);
        Assert.Equal(expense.Id.ToString(), auditEntry.EntityId);
        Assert.Equal("user-1", auditEntry.UserId);
        Assert.Equal("expense-user-1", auditEntry.UserName);
        Assert.Contains("\"ExpenseCategoryId\":2001", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"Amount\":345.67", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"DocumentNumber\":\"INV-15\"", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Empty(await dbContext.AuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateExpenseAsync_WhenValidationFails_DoesNotCreateAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor();
        var model = new ExpenseCreateModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.ExpenseInputModel
            {
                ExpenseCategoryId = 9999,
                ExpenseDate = new DateOnly(2026, 2, 15),
                Amount = 345.67m,
                Description = "Покупка инвентаря"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.Expenses.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateExpenseAsync_WhenAuditFails_RollsBackExpense()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        dbContext.Users.Add(new ApplicationUser
        {
            Id = "user-1",
            UserName = "expense-user-1",
            NormalizedUserName = "EXPENSE-USER-1",
            FirstName = "Expense",
            LastName = "User",
            DisplayName = "Expense User",
            SecurityStamp = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        dbContext.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = 2002,
            Name = "Хозяйственные расходы",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var model = new ExpenseCreateModel(dbContext, new ThrowingFinancialAuditService(), userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.ExpenseInputModel
            {
                ExpenseCategoryId = 2002,
                ExpenseDate = new DateOnly(2026, 2, 15),
                Amount = 345.67m,
                Description = "Покупка инвентаря"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.OnPostAsync(CancellationToken.None));

        Assert.Empty(await dbContext.Expenses.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    private sealed class ThrowingFinancialAuditService : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            throw new InvalidOperationException("Simulated audit failure.");
        }
    }
}

internal sealed class TestTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context)
    {
        return new Dictionary<string, object>();
    }

    public void SaveTempData(HttpContext context, IDictionary<string, object> values)
    {
    }
}
#endif
