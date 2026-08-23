#if WEB_TESTS
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
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using AdminFinanceModel = Neftyanik.Portal.Web.Pages.Administration.Members.FinanceModel;
using CancelPaymentPageModel = Neftyanik.Portal.Web.Pages.Administration.Members.Finance.CancelPaymentModel;
using MemberPlotFinanceModel = Neftyanik.Portal.Web.Pages.Member.Plots.Finance.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PaymentCancellationPageTests
{
    [Fact]
    public async Task OnPostCancelPaymentAsync_CancelsPaymentAndRedirectsToMemberFinance()
    {
        await using var testContext = await PaymentCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, memberUserId: "member-user", plotId: 101, chargeAmount: 200m);
        var payment = await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 120m,
            paymentDate: new DateOnly(2026, 8, 1),
            paymentMethod: PaymentMethod.Cash,
            referenceNumber: "RCPT-1",
            description: "Тестовый платеж",
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 120m)]);

        var model = new CancelPaymentPageModel(testContext.DbContext, testContext.CreatePaymentService())
        {
            Input = new CancelPaymentPageModel.InputModel
            {
                CancellationReason = "Платеж внесен вручную ошибочно."
            },
            PageContext = TestPageModelContext.CreatePageContext("admin-user", "admin@example.com")
        };
        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        testContext.HttpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(1, payment.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);
        Assert.Equal(1, redirect.RouteValues!["id"]);

        var storedPayment = await testContext.DbContext.Payments.AsNoTracking().SingleAsync();
        Assert.NotNull(storedPayment.CancelledAtUtc);
        Assert.Equal("Платеж внесен вручную ошибочно.", storedPayment.CancellationReason);
        Assert.Single(await testContext.DbContext.PaymentAllocations.AsNoTracking().ToListAsync());
        Assert.Single(await testContext.DbContext.FinancialAuditLogs.AsNoTracking().Where(item => item.EntityType == nameof(Payment) && item.Action == FinancialAuditLogActions.Cancelled).ToListAsync());
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_CancelledPaymentRemainsVisibleAndExcludedFromTotals()
    {
        await using var testContext = await PaymentCancellationTestContext.CreateAsync();
        await testContext.SeedAdminUserAsync("admin-user", "admin@example.com");
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, memberUserId: "member-user", plotId: 101, chargeAmount: 100m);
        await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 40m,
            paymentDate: new DateOnly(2026, 8, 1),
            paymentMethod: PaymentMethod.Cash,
            referenceNumber: null,
            description: "Активный платеж",
            cancellationReason: null,
            cancelledAtUtc: null,
            allocations: [(101L, 40m)]);
        await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 60m,
            paymentDate: new DateOnly(2026, 8, 2),
            paymentMethod: PaymentMethod.Card,
            referenceNumber: null,
            description: "Отмененный платеж",
            cancellationReason: "Ошибочный ввод",
            cancelledAtUtc: DateTime.UtcNow.AddMinutes(-5),
            allocations: [(101L, 60m)]);

        using var userManager = testContext.CreateUserManager();
        var model = new AdminFinanceModel(
            testContext.DbContext,
            new MemberElectricityService(testContext.DbContext, new FinancialAuditService(testContext.DbContext, testContext.HttpContextAccessor)),
            userManager)
        {
            ChargePage = 1,
            PaymentPage = 1
        };

        var result = await model.OnGetAsync(1, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(40m, model.Member.TotalPayments);
        Assert.Single(model.Plots);
        Assert.Equal(40m, model.Plots[0].Payments);
        Assert.Equal(60m, model.Plots[0].Balance);
        Assert.Equal(2, model.Payments.Count);
        Assert.Contains(model.Payments, payment => payment.IsCancelled && payment.CancellationReason == "Ошибочный ввод" && payment.CancelledAtUtc.HasValue);
    }

    [Fact]
    public async Task OnGetMemberPlotFinance_CancelledPaymentRemainsVisibleAndExcludedFromTotals()
    {
        await using var testContext = await PaymentCancellationTestContext.CreateAsync();
        await testContext.SeedMemberWithPlotAndChargeAsync(memberId: 1, memberUserId: "member-user", plotId: 101, chargeAmount: 100m);
        await testContext.SeedPaymentAsync(
            plotId: 101,
            amount: 100m,
            paymentDate: new DateOnly(2026, 8, 1),
            paymentMethod: PaymentMethod.Cash,
            referenceNumber: "RCPT-10",
            description: "Ошибочный платеж",
            cancellationReason: "Отменен бухгалтером",
            cancelledAtUtc: DateTime.UtcNow.AddMinutes(-10),
            allocations: [(101L, 100m)]);

        using var userManager = testContext.CreateUserManager();
        var model = new MemberPlotFinanceModel(testContext.DbContext, userManager)
        {
            PageContext = TestPageModelContext.CreatePageContext("member-user", "member@example.com"),
            ChargePage = 1,
            PaymentPage = 1
        };

        var result = await model.OnGetAsync(101, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(0m, model.Plot.ActivePaymentsTotal);
        Assert.Equal(100m, model.Plot.Balance);
        var payment = Assert.Single(model.Payments);
        Assert.True(payment.IsCancelled);
        Assert.Equal("Отменен бухгалтером", payment.CancellationReason);
        Assert.NotNull(payment.CancelledAtUtc);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_ShowsCancelActionForActivePaymentOnly()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 2201;
        const int plotId = 2202;

        long activePaymentId = 0;
        long cancelledPaymentId = 0;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-cancel@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Payment Cancellation Member",
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-2202",
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
                Id = 2203,
                Name = "Тестовое начисление",
                Code = "TEST-CANCEL",
                IsActive = true
            });
            dbContext.Charges.Add(new Charge
            {
                Id = 2204,
                PlotId = plotId,
                ChargeTypeId = 2203,
                Amount = 100m,
                ChargeDate = new DateOnly(2026, 8, 1),
                CreatedAtUtc = DateTime.UtcNow
            });

            var activePayment = new Payment
            {
                PlotId = plotId,
                Amount = 40m,
                PaymentDate = new DateOnly(2026, 8, 2),
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            };
            var cancelledPayment = new Payment
            {
                PlotId = plotId,
                Amount = 60m,
                PaymentDate = new DateOnly(2026, 8, 3),
                PaymentMethod = PaymentMethod.Card,
                CancelledAtUtc = DateTime.UtcNow,
                CancellationReason = "Ошибочный ввод",
                CreatedAtUtc = DateTime.UtcNow
            };

            dbContext.Payments.AddRange(activePayment, cancelledPayment);
            await dbContext.SaveChangesAsync();

            activePaymentId = activePayment.Id;
            cancelledPaymentId = cancelledPayment.Id;
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/Payments/{activePaymentId}/Cancel", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/Administration/Members/Finance/{memberId}/Payments/{cancelledPaymentId}/Cancel", html, StringComparison.Ordinal);
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

    private sealed class PaymentCancellationTestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private PaymentCancellationTestContext(SqliteConnection connection, ApplicationDbContext dbContext, HttpContextAccessor httpContextAccessor)
        {
            _connection = connection;
            DbContext = dbContext;
            HttpContextAccessor = httpContextAccessor;
        }

        public ApplicationDbContext DbContext { get; }

        public HttpContextAccessor HttpContextAccessor { get; }

        public static async Task<PaymentCancellationTestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;

            var dbContext = new ApplicationDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();

            return new PaymentCancellationTestContext(connection, dbContext, new HttpContextAccessor());
        }

        public PaymentService CreatePaymentService()
        {
            return new PaymentService(DbContext, new FinancialAuditService(DbContext, HttpContextAccessor));
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

        public async Task SeedAdminUserAsync(string userId, string email)
        {
            if (await DbContext.Users.AnyAsync(item => item.Id == userId))
            {
                return;
            }

            DbContext.Users.Add(CreateUser(userId, email));
            await DbContext.SaveChangesAsync();
        }

        public async Task SeedMemberWithPlotAndChargeAsync(int memberId, string memberUserId, int plotId, decimal chargeAmount)
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
            DbContext.Members.Add(new Member
            {
                Id = memberId,
                ApplicationUserId = memberUserId,
                FullName = $"Member {memberId}",
                IsActive = true
            });
            DbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = $"P-{plotId}",
                Address = $"Plot {plotId}",
                IsActive = true
            });
            DbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = plotId,
                MemberId = memberId,
                PlotId = plotId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            DbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
            {
                Id = 1,
                Name = "Test charge",
                Code = "TEST-CHARGE",
                IsActive = true
            });
            DbContext.Charges.Add(new Charge
            {
                Id = plotId,
                PlotId = plotId,
                ChargeTypeId = 1,
                Amount = chargeAmount,
                ChargeDate = new DateOnly(2026, 1, 1),
                CreatedAtUtc = DateTime.UtcNow
            });

            await DbContext.SaveChangesAsync();
        }

        public async Task<Payment> SeedPaymentAsync(
            int plotId,
            decimal amount,
            DateOnly paymentDate,
            PaymentMethod paymentMethod,
            string? referenceNumber,
            string? description,
            string? cancellationReason,
            DateTime? cancelledAtUtc,
            params (long ChargeId, decimal Amount)[] allocations)
        {
            var payment = new Payment
            {
                PlotId = plotId,
                PaymentDate = paymentDate,
                Amount = amount,
                PaymentMethod = paymentMethod,
                ReferenceNumber = referenceNumber,
                Description = description,
                CancellationReason = cancellationReason,
                CancelledAtUtc = cancelledAtUtc,
                CreatedAtUtc = DateTime.UtcNow
            };

            DbContext.Payments.Add(payment);
            await DbContext.SaveChangesAsync();

            if (allocations.Length > 0)
            {
                DbContext.PaymentAllocations.AddRange(allocations.Select(allocation => new PaymentAllocation
                {
                    PaymentId = payment.Id,
                    ChargeId = allocation.ChargeId,
                    Amount = allocation.Amount
                }));
                await DbContext.SaveChangesAsync();
            }

            return payment;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
#endif
