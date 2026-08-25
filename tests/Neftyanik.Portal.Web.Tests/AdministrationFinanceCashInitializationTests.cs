using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using CashInitializationModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Settings.CashInitializationModel;
using FinanceIndexModel = Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationFinanceCashInitializationTests
{
    [Fact]
    public async Task OnPostAsync_SavesCashInitializationAndRedirects()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var pageContext = CreatePageContext(adminUserId, "admin-finance@example.com", RoleNames.Administrator);
        var model = CreateModel(dbContext, userManager, pageContext);
        model.Input = new CashInitializationModel.InputModel
        {
            Amount = 150.50m,
            AdvancePaymentsAmount = 12.25m,
            AcceptedAt = new DateOnly(2025, 1, 15),
            AcceptedFrom = "Иван Иванов",
            IsConfirmed = true
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Finance/Settings/CashInitialization", redirect.PageName);

        var setting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        var storedValue = JsonSerializer.Deserialize<StoredCashInitialization>(setting.Value);

        Assert.NotNull(storedValue);
        Assert.Equal("Finance.CashInitialization", setting.Key);
        Assert.Equal(150.50m, storedValue!.Amount);
        Assert.Equal(12.25m, storedValue.AdvancePaymentsAmount);
        Assert.Equal(new DateOnly(2025, 1, 15), storedValue.AcceptedAt);
        Assert.Equal("Иван Иванов", storedValue.AcceptedFrom);
        Assert.Equal(adminUserId, setting.UpdatedByUserId);
    }

    [Fact]
    public async Task OnGetAsync_WhenInitializationExists_LoadsReadOnlyView()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = "Finance.CashInitialization",
            Value = "{\"Amount\":99.90,\"AcceptedAt\":\"2025-03-20\",\"AcceptedFrom\":\"Петров Петр\",\"AdvancePaymentsAmount\":15.40}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = new DateTimeOffset(2025, 3, 20, 10, 30, 0, TimeSpan.Zero),
            UpdatedByUserId = adminUserId
        });
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "admin-finance@example.com", RoleNames.Administrator));

        await model.OnGetAsync(CancellationToken.None);

        Assert.True(model.IsReadOnly);
        Assert.NotNull(model.CashInitialization);
        Assert.Equal(99.90m, model.CashInitialization!.Amount);
        Assert.Equal(15.40m, model.CashInitialization.AdvancePaymentsAmount);
        Assert.Equal(new DateOnly(2025, 3, 20), model.CashInitialization.AcceptedAt);
        Assert.Equal("Петров Петр", model.CashInitialization.AcceptedFrom);
        Assert.Equal("User Admin", model.CashInitialization.AcceptedBy);
    }

    [Fact]
    public async Task OnGetAsync_WhenAccountantLoadsExistingInitialization_ShowsReadOnlyData()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = "Finance.CashInitialization",
            Value = "{\"Amount\":88.40,\"AcceptedAt\":\"2025-03-20\",\"AcceptedFrom\":\"Петров Петр\",\"AdvancePaymentsAmount\":5.00}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = new DateTimeOffset(2025, 3, 20, 10, 30, 0, TimeSpan.Zero),
            UpdatedByUserId = adminUserId
        });
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext("accountant-user", "accountant@example.com", RoleNames.Accountant));

        await model.OnGetAsync(CancellationToken.None);

        Assert.NotNull(model.CashInitialization);
        Assert.Equal(88.40m, model.CashInitialization!.Amount);
        Assert.False(model.CanAdjust);
        Assert.False(model.CanCreate);
    }

    [Fact]
    public async Task OnPostAsync_WhenInitializationAlreadyExists_DoesNotCreateSecondRecord()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = "Finance.CashInitialization",
            Value = "{\"Amount\":10.00,\"AcceptedAt\":\"2025-01-10\",\"AcceptedFrom\":\"Старый кассир\",\"AdvancePaymentsAmount\":5.00}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = adminUserId
        });
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "admin-finance@example.com", RoleNames.Administrator));
        model.Input = new CashInitializationModel.InputModel
        {
            Amount = 20m,
            AdvancePaymentsAmount = 3m,
            AcceptedAt = new DateOnly(2025, 2, 1),
            AcceptedFrom = "Новый кассир",
            IsConfirmed = true
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.IsReadOnly);
        Assert.Single(await dbContext.SystemSettings.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("Инициализация кассы уже выполнена", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenAdministratorIncreasesAmount_UpdatesSettingAndCreatesAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        const string adminUserName = "administrator@example.com";
        var setting = await SeedInitializationAsync(dbContext, adminUserId, 100m, 10m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, adminUserName, RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 175.45m,
            AdjustmentReason = "  Пересчитали наличные после повторной инвентаризации.  "
        };

        var previousUpdatedAt = setting.UpdatedAt;

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Finance/Settings/CashInitialization", redirect.PageName);

        var persistedSetting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        var storedValue = JsonSerializer.Deserialize<StoredCashInitialization>(persistedSetting.Value);
        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();

        Assert.NotNull(storedValue);
        Assert.Single(await dbContext.SystemSettings.AsNoTracking().ToListAsync());
        Assert.Equal(setting.Id, persistedSetting.Id);
        Assert.Equal(175.45m, storedValue!.Amount);
        Assert.Equal(10m, storedValue.AdvancePaymentsAmount);
        Assert.True(persistedSetting.UpdatedAt >= previousUpdatedAt);
        Assert.Equal(adminUserId, persistedSetting.UpdatedByUserId);
        Assert.Equal(FinancialAuditLogActions.Updated, auditEntry.Action);
        Assert.Equal(nameof(SystemSetting), auditEntry.EntityType);
        Assert.Equal(persistedSetting.Id.ToString(), auditEntry.EntityId);
        Assert.Equal(adminUserId, auditEntry.UserId);
        Assert.Equal(adminUserName, auditEntry.UserName);
        Assert.Contains("Скорректирована инициализация кассы. Причина: Пересчитали наличные после повторной инвентаризации.", auditEntry.Description, StringComparison.Ordinal);
        Assert.Contains("\"Amount\":100", auditEntry.OldValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"Amount\":175.45", auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenAdministratorDecreasesAmount_UpdatesSetting()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        await SeedInitializationAsync(dbContext, adminUserId, 220m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 180m,
            AdjustmentReason = "Убрали ошибочно добавленные купюры"
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var setting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        var storedValue = JsonSerializer.Deserialize<StoredCashInitialization>(setting.Value);

        Assert.NotNull(storedValue);
        Assert.Equal(180m, storedValue!.Amount);
        Assert.Single(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenAccountantPostsDirectly_ReturnsForbidWithoutChanges()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string userId = "accountant-user";
        var setting = await SeedInitializationAsync(dbContext, userId, 90m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(userId, "accountant@example.com", RoleNames.Accountant));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 120m,
            AdjustmentReason = "Попытка ручной корректировки"
        };

        var originalUpdatedAt = setting.UpdatedAt;
        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<ForbidResult>(result);

        var persistedSetting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        var storedValue = JsonSerializer.Deserialize<StoredCashInitialization>(persistedSetting.Value);

        Assert.NotNull(storedValue);
        Assert.Equal(90m, storedValue!.Amount);
        Assert.Equal(originalUpdatedAt, persistedSetting.UpdatedAt);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenAmountIsUnchanged_DoesNotUpdateSettingOrCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        var setting = await SeedInitializationAsync(dbContext, adminUserId, 100m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 100m,
            AdjustmentReason = "Сумма совпала"
        };

        var originalUpdatedAt = setting.UpdatedAt;
        var originalUpdatedByUserId = setting.UpdatedByUserId;

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);

        var persistedSetting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        Assert.Equal(originalUpdatedAt, persistedSetting.UpdatedAt);
        Assert.Equal(originalUpdatedByUserId, persistedSetting.UpdatedByUserId);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenReasonIsEmpty_DoesNotCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        await SeedInitializationAsync(dbContext, adminUserId, 100m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 120m,
            AdjustmentReason = string.Empty
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[$"{nameof(CashInitializationModel.Adjustment)}.{nameof(CashInitializationModel.AdjustmentInputModel.AdjustmentReason)}"]!.Errors,
            error => error.ErrorMessage.Contains("Укажите причину корректировки", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenReasonIsWhitespace_DoesNotCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        await SeedInitializationAsync(dbContext, adminUserId, 100m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 120m,
            AdjustmentReason = "   "
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[$"{nameof(CashInitializationModel.Adjustment)}.{nameof(CashInitializationModel.AdjustmentInputModel.AdjustmentReason)}"]!.Errors,
            error => error.ErrorMessage.Contains("Укажите причину корректировки", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenReasonLengthExceedsLimit_DoesNotCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        await SeedInitializationAsync(dbContext, adminUserId, 100m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 120m,
            AdjustmentReason = new string('а', 501)
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[$"{nameof(CashInitializationModel.Adjustment)}.{nameof(CashInitializationModel.AdjustmentInputModel.AdjustmentReason)}"]!.Errors,
            error => error.ErrorMessage.Contains("не должна превышать 500 символов", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenAmountIsNegative_DoesNotCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        await SeedInitializationAsync(dbContext, adminUserId, 100m, 0m, new DateOnly(2025, 1, 10));

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = -1m,
            AdjustmentReason = "Ошибка ввода"
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[$"{nameof(CashInitializationModel.Adjustment)}.{nameof(CashInitializationModel.AdjustmentInputModel.Amount)}"]!.Errors,
            error => error.ErrorMessage.Contains("Сумма должна быть больше нуля", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OnPostAdjustAsync_WhenJsonIsCorrupted_DoesNotOverwriteSettingOrCreateAudit()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Key = "Finance.CashInitialization",
            Value = "{invalid json}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = adminUserId
        });
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 120m,
            AdjustmentReason = "Попытка исправить"
        };

        var result = await model.OnPostAdjustAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var setting = await dbContext.SystemSettings.AsNoTracking().SingleAsync();
        Assert.Equal("{invalid json}", setting.Value);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostAdjustAsync_RecalculatesCurrentCashAmount()
    {
        await using var dbContext = await CreateDbContextAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        dbContext.ExpenseCategories.Add(new ExpenseCategory
        {
            Id = 5001,
            Name = "Тестовые расходы",
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var acceptedAt = new DateOnly(DateTime.Today.Year, 1, 10);
        await SeedInitializationAsync(dbContext, adminUserId, 100m, 20m, acceptedAt);
        dbContext.Payments.Add(new Payment
        {
            Id = 1,
            Amount = 50m,
            PaymentDate = acceptedAt.AddDays(1),
            PaymentMethod = PaymentMethod.Cash,
            CreatedAtUtc = DateTime.UtcNow
        });
        dbContext.Expenses.Add(new Expense
        {
            Id = 1,
            ExpenseCategoryId = 5001,
            ExpenseDate = acceptedAt.AddDays(2),
            Amount = 10m,
            Description = "Расход после инициализации",
            CreatedByUserId = adminUserId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var model = CreateModel(dbContext, userManager, CreatePageContext(adminUserId, "administrator@example.com", RoleNames.Administrator));
        model.Adjustment = new CashInitializationModel.AdjustmentInputModel
        {
            Amount = 160m,
            AdjustmentReason = "После пересчёта нашли дополнительные наличные"
        };

        await model.OnPostAdjustAsync(CancellationToken.None);

        var financeModel = new FinanceIndexModel(dbContext);
        await financeModel.OnGetAsync(CancellationToken.None);

        Assert.Equal(180m, financeModel.Summary.CurrentCashAmount);
        Assert.Equal(180m, financeModel.Summary.CurrentCashOnlyAmount);
    }

    private static CashInitializationModel CreateModel(
        ApplicationDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        PageContext pageContext)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = pageContext.HttpContext
        };

        var model = new CashInitializationModel(dbContext, new FinancialAuditService(dbContext, httpContextAccessor), userManager)
        {
            PageContext = pageContext,
            TempData = new TempDataDictionary(pageContext.HttpContext, new TestTempDataProvider())
        };

        return model;
    }

    private static async Task<SystemSetting> SeedInitializationAsync(
        ApplicationDbContext dbContext,
        string updatedByUserId,
        decimal amount,
        decimal advancePaymentsAmount,
        DateOnly acceptedAt)
    {
        if (!await dbContext.Users.AnyAsync(user => user.Id == updatedByUserId))
        {
            dbContext.Users.Add(CreateUser(updatedByUserId, $"{updatedByUserId}@example.com"));
        }

        var setting = new SystemSetting
        {
            Key = "Finance.CashInitialization",
            Value = JsonSerializer.Serialize(new StoredCashInitialization
            {
                Amount = amount,
                AdvancePaymentsAmount = advancePaymentsAmount,
                AcceptedAt = acceptedAt,
                AcceptedFrom = "Кассир"
            }),
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = updatedByUserId
        };

        dbContext.SystemSettings.Add(setting);
        await dbContext.SaveChangesAsync();
        return setting;
    }

    private static async Task<ApplicationDbContext> CreateDbContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
    }

    private static PageContext CreatePageContext(string userId, string userName, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, userName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext dbContext)
    {
        var userStore = new UserStore<ApplicationUser>(dbContext);

        return new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    private static ApplicationUser CreateUser(string id, string email)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Admin",
            LastName = "User",
            MustChangePassword = false,
            IsActive = true
        };
    }

    private sealed class StoredCashInitialization
    {
        public decimal Amount { get; init; }

        public decimal AdvancePaymentsAmount { get; init; }

        public DateOnly AcceptedAt { get; init; }

        public string AcceptedFrom { get; init; } = string.Empty;
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
