using System.Security.Claims;
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
using Microsoft.Extensions.Primitives;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using MemberTariffCreateModel = Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs.CreateModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationMemberTariffTests
{
    [Fact]
    public async Task OnPostCreateMemberTariff_AcceptsDotAsDecimalSeparator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-tariff@example.com"));
        await dbContext.SaveChangesAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, adminUserId),
                new Claim(ClaimTypes.Name, "admin-tariff@example.com")
            ],
            "Test"))
        };
        httpContextAccessor.HttpContext = httpContext;

        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["Input.EffectiveFrom"] = "2026-07-29",
            ["Input.Rate"] = "5.01",
            ["Input.NightRate"] = "2.75"
        });

        var model = new MemberTariffCreateModel(service, userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs.TariffInputModel
            {
                EffectiveFrom = new DateOnly(2026, 7, 29),
                Rate = null,
                NightRate = null
            },
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        model.ModelState.AddModelError("Input.Rate", "The value '5.01' is not valid for Rate.");
        model.ModelState.AddModelError("Input.NightRate", "The value '2.75' is not valid for NightRate.");

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Electricity/MemberTariffs/Index", redirect.PageName);

        var tariff = await dbContext.MemberElectricityTariffs.AsNoTracking().SingleAsync();
        Assert.Equal(5.01m, tariff.Rate);
        Assert.Equal(2.75m, tariff.NightRate);

        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();
        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(MemberElectricityTariff), auditEntry.EntityType);
        Assert.Equal(tariff.Id.ToString(), auditEntry.EntityId);
        Assert.Equal(adminUserId, auditEntry.UserId);
        Assert.Equal("admin-tariff@example.com", auditEntry.UserName);
        Assert.Contains("\"MemberRate\":5.01", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"NightRate\":2.75", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Single(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnPostCreateMemberTariff_WhenValidationFails_DoesNotCreateAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-tariff@example.com"));
        await dbContext.SaveChangesAsync();

        using var userStore = new UserStore<ApplicationUser>(dbContext);
        using var userManager = new UserManager<ApplicationUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<ApplicationUser>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, adminUserId),
                new Claim(ClaimTypes.Name, "admin-tariff@example.com")
            ],
            "Test"))
        };
        httpContextAccessor.HttpContext = httpContext;

        var model = new MemberTariffCreateModel(service, userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Electricity.MemberTariffs.TariffInputModel
            {
                EffectiveFrom = new DateOnly(2026, 7, 29),
                Rate = -1m,
                NightRate = 2.75m
            },
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        var result = await model.OnPostAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Empty(await dbContext.MemberElectricityTariffs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateMemberTariffAsync_WhenAuditFails_RollsBackTariff()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var service = new MemberElectricityService(dbContext, new ThrowingFinancialAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateTariffAsync(
            new CreateMemberElectricityTariffRequest(new DateOnly(2026, 7, 29), 5.01m, 2.75m, null),
            CancellationToken.None));

        Assert.Empty(await dbContext.MemberElectricityTariffs.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
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

    private sealed class ThrowingFinancialAuditService : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            throw new InvalidOperationException("Simulated audit failure.");
        }
    }
}
