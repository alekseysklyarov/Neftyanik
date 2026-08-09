using Microsoft.AspNetCore.Http;
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
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using AssociationTariffCreateModel = Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.Tariffs.CreateModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationAssociationTariffTests
{
    [Fact]
    public async Task OnPostCreateAssociationTariff_AcceptsDotAsDecimalSeparator()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        dbContext.Users.Add(CreateUser(adminUserId, "admin-association-tariff@example.com"));
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

        var service = new AssociationElectricityService(dbContext);
        var httpContext = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, adminUserId)
            ],
            "Test"))
        };

        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["Input.EffectiveFrom"] = "2026-08-09",
            ["Input.DayRate"] = "4.32",
            ["Input.NightRate"] = "2.16"
        });

        var model = new AssociationTariffCreateModel(service, userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Electricity.Association.TariffInputModel
            {
                EffectiveFrom = new DateOnly(2026, 8, 9),
                DayRate = null,
                NightRate = null
            },
            PageContext = new PageContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        model.ModelState.AddModelError("Input.DayRate", "The value '4.32' is not valid for DayRate.");
        model.ModelState.AddModelError("Input.NightRate", "The value '2.16' is not valid for NightRate.");

        var result = await model.OnPostAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Electricity/Association/Tariffs/Index", redirect.PageName);

        var tariff = await dbContext.AssociationElectricityTariffs.AsNoTracking().SingleAsync();
        Assert.Equal(4.32m, tariff.DayRate);
        Assert.Equal(2.16m, tariff.NightRate);
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
}
