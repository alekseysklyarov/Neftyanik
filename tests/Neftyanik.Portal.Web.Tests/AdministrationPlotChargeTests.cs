#if WEB_TESTS
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Administration.Plots;
using Neftyanik.Portal.Web.Pages.Administration.Plots.Finance;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationPlotChargeTests
{
    [Fact]
    public async Task GetAdministrationPlots_WithActiveOwnedFilter_ShowsOnlyEligiblePlotsForBulkCharge()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int activeOwnedPlotId = 1001;
        const int activeWithoutOwnerPlotId = 1002;
        const int archivedOwnedPlotId = 1003;
        const int memberId = 1101;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "plots-admin@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Plot Owner",
                IsActive = true
            });
            dbContext.Plots.AddRange(
                new Plot { Id = activeOwnedPlotId, Number = "P-1001", Address = "Owned Plot", IsActive = true },
                new Plot { Id = activeWithoutOwnerPlotId, Number = "P-1002", Address = "No Owner Plot", IsActive = true },
                new Plot { Id = archivedOwnedPlotId, Number = "P-1003", Address = "Archived Plot", IsActive = false });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = activeOwnedPlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = archivedOwnedPlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = false
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync("/Administration/Plots?status=active&ownership=withowners");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("P-1001", html, StringComparison.Ordinal);
        Assert.DoesNotContain("P-1002", html, StringComparison.Ordinal);
        Assert.DoesNotContain("P-1003", html, StringComparison.Ordinal);
        Assert.Contains("bulk-charge-form", html, StringComparison.Ordinal);
        Assert.Contains("ChargeInput.SelectedPlotIds", html, StringComparison.Ordinal);
        Assert.Contains("select-all-plots", html, StringComparison.Ordinal);
        Assert.Contains("bulkChargeModal", html, StringComparison.Ordinal);
        Assert.Contains("Plot Owner", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Один<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Ни одного<", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Основной контакт", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Administration/Plots/Ownerships/Index", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnGetPlotDetailsAsync_LoadsChargesForReadOnlyDisplay()
    {
        using var cultureScope = new TestCultureScope("ru-RU");
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int plotId = 1501;
        const int chargeTypeId = 1601;

        dbContext.Plots.Add(new Plot
        {
            Id = plotId,
            Number = "P-1501",
            Address = "Charge Details Plot",
            IsActive = true
        });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = chargeTypeId,
            Name = "Членский взнос",
            IsActive = true,
            DefaultAmount = 500m
        });
        dbContext.Charges.Add(new Charge
        {
            PlotId = plotId,
            ChargeTypeId = chargeTypeId,
            Amount = 500m,
            ChargeDate = new DateOnly(2026, 1, 15),
            DueDate = new DateOnly(2026, 1, 31),
            Description = "Тестовое начисление"
        });

        await dbContext.SaveChangesAsync();

        var model = new DetailsModel(dbContext);

        var result = await model.OnGetAsync(plotId, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(plotId, model.Plot.Id);
        var charge = Assert.Single(model.Plot.Charges);
        Assert.Equal("Членский взнос", charge.ChargeTypeName);
        Assert.Equal(500m, charge.Amount);
        Assert.Equal(new DateOnly(2026, 1, 15), charge.ChargeDate);
        Assert.Equal(new DateOnly(2026, 1, 31), charge.DueDate);
        Assert.Equal("Тестовое начисление", charge.Description);
        Assert.Equal("Активный", charge.StatusText);
    }

    [Fact]
    public async Task OnGetPlotDetailsAsync_FiltersChargesByType()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int plotId = 1701;
        const int membershipChargeTypeId = 1801;
        const int electricityChargeTypeId = 1802;

        dbContext.Plots.Add(new Plot
        {
            Id = plotId,
            Number = "P-1701",
            IsActive = true
        });

        dbContext.ChargeTypes.AddRange(
            new ChargeType
            {
                Id = membershipChargeTypeId,
                Name = "Членский взнос",
                IsActive = true,
                DefaultAmount = 500m
            },
            new ChargeType
            {
                Id = electricityChargeTypeId,
                Name = "Электроэнергия",
                IsActive = true,
                DefaultAmount = 250m
            });

        dbContext.Charges.AddRange(
            new Charge
            {
                PlotId = plotId,
                ChargeTypeId = membershipChargeTypeId,
                Amount = 500m,
                ChargeDate = new DateOnly(2026, 1, 15)
            },
            new Charge
            {
                PlotId = plotId,
                ChargeTypeId = electricityChargeTypeId,
                Amount = 250m,
                ChargeDate = new DateOnly(2026, 2, 15)
            });

        await dbContext.SaveChangesAsync();

        var model = new DetailsModel(dbContext)
        {
            ChargeTypeId = electricityChargeTypeId
        };

        var result = await model.OnGetAsync(plotId, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var charge = Assert.Single(model.Plot.Charges);
        Assert.Equal("Электроэнергия", charge.ChargeTypeName);
        Assert.Equal(250m, charge.Amount);
        Assert.Equal(2, model.ChargeTypeOptions.Count);
    }

    [Fact]
    public async Task OnPostCreateChargesAsync_WithMultipleSelectedPlots_UsesDefaultAmountAndCurrentDate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int chargeTypeId = 1201;
        const int firstPlotId = 1301;
        const int secondPlotId = 1302;
        const int memberId = 1401;

        dbContext.Users.Add(CreateUser(adminUserId, "charges-admin@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Bulk Charge Owner",
            IsActive = true
        });
        dbContext.Plots.AddRange(
            new Plot { Id = firstPlotId, Number = "P-1301", IsActive = true },
            new Plot { Id = secondPlotId, Number = "P-1302", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 1,
                PlotId = firstPlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 2,
                PlotId = secondPlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = false
            });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = chargeTypeId,
            Name = "Членский взнос",
            IsActive = true,
            DefaultAmount = 650m
        });
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
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new IndexModel(dbContext, userManager)
        {
            ChargeInput = new PlotChargeInputModel
            {
                SelectedPlotIds = [firstPlotId, secondPlotId],
                ChargeTypeId = chargeTypeId,
                Amount = 500m,
                ChargeDate = new DateOnly(2026, 1, 15),
                DueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(10),
                Description = "Массовое начисление"
            },
            Status = "active",
            Ownership = "withowners",
            PageNumber = 1
        };

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUserId)
                ],
                "Test"))
            }
        };

        var result = await model.OnPostCreateChargesAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Plots/Index", redirect.PageName);

        var charges = await dbContext.Charges
            .AsNoTracking()
            .OrderBy(item => item.PlotId)
            .ToListAsync();

        Assert.Equal(2, charges.Count);
        Assert.Collection(charges,
            charge =>
            {
                Assert.Equal(firstPlotId, charge.PlotId);
                Assert.Equal(650m, charge.Amount);
                Assert.Equal(chargeTypeId, charge.ChargeTypeId);
                Assert.Equal(DateOnly.FromDateTime(DateTime.Today), charge.ChargeDate);
                Assert.Equal(adminUserId, charge.CreatedByUserId);
            },
            charge =>
            {
                Assert.Equal(secondPlotId, charge.PlotId);
                Assert.Equal(650m, charge.Amount);
                Assert.Equal(chargeTypeId, charge.ChargeTypeId);
                Assert.Equal(DateOnly.FromDateTime(DateTime.Today), charge.ChargeDate);
                Assert.Equal(adminUserId, charge.CreatedByUserId);
            });
    }

    [Fact]
    public async Task OnPostCreateChargesAsync_WhenSomePlotsAlreadyCharged_CreatesOnlyMissingCharges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int chargeTypeId = 2201;
        const int duplicatePlotId = 2301;
        const int newPlotId = 2302;
        const int memberId = 2401;

        dbContext.Users.Add(CreateUser(adminUserId, "duplicate-admin@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Duplicate Charge Owner",
            IsActive = true
        });
        dbContext.Plots.AddRange(
            new Plot { Id = duplicatePlotId, Number = "P-2301", IsActive = true },
            new Plot { Id = newPlotId, Number = "P-2302", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 1,
                PlotId = duplicatePlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 2,
                PlotId = newPlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = false
            });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = chargeTypeId,
            Name = "Годовое начисление",
            IsActive = true,
            IsYearly = true,
            DefaultAmount = 700m
        });
        dbContext.Charges.Add(new Charge
        {
            PlotId = duplicatePlotId,
            ChargeTypeId = chargeTypeId,
            Amount = 700m,
            ChargeDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedByUserId = adminUserId
        });
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
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new IndexModel(dbContext, userManager)
        {
            ChargeInput = new PlotChargeInputModel
            {
                SelectedPlotIds = [duplicatePlotId, newPlotId],
                ChargeTypeId = chargeTypeId,
                DueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(10),
                Description = "Повторное начисление"
            },
            Status = "active",
            Ownership = "withowners",
            PageNumber = 1
        };

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUserId)
                ],
                "Test"))
            }
        };

        var result = await model.OnPostCreateChargesAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Plots/Index", redirect.PageName);

        var charges = await dbContext.Charges
            .AsNoTracking()
            .OrderBy(item => item.PlotId)
            .ToListAsync();

        Assert.Equal(2, charges.Count);
        Assert.Collection(charges,
            charge =>
            {
                Assert.Equal(duplicatePlotId, charge.PlotId);
                Assert.Equal(700m, charge.Amount);
            },
            charge =>
            {
                Assert.Equal(newPlotId, charge.PlotId);
                Assert.Equal(700m, charge.Amount);
                Assert.Equal(chargeTypeId, charge.ChargeTypeId);
                Assert.Equal(DateOnly.FromDateTime(DateTime.Today), charge.ChargeDate);
            });
    }

    [Fact]
    public async Task OnPostCreateChargesAsync_WhenTypeIsNotYearly_AllowsRepeatedChargesInSameYear()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int chargeTypeId = 3201;
        const int plotId = 3301;
        const int memberId = 3401;

        dbContext.Users.Add(CreateUser(adminUserId, "nonyearly-admin@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Regular Charge Owner",
            IsActive = true
        });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-3301", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            PlotId = plotId,
            MemberId = memberId,
            ValidFrom = new DateOnly(2020, 1, 1),
            IsPrimaryContact = true
        });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = chargeTypeId,
            Name = "Разовое начисление",
            IsActive = true,
            DefaultAmount = 250m
        });
        dbContext.Charges.Add(new Charge
        {
            PlotId = plotId,
            ChargeTypeId = chargeTypeId,
            Amount = 250m,
            ChargeDate = DateOnly.FromDateTime(DateTime.Today),
            CreatedByUserId = adminUserId
        });
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
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new IndexModel(dbContext, userManager)
        {
            ChargeInput = new PlotChargeInputModel
            {
                SelectedPlotIds = [plotId],
                ChargeTypeId = chargeTypeId,
                DueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(10),
                Description = "Повторное разовое начисление"
            },
            Status = "active",
            Ownership = "withowners",
            PageNumber = 1
        };

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUserId)
                ],
                "Test"))
            }
        };

        var result = await model.OnPostCreateChargesAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Plots/Index", redirect.PageName);

        var charges = await dbContext.Charges
            .AsNoTracking()
            .Where(item => item.PlotId == plotId)
            .OrderBy(item => item.Id)
            .ToListAsync();

        Assert.Equal(2, charges.Count);
        Assert.All(charges, charge => Assert.Equal(chargeTypeId, charge.ChargeTypeId));
    }

    [Fact]
    public async Task OnPostCreateChargesAsync_WhenTypeIsOnlyOnOwnerChange_CreatesOnlyOneChargePerOwnership()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int chargeTypeId = 4201;
        const int duplicatePlotId = 4301;
        const int newPlotId = 4302;
        const int memberId = 4401;

        dbContext.Users.Add(CreateUser(adminUserId, "ownerchange-admin@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Owner Change Charge Owner",
            IsActive = true
        });
        dbContext.Plots.AddRange(
            new Plot { Id = duplicatePlotId, Number = "P-4301", IsActive = true },
            new Plot { Id = newPlotId, Number = "P-4302", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership
            {
                Id = 1,
                PlotId = duplicatePlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsPrimaryContact = true
            },
            new PlotOwnership
            {
                Id = 2,
                PlotId = newPlotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2026, 1, 1),
                IsPrimaryContact = false
            });
        dbContext.ChargeTypes.Add(new ChargeType
        {
            Id = chargeTypeId,
            Name = "При смене владельца",
            IsActive = true,
            OnlyOnOwnerChange = true,
            DefaultAmount = 900m
        });
        dbContext.Charges.Add(new Charge
        {
            PlotId = duplicatePlotId,
            ChargeTypeId = chargeTypeId,
            Amount = 900m,
            ChargeDate = new DateOnly(2026, 2, 1),
            CreatedByUserId = adminUserId
        });
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
            null,
            NullLogger<UserManager<ApplicationUser>>.Instance);

        var model = new IndexModel(dbContext, userManager)
        {
            ChargeInput = new PlotChargeInputModel
            {
                SelectedPlotIds = [duplicatePlotId, newPlotId],
                ChargeTypeId = chargeTypeId,
                DueDate = DateOnly.FromDateTime(DateTime.Today).AddDays(10),
                Description = "Начисление при смене владельца"
            },
            Status = "active",
            Ownership = "withowners",
            PageNumber = 1
        };

        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUserId)
                ],
                "Test"))
            }
        };

        var result = await model.OnPostCreateChargesAsync(CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Plots/Index", redirect.PageName);

        var charges = await dbContext.Charges
            .AsNoTracking()
            .OrderBy(item => item.PlotId)
            .ToListAsync();

        Assert.Equal(2, charges.Count);
        Assert.Collection(charges,
            charge =>
            {
                Assert.Equal(duplicatePlotId, charge.PlotId);
                Assert.Equal(new DateOnly(2026, 2, 1), charge.ChargeDate);
            },
            charge =>
            {
                Assert.Equal(newPlotId, charge.PlotId);
                Assert.Equal(900m, charge.Amount);
                Assert.Equal(DateOnly.FromDateTime(DateTime.Today), charge.ChargeDate);
            });
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
}
#endif
