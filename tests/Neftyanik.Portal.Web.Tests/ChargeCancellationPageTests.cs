using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using AdminFinanceModel = Neftyanik.Portal.Web.Pages.Administration.Members.FinanceModel;
using CancelChargePageModel = Neftyanik.Portal.Web.Pages.Administration.Members.Finance.CancelChargeModel;
using MemberReadingHistoryModel = Neftyanik.Portal.Web.Pages.Member.Electricity.Meters.Readings.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ChargeCancellationPageTests
{
    [Fact]
    public async Task OnPostCancelChargeAsync_CancelsChargeAndRedirectsToMemberFinance()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var charge = await testContext.SeedChargeAsync(101, 450m, new DateOnly(2026, 8, 1), description: "Тестовое начисление");

        var model = new CancelChargePageModel(testContext.DbContext, testContext.CreateChargeService())
        {
            Input = new CancelChargePageModel.InputModel
            {
                CancellationReason = "Начисление создано вручную ошибочно."
            },
            PageContext = TestPageModelContext.CreatePageContext("admin-user", "admin@example.com")
        };
        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        testContext.HttpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(1, charge.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);
        Assert.Equal(1, redirect.RouteValues!["id"]);

        var storedCharge = await testContext.DbContext.Charges.AsNoTracking().SingleAsync();
        Assert.NotNull(storedCharge.CancelledAtUtc);
        Assert.Equal("Начисление создано вручную ошибочно.", storedCharge.CancellationReason);
        Assert.Single(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().Where(item => item.EntityType == nameof(Charge) && item.Action == FinancialAuditLogActions.Cancelled).ToListAsync());
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_UnpaidCancelledCharge_RemovesDebtAndKeepsHistory()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));
        await testContext.CancelChargeAsync(charge.Id, "Отмена неоплаченного начисления.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = testContext.CreateAdminFinanceModel(userManager);

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0m, model.Member.TotalCharges);
        Assert.Equal(0m, model.Member.TotalPayments);
        Assert.Single(model.Plots);
        Assert.Equal(0m, model.Plots[0].Balance);
        Assert.Contains(model.Charges, item => item.ChargeId == charge.Id && item.IsCancelled);
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_PartiallyPaidCancelledCharge_ReleasesCredit()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));
        var payment = await testContext.SeedPaymentAsync(101, 600m, new DateOnly(2026, 8, 2));
        await testContext.SeedPaymentAllocationAsync(payment.Id, charge.Id, 600m);
        await testContext.CancelChargeAsync(charge.Id, "Отмена частично оплаченного начисления.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = testContext.CreateAdminFinanceModel(userManager);

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0m, model.Member.TotalCharges);
        Assert.Equal(600m, model.Member.TotalPayments);
        Assert.Single(model.Plots);
        Assert.Equal(600m, model.Plots[0].Payments);
        Assert.Equal(-600m, model.Plots[0].Balance);
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_FullyPaidCancelledCharge_ReleasesFullCredit()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var charge = await testContext.SeedChargeAsync(101, 1000m, new DateOnly(2026, 8, 1));
        var payment = await testContext.SeedPaymentAsync(101, 1000m, new DateOnly(2026, 8, 2));
        await testContext.SeedPaymentAllocationAsync(payment.Id, charge.Id, 1000m);
        await testContext.CancelChargeAsync(charge.Id, "Отмена полностью оплаченного начисления.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = testContext.CreateAdminFinanceModel(userManager);

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0m, model.Member.TotalCharges);
        Assert.Equal(1000m, model.Member.TotalPayments);
        Assert.Single(model.Plots);
        Assert.Equal(1000m, model.Plots[0].Payments);
        Assert.Equal(-1000m, model.Plots[0].Balance);
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_CancellingOneOfMultipleCharges_LeavesOtherChargeActive()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var cancelledCharge = await testContext.SeedChargeAsync(101, 300m, new DateOnly(2026, 8, 1));
        var activeCharge = await testContext.SeedChargeAsync(101, 400m, new DateOnly(2026, 8, 2));
        await testContext.CancelChargeAsync(cancelledCharge.Id, "Отменено одно начисление.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = testContext.CreateAdminFinanceModel(userManager);

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(400m, model.Member.TotalCharges);
        Assert.Single(model.Plots);
        Assert.Equal(400m, model.Plots[0].Balance);
        Assert.Contains(model.Charges, item => item.ChargeId == cancelledCharge.Id && item.IsCancelled);
        Assert.Contains(model.Charges, item => item.ChargeId == activeCharge.Id && !item.IsCancelled);
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_CrossPlotCancelledCharge_ReturnsCreditToPaymentPlot()
    {
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        await testContext.SeedPlotOwnershipAsync(1, 102, "P-102");
        var chargeOnPlotB = await testContext.SeedChargeAsync(102, 600m, new DateOnly(2026, 8, 1));
        var paymentOnPlotA = await testContext.SeedPaymentAsync(101, 600m, new DateOnly(2026, 8, 2));
        await testContext.SeedPaymentAllocationAsync(paymentOnPlotA.Id, chargeOnPlotB.Id, 600m);
        await testContext.CancelChargeAsync(chargeOnPlotB.Id, "Отмена начисления на другом участке.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = testContext.CreateAdminFinanceModel(userManager);

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0m, model.Member.TotalCharges);
        Assert.Equal(600m, model.Member.TotalPayments);

        var plotsById = model.Plots.ToDictionary(item => item.PlotId);
        Assert.Equal(600m, plotsById[101].Payments);
        Assert.Equal(-600m, plotsById[101].Balance);
        Assert.Equal(0m, plotsById[102].Payments);
        Assert.Equal(0m, plotsById[102].Balance);
    }

    [Fact]
    public async Task OnGetMemberElectricityReadingHistory_CancelledCharge_RemainsVisibleAsCancelled()
    {
        using var cultureScope = new TestCultureScope("ru-RU");
        await using var testContext = await ChargeCancellationTestContext.CreateAsync();
        await testContext.SeedMemberAsync(1, "member-user");
        await testContext.SeedPlotOwnershipAsync(1, 101, "P-101");
        var charge = await testContext.SeedChargeAsync(101, 125m, new DateOnly(2026, 8, 1));
        var meterId = await testContext.SeedElectricityReadingWithMeterAsync(1, 101, charge.Id);
        await testContext.CancelChargeAsync(charge.Id, "Отмена начисления по показаниям.", "admin-user", "admin@example.com");

        using var userManager = testContext.CreateUserManager();
        var model = new MemberReadingHistoryModel(testContext.DbContext, userManager)
        {
            PageContext = TestPageModelContext.CreatePageContext("member-user", "member-user@example.com")
        };

        var result = await model.OnGetAsync(meterId, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        var reading = Assert.Single(model.Readings);
        Assert.True(reading.IsChargeCancelled);
        Assert.Equal("Начисление отменено", reading.ChargeStatusText);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_ShowsCancelActionForActiveChargeOnly()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 3201;
        const int plotId = 3202;

        long activeChargeId = 0;
        long cancelledChargeId = 0;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-charge-cancel@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Charge Cancellation Member",
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-3202",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            dbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
            {
                Id = 3203,
                Name = "Тестовый тип",
                Code = "TEST-CHARGE-CANCEL",
                IsActive = true
            });

            var activeCharge = new Charge
            {
                PlotId = plotId,
                ChargeTypeId = 3203,
                Amount = 200m,
                ChargeDate = new DateOnly(2026, 8, 1),
                CreatedAtUtc = DateTime.UtcNow
            };
            var cancelledCharge = new Charge
            {
                PlotId = plotId,
                ChargeTypeId = 3203,
                Amount = 150m,
                ChargeDate = new DateOnly(2026, 8, 2),
                CreatedAtUtc = DateTime.UtcNow,
                CancelledAtUtc = DateTime.UtcNow,
                CancellationReason = "Ошибочное начисление"
            };

            dbContext.Charges.AddRange(activeCharge, cancelledCharge);
            await dbContext.SaveChangesAsync();

            activeChargeId = activeCharge.Id;
            cancelledChargeId = cancelledCharge.Id;
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/Charges/{activeChargeId}/Cancel", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/Administration/Members/Finance/{memberId}/Charges/{cancelledChargeId}/Cancel", html, StringComparison.Ordinal);
    }

    private static ApplicationUser CreateUser(string userId, string email)
    {
        return new ApplicationUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Admin",
            LastName = "User",
            DisplayName = "Portal Admin"
        };
    }

    private sealed class ChargeCancellationTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private ChargeCancellationTestContext(SqliteConnection connection, ApplicationDbContext dbContext, HttpContextAccessor httpContextAccessor)
        {
            _connection = connection;
            DbContext = dbContext;
            HttpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext DbContext { get; }

        public HttpContextAccessor HttpContextAccessor { get; }

        public static async Task<ChargeCancellationTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new ChargeCancellationTestContext(connection, dbContext, new HttpContextAccessor());
        }

        public ChargeService CreateChargeService()
        {
            return new ChargeService(DbContext, new FinancialAuditService(DbContext, HttpContextAccessor));
        }

        public UserManager<ApplicationUser> CreateUserManager()
        {
            var userStore = new UserStore<ApplicationUser>(DbContext);
            return new UserManager<ApplicationUser>(
                userStore,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                Array.Empty<IUserValidator<ApplicationUser>>(),
                Array.Empty<IPasswordValidator<ApplicationUser>>(),
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                null,
                NullLogger<UserManager<ApplicationUser>>.Instance);
        }

        public AdminFinanceModel CreateAdminFinanceModel(UserManager<ApplicationUser> userManager)
        {
            return new AdminFinanceModel(
                DbContext,
                new MemberElectricityService(DbContext, new FinancialAuditService(DbContext, HttpContextAccessor)),
                userManager)
            {
                ChargePage = 1,
                PaymentPage = 1
            };
        }

        public async Task CancelChargeAsync(long chargeId, string reason, string userId, string userName)
        {
            HttpContextAccessor.HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, userName)
                    ],
                    authenticationType: "TestAuthentication"))
            };

            var result = await CreateChargeService().CancelChargeAsync(new CancelChargeRequest(chargeId, reason));
            Assert.True(result.Succeeded);
        }

        public async Task SeedAdminUserAsync(string userId, string email)
        {
            if (await DbContext.Users.AnyAsync(item => item.Id == userId))
            {
                return;
            }

            DbContext.Users.Add(CreateUser(userId, email));
            await DbContext.SaveChangesAsync();
        }

        public async Task SeedMemberAsync(int memberId, string memberUserId)
        {
            if (!await DbContext.Users.AnyAsync(item => item.Id == memberUserId))
            {
                DbContext.Users.Add(new ApplicationUser
                {
                    Id = memberUserId,
                    UserName = $"{memberUserId}@example.com",
                    NormalizedUserName = $"{memberUserId}@example.com".ToUpperInvariant(),
                    Email = $"{memberUserId}@example.com",
                    NormalizedEmail = $"{memberUserId}@example.com".ToUpperInvariant(),
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    FirstName = "Member",
                    LastName = memberId.ToString()
                });
            }

            if (!await DbContext.Members.AnyAsync(item => item.Id == memberId))
            {
                DbContext.Members.Add(new Member
                {
                    Id = memberId,
                    ApplicationUserId = memberUserId,
                    FullName = $"Member {memberId}",
                    IsActive = true
                });
            }

            if (!await DbContext.ChargeTypes.AnyAsync(item => item.Id == 1))
            {
                DbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
                {
                    Id = 1,
                    Name = "Test charge",
                    Code = "TEST-CHARGE",
                    IsActive = true
                });
            }

            await DbContext.SaveChangesAsync();
        }

        public async Task SeedPlotOwnershipAsync(int memberId, int plotId, string plotNumber)
        {
            if (!await DbContext.Plots.AnyAsync(item => item.Id == plotId))
            {
                DbContext.Plots.Add(new Plot
                {
                    Id = plotId,
                    Number = plotNumber,
                    Address = $"Plot {plotId}",
                    IsActive = true
                });
            }

            if (!await DbContext.PlotOwnerships.AnyAsync(item => item.MemberId == memberId && item.PlotId == plotId))
            {
                DbContext.PlotOwnerships.Add(new PlotOwnership
                {
                    Id = plotId,
                    MemberId = memberId,
                    PlotId = plotId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = true
                });
            }

            await DbContext.SaveChangesAsync();
        }

        public async Task<Charge> SeedChargeAsync(int plotId, decimal amount, DateOnly chargeDate, string? description = null)
        {
            var charge = new Charge
            {
                PlotId = plotId,
                ChargeTypeId = 1,
                Amount = amount,
                ChargeDate = chargeDate,
                Description = description,
                CreatedAtUtc = DateTime.UtcNow
            };

            DbContext.Charges.Add(charge);
            await DbContext.SaveChangesAsync();
            return charge;
        }

        public async Task<Payment> SeedPaymentAsync(int plotId, decimal amount, DateOnly paymentDate)
        {
            var payment = new Payment
            {
                PlotId = plotId,
                Amount = amount,
                PaymentDate = paymentDate,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            };

            DbContext.Payments.Add(payment);
            await DbContext.SaveChangesAsync();
            return payment;
        }

        public async Task SeedPaymentAllocationAsync(long paymentId, long chargeId, decimal amount)
        {
            DbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = paymentId,
                ChargeId = chargeId,
                Amount = amount
            });
            await DbContext.SaveChangesAsync();
        }

        public async Task<int> SeedElectricityReadingWithMeterAsync(int memberId, int plotId, long chargeId)
        {
            var meter = new MemberElectricityMeter
            {
                Id = plotId + 5000,
                MemberId = memberId,
                BillingPlotId = plotId,
                Name = $"Meter-{plotId}",
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            DbContext.MemberElectricityMeters.Add(meter);
            await DbContext.SaveChangesAsync();

            var plot = await DbContext.Plots.SingleAsync(item => item.Id == plotId);
            plot.MemberElectricityMeterId = meter.Id;

            DbContext.MemberElectricityReadings.Add(new MemberElectricityReading
            {
                MemberElectricityMeterId = meter.Id,
                ReadingDate = new DateOnly(2026, 8, 1),
                CurrentReading = 150m,
                CurrentNightReading = 40m,
                AppliedMemberRate = 5.00m,
                AppliedMemberNightRate = 2.50m,
                Amount = 125m,
                ChargeId = chargeId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            await DbContext.SaveChangesAsync();
            return meter.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
