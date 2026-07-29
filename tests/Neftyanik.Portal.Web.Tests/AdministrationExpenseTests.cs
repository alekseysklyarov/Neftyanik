#if WEB_TESTS
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using ExpenseCancelModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Expenses.CancelModel;
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

        var service = new AssociationElectricityService(dbContext);
        var model = new ElectricityExpenseIndexModel(dbContext, service, userManager);

        await model.OnGetAsync(CancellationToken.None);

        Assert.False(model.HasHistory);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), model.Input.ReadingDate);
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

        var model = new ExpenseEditModel(dbContext, userManager)
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

        var model = new ExpenseCancelModel(dbContext, userManager)
        {
            Input = new ExpenseCancelModel.InputModel
            {
                CancellationReason = "Ошибка в документе"
            },
            PageContext = TestPageModelContext.CreatePageContext("user-1", "expense-user-1")
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var firstResult = await model.OnPostAsync(102, CancellationToken.None);
        var firstRedirect = Assert.IsType<RedirectToPageResult>(firstResult);
        Assert.Equal("/Administration/Finance/Expenses/Details", firstRedirect.PageName);
        Assert.True((await dbContext.Expenses.SingleAsync()).IsCancelled);
        Assert.Equal("Ошибка в документе", (await dbContext.Expenses.SingleAsync()).CancellationReason);

        var secondResult = await model.OnPostAsync(102, CancellationToken.None);
        var secondRedirect = Assert.IsType<RedirectToPageResult>(secondResult);
        Assert.Equal("/Administration/Finance/Expenses/Details", secondRedirect.PageName);
        Assert.False((await dbContext.Expenses.SingleAsync()).IsCancelled);
        Assert.Null((await dbContext.Expenses.SingleAsync()).CancellationReason);
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
