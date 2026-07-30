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
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using CashInitializationModel = Neftyanik.Portal.Web.Pages.Administration.Finance.Settings.CashInitializationModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationFinanceCashInitializationTests
{
    [Fact]
    public async Task OnPostAsync_SavesCashInitializationAndRedirects()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-finance@example.com"));
        await dbContext.SaveChangesAsync();

        using var userManager = CreateUserManager(dbContext);
        var httpContext = CreateHttpContext(adminUserId);
        var model = new CashInitializationModel(dbContext, userManager)
        {
            Input = new CashInitializationModel.InputModel
            {
                Amount = 150.50m,
                AdvancePaymentsAmount = 12.25m,
                AcceptedAt = new DateOnly(2025, 1, 15),
                AcceptedFrom = "Иван Иванов",
                IsConfirmed = true
            },
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
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
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

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
        var model = new CashInitializationModel(dbContext, userManager);

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
    public async Task OnPostAsync_WhenInitializationAlreadyExists_DoesNotCreateSecondRecord()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

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
        var httpContext = CreateHttpContext(adminUserId);
        var model = new CashInitializationModel(dbContext, userManager)
        {
            Input = new CashInitializationModel.InputModel
            {
                Amount = 20m,
                AdvancePaymentsAmount = 3m,
                AcceptedAt = new DateOnly(2025, 2, 1),
                AcceptedFrom = "Новый кассир",
                IsConfirmed = true
            },
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.True(model.IsReadOnly);
        Assert.Single(await dbContext.SystemSettings.AsNoTracking().ToListAsync());
        Assert.Contains(model.ModelState[string.Empty]!.Errors, error => error.ErrorMessage.Contains("Инициализация кассы уже выполнена", StringComparison.Ordinal));
    }

    private static DefaultHttpContext CreateHttpContext(string userId)
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId)
            ],
            "Test"))
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
