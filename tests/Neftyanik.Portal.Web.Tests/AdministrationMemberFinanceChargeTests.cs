using System.Net;
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
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Pages.Administration.Members;
using Neftyanik.Portal.Web.Pages.Administration.Members.Finance;
using FinanceIndexModel = Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationMemberFinanceChargeTests
{
    [Fact]
    public async Task OnPostCreateChargeAsync_CreatesExactlyOneChargeAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const string adminUserName = "admin-charge@example.com";
        const int memberId = 2601;
        const int plotId = 2602;
        const int chargeTypeId = 2603;

        dbContext.Users.Add(CreateUser(adminUserId, adminUserName));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Charge Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-2602", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.ChargeTypes.Add(new ChargeType { Id = chargeTypeId, Name = "Членский взнос", IsActive = true, DefaultAmount = 450m });
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

        var model = new CreateChargeModel(dbContext, userManager)
        {
            Input = new MemberChargeInputModel
            {
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 450m,
                ChargeDate = new DateOnly(2026, 3, 1),
                DueDate = new DateOnly(2026, 3, 15),
                Description = "Ручное начисление"
            },
            PageContext = CreatePageContext(adminUserId, adminUserName)
        };
        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);

        var result = await model.OnPostAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var charge = await dbContext.Charges.AsNoTracking().SingleAsync();
        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();

        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(Charge), auditEntry.EntityType);
        Assert.Equal(charge.Id.ToString(), auditEntry.EntityId);
        Assert.Equal(adminUserId, auditEntry.UserId);
        Assert.Equal(adminUserName, auditEntry.UserName);
        Assert.Contains("\"MemberId\":2601", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"PlotId\":2602", auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_ForMemberWithActivePlots_ShowsChargeCreationActions()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 201;
        const int firstPlotId = 301;
        const int secondPlotId = 302;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin1@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Admin Finance Member",
                IsActive = true
            });
            dbContext.Plots.AddRange(
                new Plot
                {
                    Id = firstPlotId,
                    Number = "P-301",
                    Address = "Finance Plot 301",
                    IsActive = true
                },
                new Plot
                {
                    Id = secondPlotId,
                    Number = "P-302",
                    Address = "Finance Plot 302",
                    IsActive = true
                });
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

            await dbContext.SaveChangesAsync();

            dbContext.MemberElectricityMeters.AddRange(
                new MemberElectricityMeter
                {
                    Id = 1,
                    MemberId = memberId,
                    Name = "Счетчик 1",
                    BillingPlotId = firstPlotId,
                    IsActive = true,
                    Readings =
                    [
                        new MemberElectricityReading
                        {
                            ReadingDate = new DateOnly(2026, 1, 1),
                            CurrentReading = 10m,
                            IsInitialReading = true
                        }
                    ]
                },
                new MemberElectricityMeter
                {
                    Id = 2,
                    MemberId = memberId,
                    Name = "Счетчик 2",
                    BillingPlotId = secondPlotId,
                    IsActive = true
                });

            await dbContext.SaveChangesAsync();

            var plots = await dbContext.Plots.OrderBy(plot => plot.Id).ToListAsync();
            plots[0].MemberElectricityMeterId = 1;
            plots[1].MemberElectricityMeterId = 2;
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/CreateCharge", html, StringComparison.Ordinal);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/RegisterPayment", html, StringComparison.Ordinal);
        Assert.Contains("#electricityReadingModal", html, StringComparison.Ordinal);
        Assert.Contains("#electricityInitializationModal", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_ForDayNightMember_ShowsNightReadingInitializationFields()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 211;
        const int plotId = 311;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-daynight@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Day Night Member",
                ElectricityMeterType = Neftyanik.Portal.Domain.Enums.MemberElectricityMeterType.DayNight,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-311",
                Address = "Finance Plot 311",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 11,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"ReadingInput.CurrentNightReading\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"SetupInput.CurrentNightReading\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"InitializationInput.CurrentNightReading\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_RendersInlineCancelledStateWithoutStatusColumns()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-finance-inline-status";
        const int memberId = 221;
        const int plotId = 321;
        const int chargeTypeId = 421;
        const long cancelledChargeId = 521;
        const long activeChargeId = 522;
        const long cancelledPaymentId = 621;
        const long activePaymentId = 622;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-finance-inline-status@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Inline Status Member",
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-321",
                Address = "Finance Plot 321",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 21,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            dbContext.ChargeTypes.Add(new ChargeType
            {
                Id = chargeTypeId,
                Name = "Членский взнос",
                IsActive = true,
                DefaultAmount = 500m
            });
            dbContext.Charges.AddRange(
                new Charge
                {
                    Id = cancelledChargeId,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    ChargeDate = new DateOnly(2026, 4, 1),
                    Amount = 500m,
                    DueDate = new DateOnly(2026, 4, 15),
                    Description = "Cancelled charge description",
                    CancelledAtUtc = new DateTime(2026, 4, 20, 10, 30, 0, DateTimeKind.Utc),
                    CancellationReason = "Cancelled charge reason"
                },
                new Charge
                {
                    Id = activeChargeId,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    ChargeDate = new DateOnly(2026, 5, 1),
                    Amount = 250m,
                    DueDate = new DateOnly(2026, 5, 15),
                    Description = "Active charge description"
                });
            dbContext.Payments.AddRange(
                new Payment
                {
                    Id = cancelledPaymentId,
                    MemberId = memberId,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2026, 4, 2),
                    Amount = 300m,
                    PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.Cash,
                    ReferenceNumber = "PAY-CANCELLED",
                    Description = "Cancelled payment description",
                    CancelledAtUtc = new DateTime(2026, 4, 22, 9, 15, 0, DateTimeKind.Utc),
                    CancellationReason = "Cancelled payment reason",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Payment
                {
                    Id = activePaymentId,
                    MemberId = memberId,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2026, 5, 2),
                    Amount = 125m,
                    PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.Card,
                    ReferenceNumber = "PAY-ACTIVE",
                    Description = "Active payment description",
                    CreatedAtUtc = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/Finance");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chargesSection = ExtractSection(html, "<h2 class=\"h5 mb-0\">Начисления</h2>", "<h2 class=\"h5 mb-0\">Платежи</h2>");
        var paymentsSection = ExtractSection(html, "<h2 class=\"h5 mb-0\">Платежи</h2>", "<h2 class=\"h5 mb-0\">Электросчётчики</h2>");

        Assert.DoesNotContain("<th>Статус</th>", chargesSection, StringComparison.Ordinal);
        Assert.DoesNotContain("<th>Статус</th>", paymentsSection, StringComparison.Ordinal);

        Assert.Contains("Отменено", chargesSection, StringComparison.Ordinal);
        Assert.Contains("Отменено", paymentsSection, StringComparison.Ordinal);
        Assert.Contains("Cancelled charge description", chargesSection, StringComparison.Ordinal);
        Assert.Contains("Cancelled charge reason", chargesSection, StringComparison.Ordinal);
        Assert.Contains("Cancelled payment description", paymentsSection, StringComparison.Ordinal);
        Assert.Contains("Cancelled payment reason", paymentsSection, StringComparison.Ordinal);

        Assert.Contains($"/Payments/{cancelledPaymentId}/Receipt?memberId={memberId}", paymentsSection, StringComparison.Ordinal);
        Assert.Contains($"/Payments/{activePaymentId}/Receipt?memberId={memberId}", paymentsSection, StringComparison.Ordinal);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/Charges/{activeChargeId}/Cancel", chargesSection, StringComparison.Ordinal);
        Assert.Contains($"/Administration/Members/Finance/{memberId}/Payments/{activePaymentId}/Cancel", paymentsSection, StringComparison.Ordinal);
        Assert.Contains("colspan=\"6\"", chargesSection, StringComparison.Ordinal);
        Assert.Contains("colspan=\"6\"", paymentsSection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberCreateCharge_ShowsOnlyActiveMemberPlotsAndActiveChargeTypes()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 401;
        const int activePlotId = 501;
        const int inactivePlotId = 502;
        const int activeChargeTypeId = 601;
        const int archivedChargeTypeId = 602;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin2@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Charge Page Member",
                IsActive = true
            });
            dbContext.Plots.AddRange(
                new Plot { Id = activePlotId, Number = "P-501", Address = "Active Plot 501", IsActive = true },
                new Plot { Id = inactivePlotId, Number = "P-502", Address = "Historical Plot 502", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = activePlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = inactivePlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2021, 1, 1),
                    IsPrimaryContact = false
                });
            dbContext.ChargeTypes.AddRange(
                new ChargeType { Id = activeChargeTypeId, Name = "Членский взнос", IsActive = true },
                new ChargeType { Id = archivedChargeTypeId, Name = "Архивный тип", IsActive = false });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/CreateCharge?plotId={activePlotId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.Amount\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Input.ChargeDate\"", html, StringComparison.Ordinal);
        Assert.Contains("P-501", html, StringComparison.Ordinal);
        Assert.DoesNotContain("P-502", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberRegisterPayment_ShowsOnlyActiveMemberPlots()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int memberId = 701;
        const int activePlotId = 801;
        const int inactivePlotId = 802;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin3@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Payment Page Member",
                IsActive = true
            });
            dbContext.Plots.AddRange(
                new Plot { Id = activePlotId, Number = "P-801", Address = "Active Plot 801", IsActive = true },
                new Plot { Id = inactivePlotId, Number = "P-802", Address = "Historical Plot 802", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = activePlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = inactivePlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2021, 1, 1),
                    IsPrimaryContact = false
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Administration/Members/Finance/{memberId}/RegisterPayment?plotId={activePlotId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.Amount\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"Input.PaymentMethod\"", html, StringComparison.Ordinal);
        Assert.Contains("P-801", html, StringComparison.Ordinal);
        Assert.DoesNotContain("P-802", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostAdministrationMemberRegisterPayment_AutomaticallyAllocatesPaymentAcrossAllMemberPlots()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 851;
        const int firstPlotId = 861;
        const int secondPlotId = 862;
        const int chargeTypeId = 863;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-payment-allocation@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Allocated Payment Member", IsActive = true });
        dbContext.Plots.AddRange(
            new Plot { Id = firstPlotId, Number = "P-861", IsActive = true },
            new Plot { Id = secondPlotId, Number = "P-862", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership { Id = 1, PlotId = firstPlotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
            new PlotOwnership { Id = 2, PlotId = secondPlotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = false });
        dbContext.ChargeTypes.Add(new ChargeType { Id = chargeTypeId, Name = "Тестовый тип", IsActive = true, DefaultAmount = 100m });
        dbContext.Charges.AddRange(
            new Charge { Id = 1, PlotId = firstPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 1) },
            new Charge { Id = 2, PlotId = secondPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 2) });
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

        var model = new RegisterPaymentModel(
            dbContext,
            new PaymentService(dbContext, new FinancialAuditService(dbContext, new Microsoft.AspNetCore.Http.HttpContextAccessor())),
            userManager)
        {
            Input = new MemberPaymentInputModel
            {
                PlotId = firstPlotId,
                PaymentDate = new DateOnly(2026, 1, 10),
                Amount = 150m,
                PaymentMethod = Domain.Enums.PaymentMethod.Cash,
                Description = "Оплата по нескольким участкам"
            }
        };

        model.PageContext = CreatePageContext(adminUserId);

        var result = await model.OnPostAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var payment = await dbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Equal(firstPlotId, payment.PlotId);
        Assert.Equal(150m, payment.Amount);

        var allocations = await dbContext.PaymentAllocations
            .AsNoTracking()
            .OrderBy(item => item.ChargeId)
            .ToListAsync();

        Assert.Equal(2, allocations.Count);
        Assert.Collection(allocations,
            allocation =>
            {
                Assert.Equal(1L, allocation.ChargeId);
                Assert.Equal(100m, allocation.Amount);
            },
            allocation =>
            {
                Assert.Equal(2L, allocation.ChargeId);
                Assert.Equal(50m, allocation.Amount);
            });
    }

    [Fact]
    public async Task OnGetAdministrationMemberRegisterPayment_UsesSameCurrentFundsAsFinanceOverview()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var currentYear = DateTime.Today.Year;
        var acceptedAt = new DateOnly(currentYear, 7, 1);
        var beforeInitialization = acceptedAt.AddDays(-1);
        var afterInitialization = acceptedAt.AddDays(2);
        const string adminUserId = "admin-user";
        const int memberId = 865;
        const int plotId = 866;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-payment-cash@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Cash Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-866", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.Payments.AddRange(
            new Payment { Id = 1, MemberId = memberId, PlotId = plotId, PaymentDate = beforeInitialization, Amount = 200m, PaymentMethod = Domain.Enums.PaymentMethod.Cash, CreatedAtUtc = DateTime.UtcNow },
            new Payment { Id = 2, MemberId = memberId, PlotId = plotId, PaymentDate = afterInitialization, Amount = 60m, PaymentMethod = Domain.Enums.PaymentMethod.Cash, CreatedAtUtc = DateTime.UtcNow },
            new Payment { Id = 3, MemberId = memberId, PlotId = plotId, PaymentDate = beforeInitialization, Amount = 50m, PaymentMethod = Domain.Enums.PaymentMethod.Card, CreatedAtUtc = DateTime.UtcNow },
            new Payment { Id = 4, MemberId = memberId, PlotId = plotId, PaymentDate = afterInitialization, Amount = 40m, PaymentMethod = Domain.Enums.PaymentMethod.Card, CreatedAtUtc = DateTime.UtcNow });
        dbContext.Expenses.AddRange(
            new Expense
            {
                Id = 1,
                ExpenseCategoryId = 1,
                ExpenseDate = beforeInitialization,
                Amount = 80m,
                Description = "Expense before initialization",
                CreatedByUserId = adminUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                IsCancelled = false
            },
            new Expense
            {
                Id = 2,
                ExpenseCategoryId = 1,
                ExpenseDate = afterInitialization,
                Amount = 30m,
                Description = "Expense after initialization",
                CreatedByUserId = adminUserId,
                CreatedAt = DateTimeOffset.UtcNow,
                IsCancelled = false
            });
        dbContext.SystemSettings.Add(new SystemSetting
        {
            Id = 1,
            Key = "Finance.CashInitialization",
            Value = "{\"Amount\":500,\"AcceptedAt\":\"" + acceptedAt.ToString("yyyy-MM-dd") + "\",\"AcceptedFrom\":\"Кассир\",\"AdvancePaymentsAmount\":25}",
            Description = "Initial cash amount configured from finance settings.",
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedByUserId = adminUserId
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

        var model = new RegisterPaymentModel(
            dbContext,
            new PaymentService(dbContext, new FinancialAuditService(dbContext, new Microsoft.AspNetCore.Http.HttpContextAccessor())),
            userManager);
        var financeModel = new FinanceIndexModel(dbContext);

        var result = await model.OnGetAsync(memberId, null, CancellationToken.None);
        await financeModel.OnGetAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(545m, model.CurrentCashAmount);
        Assert.Equal(model.CurrentCashAmount, financeModel.Summary.CurrentCashAmount);
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_UsesPaymentAllocationsForPlotBalances()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 871;
        const int firstPlotId = 881;
        const int secondPlotId = 882;
        const int chargeTypeId = 883;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-member-finance@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Finance Allocation Member", IsActive = true });
        dbContext.Plots.AddRange(
            new Plot { Id = firstPlotId, Number = "P-881", IsActive = true },
            new Plot { Id = secondPlotId, Number = "P-882", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership { Id = 1, PlotId = firstPlotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
            new PlotOwnership { Id = 2, PlotId = secondPlotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = false });
        dbContext.ChargeTypes.Add(new ChargeType { Id = chargeTypeId, Name = "Тестовый тип", IsActive = true, DefaultAmount = 100m });
        dbContext.Charges.AddRange(
            new Charge { Id = 11, PlotId = firstPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 1) },
            new Charge { Id = 12, PlotId = secondPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 2) });
        dbContext.Payments.Add(new Payment
        {
            Id = 21,
            MemberId = memberId,
            PlotId = firstPlotId,
            Amount = 150m,
            PaymentDate = new DateOnly(2026, 1, 10),
            PaymentMethod = Domain.Enums.PaymentMethod.Cash
        });
        dbContext.PaymentAllocations.AddRange(
            new PaymentAllocation { PaymentId = 21, ChargeId = 11, Amount = 100m },
            new PaymentAllocation { PaymentId = 21, ChargeId = 12, Amount = 50m });

        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = 31,
            MemberId = memberId,
            Name = "Meter",
            BillingPlotId = firstPlotId,
            IsActive = true
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new FinanceModel(dbContext, service, userManager)
        {
            ChargePage = 1,
            PaymentPage = 1
        };

        var result = await model.OnGetAsync(memberId, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Equal(150m, model.Member.TotalPayments);
        Assert.Equal(50m, model.Member.Balance);
        Assert.Collection(model.Plots.OrderBy(item => item.PlotId),
            plot =>
            {
                Assert.Equal(firstPlotId, plot.PlotId);
                Assert.Equal(100m, plot.Charges);
                Assert.Equal(100m, plot.Payments);
                Assert.Equal(0m, plot.Balance);
            },
            plot =>
            {
                Assert.Equal(secondPlotId, plot.PlotId);
                Assert.Equal(100m, plot.Charges);
                Assert.Equal(50m, plot.Payments);
                Assert.Equal(50m, plot.Balance);
            });
    }

    [Fact]
    public async Task GetAdministrationMembers_ShowsDebtAndOverpaymentInTable()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const int debtorMemberId = 901;
        const int overpaidMemberId = 902;
        const int debtorPlotId = 911;
        const int overpaidPlotId = 912;
        const int chargeTypeId = 913;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin4@example.com"));
            dbContext.Members.AddRange(
                new Member { Id = debtorMemberId, FullName = "Member With Debt", IsActive = true },
                new Member { Id = overpaidMemberId, FullName = "Member With Overpayment", IsActive = true });
            dbContext.Plots.AddRange(
                new Plot { Id = debtorPlotId, Number = "P-911", IsActive = true },
                new Plot { Id = overpaidPlotId, Number = "P-912", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership { Id = 1, PlotId = debtorPlotId, MemberId = debtorMemberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
                new PlotOwnership { Id = 2, PlotId = overpaidPlotId, MemberId = overpaidMemberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
            dbContext.ChargeTypes.Add(new ChargeType { Id = chargeTypeId, Name = "Тестовое начисление", IsActive = true, DefaultAmount = 100m });
            dbContext.Charges.AddRange(
                new Charge { PlotId = debtorPlotId, ChargeTypeId = chargeTypeId, Amount = 1000m, ChargeDate = new DateOnly(2026, 1, 15) },
                new Charge { PlotId = overpaidPlotId, ChargeTypeId = chargeTypeId, Amount = 200m, ChargeDate = new DateOnly(2026, 1, 15) });
            dbContext.Payments.AddRange(
                new Payment { MemberId = debtorMemberId, PlotId = debtorPlotId, Amount = 400m, PaymentDate = new DateOnly(2026, 1, 20) },
                new Payment { MemberId = overpaidMemberId, PlotId = overpaidPlotId, Amount = 500m, PaymentDate = new DateOnly(2026, 1, 20) });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");

        var response = await client.GetAsync("/Administration/Members");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Member With Debt", html, StringComparison.Ordinal);
        Assert.Contains("600,00", html, StringComparison.Ordinal);
        Assert.Contains("Member With Overpayment", html, StringComparison.Ordinal);
        Assert.Contains("300,00", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnGetAdministrationMembers_LoadsDebtAndOverpayment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int debtorMemberId = 1001;
        const int overpaidMemberId = 1002;
        const int debtorPlotId = 1011;
        const int overpaidPlotId = 1012;
        const int chargeTypeId = 1013;

        dbContext.Members.AddRange(
            new Member { Id = debtorMemberId, FullName = "Direct Debt Member", IsActive = true },
            new Member { Id = overpaidMemberId, FullName = "Direct Overpayment Member", IsActive = true });
        dbContext.Plots.AddRange(
            new Plot { Id = debtorPlotId, Number = "P-1011", IsActive = true },
            new Plot { Id = overpaidPlotId, Number = "P-1012", IsActive = true });
        dbContext.PlotOwnerships.AddRange(
            new PlotOwnership { Id = 1, PlotId = debtorPlotId, MemberId = debtorMemberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true },
            new PlotOwnership { Id = 2, PlotId = overpaidPlotId, MemberId = overpaidMemberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.ChargeTypes.Add(new ChargeType { Id = chargeTypeId, Name = "Тест", IsActive = true, DefaultAmount = 100m });
        dbContext.Charges.AddRange(
            new Charge { PlotId = debtorPlotId, ChargeTypeId = chargeTypeId, Amount = 1000m, ChargeDate = new DateOnly(2026, 1, 15) },
            new Charge { PlotId = overpaidPlotId, ChargeTypeId = chargeTypeId, Amount = 200m, ChargeDate = new DateOnly(2026, 1, 15) });
        dbContext.Payments.AddRange(
            new Payment { MemberId = debtorMemberId, PlotId = debtorPlotId, Amount = 400m, PaymentDate = new DateOnly(2026, 1, 20) },
            new Payment { MemberId = overpaidMemberId, PlotId = overpaidPlotId, Amount = 500m, PaymentDate = new DateOnly(2026, 1, 20) });
        await dbContext.SaveChangesAsync();

        var model = new IndexModel(dbContext);
        await model.OnGetAsync(CancellationToken.None);

        Assert.Equal(2, model.Members.Count);
        Assert.Contains(model.Members, member => member.FullName == "Direct Debt Member" && member.Balance == 600m);
        Assert.Contains(model.Members, member => member.FullName == "Direct Overpayment Member" && member.Balance == -300m);
    }

    [Fact]
    public async Task OnPostInitialElectricityReadingAsync_CreatesExactlyOneReadingAuditEntry()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const string adminUserName = "admin-initial@example.com";
        const int memberId = 1151;
        const int plotId = 1152;
        const int meterId = 1153;

        dbContext.Users.Add(CreateUser(adminUserId, adminUserName));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Initial Reading Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1152", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Initial Meter",
            BillingPlotId = plotId,
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var initialPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        initialPlot.MemberElectricityMeterId = meterId;
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.InitialModel(dbContext, service, userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.ReadingInputModel
            {
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 111.222m
            },
            PageContext = CreatePageContext(adminUserId, adminUserName)
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(meterId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Electricity/Meters/Details", redirect.PageName);

        var reading = await dbContext.MemberElectricityReadings.AsNoTracking().SingleAsync();
        var auditEntry = await dbContext.FinancialAuditLogs.AsNoTracking().SingleAsync();

        Assert.True(reading.IsInitialReading);
        Assert.Null(reading.AppliedMemberNightRate);
        Assert.Equal(FinancialAuditLogActions.Created, auditEntry.Action);
        Assert.Equal(nameof(MemberElectricityReading), auditEntry.EntityType);
        Assert.Equal(reading.Id.ToString(), auditEntry.EntityId);
        Assert.Equal(adminUserId, auditEntry.UserId);
        Assert.Equal(adminUserName, auditEntry.UserName);
        Assert.Contains("начальное показание", auditEntry.Description ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"AppliedMemberNightRate\":null", auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"ReadingId\":" + reading.Id, auditEntry.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"RelatedChargeId\":null", auditEntry.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostInitializeElectricityAsync_CreatesInitialReadingAndOpeningDebtCharge()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1201;
        const int plotId = 1202;
        const int meterId = 1203;

        dbContext.Users.Add(CreateUser(adminUserId, "admin5@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Init Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1202", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Init Meter",
            BillingPlotId = plotId,
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var initializationPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        initializationPlot.MemberElectricityMeterId = meterId;
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new FinanceModel(dbContext, service, userManager)
        {
            InitializationInput = new MemberElectricityInitializationInputModel
            {
                MeterId = meterId,
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 321.123m,
                OpeningDebtAmount = 450m
            },
            ChargePage = 1,
            PaymentPage = 1
        };

        model.PageContext = CreatePageContext(adminUserId, "admin5@example.com");
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostInitializeElectricityAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var reading = await dbContext.MemberElectricityReadings.AsNoTracking().SingleAsync();
        Assert.True(reading.IsInitialReading);
        Assert.Equal(321.123m, reading.CurrentReading);
        Assert.Null(reading.AppliedMemberNightRate);

        var charge = await dbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Equal(450m, charge.Amount);
        Assert.Equal(plotId, charge.PlotId);
        Assert.Contains("Начальная задолженность по электроэнергии", charge.Description, StringComparison.Ordinal);

        var auditEntries = await dbContext.FinancialAuditLogs
            .AsNoTracking()
            .OrderBy(item => item.EntityType)
            .ThenBy(item => item.EntityId)
            .ToListAsync();

        Assert.Equal(2, auditEntries.Count);
        Assert.Single(auditEntries, item => item.EntityType == nameof(MemberElectricityReading) && item.Action == FinancialAuditLogActions.Created && item.EntityId == reading.Id.ToString());
        var readingAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
        Assert.Equal(adminUserId, readingAudit.UserId);
        Assert.Contains("\"RelatedChargeId\":" + charge.Id, readingAudit.NewValuesJson, StringComparison.Ordinal);
        Assert.Single(auditEntries, item => item.EntityType == nameof(Charge) && item.Action == FinancialAuditLogActions.Created && item.EntityId == charge.Id.ToString());
    }

    [Fact]
    public async Task OnPostSetupElectricityAsync_CreatesMeterAndInitialReadingForMemberWithoutMeters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1251;
        const int plotId = 1252;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-setup@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Setup Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1252", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new FinanceModel(dbContext, service, userManager)
        {
            SetupInput = new MemberElectricitySetupInputModel
            {
                BillingPlotId = plotId,
                MeterNumber = "M-1252",
                Name = "Основной счётчик",
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 222.333m,
                OpeningDebtAmount = 150m
            },
            ChargePage = 1,
            PaymentPage = 1
        };

        model.PageContext = CreatePageContext(adminUserId, "admin-setup@example.com");
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostSetupElectricityAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var meter = await dbContext.MemberElectricityMeters
            .AsNoTracking()
            .Include(item => item.Plots)
            .SingleAsync();
        Assert.Equal(memberId, meter.MemberId);
        Assert.Equal(plotId, meter.BillingPlotId);
        Assert.Equal("M-1252", meter.MeterNumber);
        Assert.Single(meter.Plots);
        Assert.Equal(plotId, meter.Plots[0].Id);

        var reading = await dbContext.MemberElectricityReadings.AsNoTracking().SingleAsync();
        Assert.True(reading.IsInitialReading);
        Assert.Equal(222.333m, reading.CurrentReading);
        Assert.Null(reading.AppliedMemberNightRate);

        var charge = await dbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Equal(150m, charge.Amount);
        Assert.Equal(plotId, charge.PlotId);

        var auditEntries = await dbContext.FinancialAuditLogs
            .AsNoTracking()
            .OrderBy(item => item.EntityType)
            .ThenBy(item => item.EntityId)
            .ToListAsync();

        Assert.Equal(2, auditEntries.Count);
        var readingAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
        Assert.Equal(FinancialAuditLogActions.Created, readingAudit.Action);
        Assert.Equal(adminUserId, readingAudit.UserId);
        Assert.Contains("\"RelatedChargeId\":" + charge.Id, readingAudit.NewValuesJson, StringComparison.Ordinal);
        Assert.Single(auditEntries, item => item.EntityType == nameof(Charge) && item.Action == FinancialAuditLogActions.Created && item.EntityId == charge.Id.ToString());
    }

    [Fact]
    public async Task OnPostCreateElectricityReadingAsync_CreatesReadingAndElectricityCharge()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1301;
        const int plotId = 1302;
        const int meterId = 1303;

        dbContext.Users.Add(CreateUser(adminUserId, "admin6@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Reading Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1302", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Reading Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                }
            ]
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new DateOnly(2020, 1, 1), Rate = 5m });
        await dbContext.SaveChangesAsync();

        var readingPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        readingPlot.MemberElectricityMeterId = meterId;
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new FinanceModel(dbContext, service, userManager)
        {
            ReadingInput = new MemberElectricityReadingInputModel
            {
                MeterId = meterId,
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 130m
            },
            ChargePage = 1,
            PaymentPage = 1
        };

        model.PageContext = CreatePageContext(adminUserId, "admin6@example.com");
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostCreateElectricityReadingAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var readings = await dbContext.MemberElectricityReadings.AsNoTracking().OrderBy(item => item.ReadingDate).ToListAsync();
        Assert.Equal(2, readings.Count);
        Assert.Equal(30m, readings[1].CurrentReading - readings[0].CurrentReading);
        Assert.Equal(5m, readings[1].AppliedMemberRate);
        Assert.Null(readings[1].AppliedMemberNightRate);
        Assert.Equal(150m, readings[1].Amount);

        var charge = await dbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Equal(150m, charge.Amount);
        Assert.Equal(plotId, charge.PlotId);
        Assert.Contains("Электроэнергия", charge.Description, StringComparison.Ordinal);

        var auditEntries = await dbContext.FinancialAuditLogs
            .AsNoTracking()
            .OrderBy(item => item.EntityType)
            .ThenBy(item => item.EntityId)
            .ToListAsync();

        Assert.Equal(2, auditEntries.Count);
        var readingAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
        Assert.Equal(FinancialAuditLogActions.Created, readingAudit.Action);
        Assert.Equal(adminUserId, readingAudit.UserId);
        Assert.Equal("admin6@example.com", readingAudit.UserName);
        Assert.Contains("\"Consumption\":30", readingAudit.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"AppliedMemberNightRate\":null", readingAudit.NewValuesJson, StringComparison.Ordinal);
        Assert.Contains("\"RelatedChargeId\":" + charge.Id, readingAudit.NewValuesJson, StringComparison.Ordinal);

        var chargeAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(Charge)));
        Assert.Equal(FinancialAuditLogActions.Created, chargeAudit.Action);
        Assert.Equal(adminUserId, chargeAudit.UserId);
        Assert.Equal("admin6@example.com", chargeAudit.UserName);
    }

    [Fact]
    public async Task OnPostMemberElectricityReadingAsync_CapturesAuthenticatedMemberUserOnce()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string memberUserId = "member-user";
        const string memberUserName = "member-reading@example.com";
        const int memberId = 1304;
        const int plotId = 1305;
        const int meterId = 1306;

        dbContext.Users.Add(CreateUser(memberUserId, memberUserName));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            ApplicationUserId = memberUserId,
            FullName = "Member Reading User",
            IsActive = true
        });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1305", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Member Reading Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                }
            ]
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new DateOnly(2020, 1, 1), Rate = 5m });
        await dbContext.SaveChangesAsync();

        var memberReadingPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        memberReadingPlot.MemberElectricityMeterId = meterId;
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new Neftyanik.Portal.Web.Pages.Member.Electricity.Meters.Readings.CreateModel(dbContext, service, userManager)
        {
            Input = new Neftyanik.Portal.Web.Pages.Administration.Electricity.Meters.ReadingInputModel
            {
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 140m
            },
            PageContext = CreatePageContext(memberUserId, memberUserName)
        };

        model.TempData = TestPageModelContext.CreateTempData(model.PageContext.HttpContext);
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostAsync(meterId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Member/Electricity/Meters/Readings/Index", redirect.PageName);

        var auditEntries = await dbContext.FinancialAuditLogs
            .AsNoTracking()
            .OrderBy(item => item.EntityType)
            .ThenBy(item => item.EntityId)
            .ToListAsync();

        Assert.Equal(2, auditEntries.Count);
        var readingAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
        Assert.Equal(memberUserId, readingAudit.UserId);
        Assert.Equal(memberUserName, readingAudit.UserName);
        Assert.Contains("\"SubmittedByMember\":true", readingAudit.NewValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnPostCreateElectricityReadingAsync_DayNightMeter_PersistsAppliedNightRate_AndPreservesCalculation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1307;
        const int plotId = 1308;
        const int meterId = 1309;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-daynight-reading@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Day Night Reading Member",
            IsActive = true,
            ElectricityMeterType = Neftyanik.Portal.Domain.Enums.MemberElectricityMeterType.DayNight
        });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1308", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Day Night Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    CurrentNightReading = 40m,
                    IsInitialReading = true
                }
            ]
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff
        {
            EffectiveFrom = new DateOnly(2020, 1, 1),
            Rate = 5m,
            NightRate = 2.50m
        });
        await dbContext.SaveChangesAsync();

        var plot = await dbContext.Plots.SingleAsync(item => item.Id == plotId);
        plot.MemberElectricityMeterId = meterId;
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

        var httpContextAccessor = new HttpContextAccessor();
        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, httpContextAccessor));
        var model = new FinanceModel(dbContext, service, userManager)
        {
            ReadingInput = new MemberElectricityReadingInputModel
            {
                MeterId = meterId,
                ReadingDate = new DateOnly(2026, 2, 1),
                CurrentReading = 130m,
                CurrentNightReading = 50m
            },
            ChargePage = 1,
            PaymentPage = 1
        };

        model.PageContext = CreatePageContext(adminUserId, "admin-daynight-reading@example.com");
        httpContextAccessor.HttpContext = model.PageContext.HttpContext;

        var result = await model.OnPostCreateElectricityReadingAsync(memberId, CancellationToken.None);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("/Administration/Members/Finance", redirect.PageName);

        var readings = await dbContext.MemberElectricityReadings.AsNoTracking().OrderBy(item => item.ReadingDate).ToListAsync();
        Assert.Equal(2, readings.Count);
        Assert.Equal(5m, readings[1].AppliedMemberRate);
        Assert.Equal(2.50m, readings[1].AppliedMemberNightRate);
        Assert.Equal(175m, readings[1].Amount);

        var charge = await dbContext.Charges.AsNoTracking().SingleAsync();
        Assert.Equal(175m, charge.Amount);

        var auditEntries = await dbContext.FinancialAuditLogs.AsNoTracking().OrderBy(item => item.EntityType).ThenBy(item => item.EntityId).ToListAsync();
        Assert.Equal(2, auditEntries.Count);
        var readingAudit = Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
        Assert.Contains("\"AppliedMemberNightRate\":2.5", readingAudit.NewValuesJson, StringComparison.Ordinal);
        Assert.Single(auditEntries.Where(item => item.EntityType == nameof(MemberElectricityReading)));
    }

    [Fact]
    public async Task CreateInitialReadingAsync_DayNightMeter_PersistsNoAppliedNightRate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1314;
        const int plotId = 1315;
        const int meterId = 1316;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-daynight-initial@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Day Night Initial Member",
            IsActive = true,
            ElectricityMeterType = Neftyanik.Portal.Domain.Enums.MemberElectricityMeterType.DayNight
        });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1315", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Day Night Initial Meter",
            BillingPlotId = plotId,
            IsActive = true
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff
        {
            EffectiveFrom = new DateOnly(2020, 1, 1),
            Rate = 5m,
            NightRate = 2.50m
        });
        await dbContext.SaveChangesAsync();

        var initialPlot = await dbContext.Plots.SingleAsync(item => item.Id == plotId);
        initialPlot.MemberElectricityMeterId = meterId;
        await dbContext.SaveChangesAsync();

        var service = new MemberElectricityService(dbContext, new FinancialAuditService(dbContext, new HttpContextAccessor()));
        var result = await service.CreateInitialReadingAsync(
            new CreateMemberElectricityInitialReadingRequest(
                meterId,
                new DateOnly(2026, 2, 1),
                100m,
                50m,
                adminUserId),
            CancellationToken.None);

        Assert.True(result.Succeeded);

        var reading = await dbContext.MemberElectricityReadings.AsNoTracking().SingleAsync();
        Assert.True(reading.IsInitialReading);
        Assert.Null(reading.AppliedMemberRate);
        Assert.Null(reading.AppliedMemberNightRate);
    }

    [Fact]
    public async Task CreateMemberElectricityReadingAsync_ReturnsValidationError_WhenReadingIncreaseExceeds500()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1311;
        const int plotId = 1312;
        const int meterId = 1313;

        dbContext.Users.Add(CreateUser(adminUserId, "admin7@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "High Consumption Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1312", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "High Consumption Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                }
            ]
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new DateOnly(2020, 1, 1), Rate = 5m });
        await dbContext.SaveChangesAsync();

        var highConsumptionPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        highConsumptionPlot.MemberElectricityMeterId = meterId;
        await dbContext.SaveChangesAsync();

        var service = new MemberElectricityService(dbContext);
        var result = await service.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                meterId,
                new DateOnly(2026, 2, 1),
                700m,
                null,
                adminUserId),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Изменение показаний не может превышать 500 кВт·ч.", result.ErrorMessage);
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CreateMemberElectricityReadingAsync_ReturnsValidationError_WhenMemberTariffIsMissing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1321;
        const int plotId = 1322;
        const int meterId = 1323;

        dbContext.Users.Add(CreateUser(adminUserId, "admin8@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Missing Tariff Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1322", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Missing Tariff Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                }
            ]
        });
        await dbContext.SaveChangesAsync();

        var missingTariffPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        missingTariffPlot.MemberElectricityMeterId = meterId;
        await dbContext.SaveChangesAsync();

        var service = new MemberElectricityService(dbContext);
        var result = await service.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                meterId,
                new DateOnly(2026, 2, 1),
                130m,
                null,
                adminUserId),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Для указанной даты не найден тариф для участников. Добавьте его на странице \"Тариф для участников\".", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateMemberElectricityReadingAsync_WhenAuditFails_RollsBackReadingAndCharge()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const string adminUserId = "admin-user";
        const int memberId = 1331;
        const int plotId = 1332;
        const int meterId = 1333;

        dbContext.Users.Add(CreateUser(adminUserId, "admin-rollback@example.com"));
        dbContext.Members.Add(new Member { Id = memberId, FullName = "Rollback Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1332", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "Rollback Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                }
            ]
        });
        dbContext.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new DateOnly(2020, 1, 1), Rate = 5m });
        await dbContext.SaveChangesAsync();

        var rollbackPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        rollbackPlot.MemberElectricityMeterId = meterId;
        await dbContext.SaveChangesAsync();

        var service = new MemberElectricityService(dbContext, new ThrowingFinancialAuditService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateReadingAsync(
            new CreateMemberElectricityReadingRequest(
                meterId,
                new DateOnly(2026, 2, 1),
                130m,
                null,
                adminUserId),
            CancellationToken.None));

        var readings = await dbContext.MemberElectricityReadings
            .AsNoTracking()
            .OrderBy(item => item.ReadingDate)
            .ToListAsync();

        Assert.Single(readings);
        Assert.True(readings[0].IsInitialReading);
        Assert.Empty(await dbContext.Charges.AsNoTracking().ToListAsync());
        Assert.Empty(await dbContext.FinancialAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task OnGetAdministrationMemberFinance_LoadsFullElectricityReadingHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int memberId = 1321;
        const int plotId = 1322;
        const int meterId = 1323;

        dbContext.Members.Add(new Member { Id = memberId, FullName = "History Member", IsActive = true });
        dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1322", IsActive = true });
        dbContext.PlotOwnerships.Add(new PlotOwnership { Id = 1, PlotId = plotId, MemberId = memberId, ValidFrom = new DateOnly(2020, 1, 1), IsPrimaryContact = true });
        dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
        {
            Id = meterId,
            MemberId = memberId,
            Name = "History Meter",
            BillingPlotId = plotId,
            IsActive = true,
            Readings =
            [
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 1, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true
                },
                new MemberElectricityReading
                {
                    ReadingDate = new DateOnly(2026, 2, 1),
                    CurrentReading = 130m,
                    Amount = 150m,
                    IsInitialReading = false
                }
            ]
        });
        await dbContext.SaveChangesAsync();

        var historyPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
        historyPlot.MemberElectricityMeterId = meterId;
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

        var service = new MemberElectricityService(dbContext);
        var model = new FinanceModel(dbContext, service, userManager)
        {
            ChargePage = 1,
            PaymentPage = 1
        };

        var result = await model.OnGetAsync(memberId, CancellationToken.None);

        Assert.IsType<PageResult>(result);

        var meter = Assert.Single(model.ElectricityMeters);
        Assert.Equal(new DateOnly(2026, 2, 1), meter.LatestReadingDate);
        Assert.Equal(130m, meter.LatestReading);
        Assert.Equal(2, meter.Readings.Count);
        Assert.Collection(meter.Readings,
            reading =>
            {
                Assert.Equal(new DateOnly(2026, 2, 1), reading.ReadingDate);
                Assert.Equal(130m, reading.CurrentReading);
                Assert.Equal(30m, reading.Consumption);
                Assert.Equal(150m, reading.Amount);
                Assert.False(reading.IsInitialReading);
            },
            reading =>
            {
                Assert.Equal(new DateOnly(2026, 1, 1), reading.ReadingDate);
                Assert.Equal(100m, reading.CurrentReading);
                Assert.True(reading.IsInitialReading);
            });
    }

    [Fact]
    public async Task OnPostRegisterPaymentAsync_WithBankTransfer_ReturnsValidationError()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        const int memberId = 2;
        const int plotId = 3;

        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = "Payment Member",
            IsActive = true
        });

        dbContext.Plots.Add(new Plot
        {
            Id = plotId,
            Number = "P-3",
            IsActive = true
        });

        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = 1,
            MemberId = memberId,
            PlotId = plotId,
            ValidFrom = new DateOnly(2020, 1, 1),
            IsPrimaryContact = true
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

        var model = new RegisterPaymentModel(
            dbContext,
            new PaymentService(dbContext, new FinancialAuditService(dbContext, new Microsoft.AspNetCore.Http.HttpContextAccessor())),
            userManager)
        {
            Input = new MemberPaymentInputModel
            {
                PlotId = plotId,
                PaymentDate = DateOnly.FromDateTime(DateTime.Today),
                Amount = 100m,
                PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.BankTransfer
            }
        };

        var result = await model.OnPostAsync(memberId, CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        var error = Assert.Single(model.ModelState[nameof(model.Input.PaymentMethod)]!.Errors);
        Assert.Equal("Выберите допустимый способ оплаты: наличные или перевод на карту.", error.ErrorMessage);
    }

    private static PageContext CreatePageContext(string userId, string? userName = null)
    {
        return new PageContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, userName ?? userId)
                ],
                "Test"))
            }
        };
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

    private static string ExtractSection(string html, string startMarker, string endMarker)
    {
        var startIndex = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker not found: {startMarker}");

        var endIndex = html.IndexOf(endMarker, startIndex + startMarker.Length, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"End marker not found: {endMarker}");

        return html[startIndex..endIndex];
    }

    private sealed class ThrowingFinancialAuditService : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            throw new InvalidOperationException("Simulated audit failure.");
        }
    }
}
