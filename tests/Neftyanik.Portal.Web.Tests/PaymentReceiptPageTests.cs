using System.Net;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;
using ChargeTypeEntity = Neftyanik.Portal.Domain.Entities.ChargeType;
using System.Text.RegularExpressions;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PaymentReceiptPageTests
{
    [Fact]
    public async Task GetReceipt_ForOwnMemberPayment_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-1";
        const int memberId = 101;
        const int plotId = 201;
        const long paymentId = 301;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Member Receipt One", "P-201", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = paymentId,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 1),
                Amount = 120m,
                PaymentMethod = PaymentMethod.Cash,
                ReferenceNumber = "REC-301",
                Description = "Own payment",
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync($"/Payments/{paymentId}/Receipt");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Member Receipt One", html, StringComparison.Ordinal);
        Assert.Contains("REC-301", html, StringComparison.Ordinal);
        Assert.Contains("Own payment", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_ForAnotherMembersPayment_ReturnsNotFound()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-2";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, 101, userId, 201, "Member One", "P-201", validFrom: new DateOnly(2020, 1, 1));
            SeedMemberWithUserAndPlot(dbContext, 102, "member-receipt-other", 202, "Member Two", "P-202", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 302,
                MemberId = 102,
                PlotId = 202,
                PaymentDate = new DateOnly(2026, 8, 2),
                Amount = 80m,
                PaymentMethod = PaymentMethod.Card,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/302/Receipt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReceipt_ForCurrentOwnerOfTransferredPlot_DoesNotExposePreviousOwnersPayment()
    {
        using var factory = new PortalWebApplicationFactory();
        const int plotId = 203;
        const long paymentId = 303;
        const string currentOwnerUserId = "member-current-owner";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUser(dbContext, 103, "member-previous-owner", "Previous Owner");
            SeedMemberWithUser(dbContext, 104, currentOwnerUserId, "Current Owner");
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-203", Address = "Transferred plot", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = plotId,
                    MemberId = 103,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2025, 12, 31),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = plotId,
                    MemberId = 104,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryContact = true
                });
            dbContext.Payments.Add(new Payment
            {
                Id = paymentId,
                MemberId = 103,
                PlotId = plotId,
                PaymentDate = new DateOnly(2025, 6, 1),
                Amount = 95m,
                PaymentMethod = PaymentMethod.BankTransfer,
                ReferenceNumber = "OLD-OWNER-PAYMENT",
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(currentOwnerUserId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync($"/Payments/{paymentId}/Receipt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReceipt_ForAdministratorWithMatchingMemberContext_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-receipt-user";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-receipt@example.com"));
            SeedMemberWithUserAndPlot(dbContext, 105, "member-admin-target", 205, "Admin Target Member", "P-205", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 305,
                MemberId = 105,
                PlotId = 205,
                PaymentDate = new DateOnly(2026, 8, 3),
                Amount = 75m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/305/Receipt?memberId=105");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Administration/Members/Finance/105/Finance", html, StringComparison.Ordinal);
        Assert.Contains("Admin Target Member", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_ForAdministratorWithMismatchedMemberContext_ReturnsNotFound()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-receipt-mismatch";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-mismatch@example.com"));
            SeedMemberWithUserAndPlot(dbContext, 106, "member-admin-one", 206, "Admin Member One", "P-206", validFrom: new DateOnly(2020, 1, 1));
            SeedMemberWithUserAndPlot(dbContext, 107, "member-admin-two", 207, "Admin Member Two", "P-207", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 306,
                MemberId = 106,
                PlotId = 206,
                PaymentDate = new DateOnly(2026, 8, 4),
                Amount = 60m,
                PaymentMethod = PaymentMethod.Card,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/306/Receipt?memberId=107");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReceipt_ForAccountantWithMatchingMemberContext_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        const string accountantUserId = "accountant-receipt-user";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(accountantUserId, "accountant-receipt@example.com"));
            SeedMemberWithUserAndPlot(dbContext, 108, "member-accountant-target", 208, "Accountant Target Member", "P-208", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 307,
                MemberId = 108,
                PlotId = 208,
                PaymentDate = new DateOnly(2026, 8, 5),
                Amount = 66m,
                PaymentMethod = PaymentMethod.BankTransfer,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(accountantUserId, RoleNames.Accountant), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/307/Receipt?memberId=108");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetReceipt_ForMultiRoleUser_WithMismatchedAdminContext_DoesNotFallbackToMemberAccess()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "multi-role-user";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, 109, userId, 209, "Multi Role Member", "P-209", validFrom: new DateOnly(2020, 1, 1));
            SeedMemberWithUserAndPlot(dbContext, 110, "another-member-user", 210, "Another Member", "P-210", validFrom: new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 308,
                MemberId = 109,
                PlotId = 209,
                PaymentDate = new DateOnly(2026, 8, 6),
                Amount = 110m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/308/Receipt?memberId=110");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReceipt_ForCancelledPayment_ShowsCancellationAndAllocations()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-cancelled-receipt";
        const int memberId = 111;
        const int plotId = 211;
        const long paymentId = 309;
        const int chargeTypeId = 411;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Cancelled Receipt Member", "P-211", validFrom: new DateOnly(2020, 1, 1));
            dbContext.ChargeTypes.Add(new ChargeTypeEntity { Id = chargeTypeId, Name = "Членский взнос", IsActive = true });
            dbContext.Charges.Add(new Charge
            {
                Id = 511,
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 150m,
                ChargeDate = new DateOnly(2026, 8, 1),
                CreatedAtUtc = DateTime.UtcNow
            });
            dbContext.Payments.Add(new Payment
            {
                Id = paymentId,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 7),
                Amount = 150m,
                PaymentMethod = PaymentMethod.Cash,
                CancelledAtUtc = new DateTime(2026, 8, 8, 10, 30, 0, DateTimeKind.Utc),
                CancellationReason = "Ошибочный ввод",
                CreatedAtUtc = DateTime.UtcNow
            });
            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = paymentId,
                ChargeId = 511,
                Amount = 150m
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync($"/Payments/{paymentId}/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(new Regex("Плат[её]ж отмен[её]н", RegexOptions.CultureInvariant), html);
        Assert.Contains("Ошибочный ввод", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("150[,.]00", RegexOptions.CultureInvariant), html);
    }

    [Fact]
    public async Task GetMemberPlotFinance_DoesNotShowPreviousOwnersPaymentsForSamePlot()
    {
        using var factory = new PortalWebApplicationFactory();
        const string currentOwnerUserId = "current-owner-plot-finance";
        const int plotId = 212;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUser(dbContext, 112, "previous-owner-plot-finance", "Previous Plot Owner");
            SeedMemberWithUser(dbContext, 113, currentOwnerUserId, "Current Plot Owner");
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-212", Address = "Shared plot", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = plotId,
                    MemberId = 112,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2025, 12, 31),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = plotId,
                    MemberId = 113,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryContact = true
                });
            dbContext.Payments.AddRange(
                new Payment
                {
                    Id = 310,
                    MemberId = 112,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2025, 12, 1),
                    Amount = 55m,
                    PaymentMethod = PaymentMethod.Cash,
                    ReferenceNumber = "PREVIOUS-OWNER-REF",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Payment
                {
                    Id = 311,
                    MemberId = 113,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2026, 8, 9),
                    Amount = 85m,
                    PaymentMethod = PaymentMethod.Card,
                    ReferenceNumber = "CURRENT-OWNER-REF",
                    CreatedAtUtc = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(currentOwnerUserId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CURRENT-OWNER-REF", html, StringComparison.Ordinal);
        Assert.DoesNotContain("PREVIOUS-OWNER-REF", html, StringComparison.Ordinal);
        Assert.Contains($"/Payments/311/Receipt", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"/Payments/310/Receipt", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_DoesNotShowPreviousOwnersHistoricalPaymentsForSharedPlot()
    {
        using var factory = new PortalWebApplicationFactory();
        const string currentOwnerUserId = "current-owner-dashboard";
        const int plotId = 213;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUser(dbContext, 114, "previous-owner-dashboard", "Previous Dashboard Owner");
            SeedMemberWithUser(dbContext, 115, currentOwnerUserId, "Current Dashboard Owner");
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-213", Address = "Dashboard shared plot", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = plotId,
                    MemberId = 114,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2025, 12, 31),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = plotId,
                    MemberId = 115,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryContact = true
                });
            dbContext.Payments.AddRange(
                new Payment
                {
                    Id = 312,
                    MemberId = 114,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2025, 12, 5),
                    Amount = 40m,
                    PaymentMethod = PaymentMethod.Cash,
                    ReferenceNumber = "OLD-DASHBOARD-REF",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Payment
                {
                    Id = 313,
                    MemberId = 115,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2026, 8, 10),
                    Amount = 90m,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    ReferenceNumber = "CURRENT-DASHBOARD-REF",
                    CreatedAtUtc = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(currentOwnerUserId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Member");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CURRENT-DASHBOARD-REF", html, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD-DASHBOARD-REF", html, StringComparison.Ordinal);
        Assert.Contains($"/Payments/313/Receipt", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberFinance_DoesNotShowAnotherMembersHistoricalPaymentsForSharedPlot()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-historical-filter";
        const int plotId = 214;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(adminUserId, "admin-historical@example.com"));
            SeedMemberWithUser(dbContext, 116, "previous-owner-admin", "Previous Admin Owner");
            SeedMemberWithUser(dbContext, 117, "current-owner-admin", "Current Admin Owner");
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-214", Address = "Admin shared plot", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = plotId,
                    MemberId = 116,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    ValidTo = new DateOnly(2025, 12, 31),
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = plotId,
                    MemberId = 117,
                    ValidFrom = new DateOnly(2026, 1, 1),
                    IsPrimaryContact = true
                });
            dbContext.Payments.AddRange(
                new Payment
                {
                    Id = 314,
                    MemberId = 116,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2025, 12, 7),
                    Amount = 45m,
                    PaymentMethod = PaymentMethod.Cash,
                    ReferenceNumber = "OLD-ADMIN-REF",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Payment
                {
                    Id = 315,
                    MemberId = 117,
                    PlotId = plotId,
                    PaymentDate = new DateOnly(2026, 8, 11),
                    Amount = 88m,
                    PaymentMethod = PaymentMethod.Card,
                    ReferenceNumber = "CURRENT-ADMIN-REF",
                    CreatedAtUtc = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Administration/Members/Finance/117/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CURRENT-ADMIN-REF", html, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD-ADMIN-REF", html, StringComparison.Ordinal);
        Assert.Contains("/Payments/315/Receipt?memberId=117", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Payments/314/Receipt?memberId=117", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_FinanceTablesDoNotShowStatusColumns_AndCancelledItemsRemainVisible()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-finance-statusless";
        const int memberId = 118;
        const int plotId = 218;
        const int chargeTypeId = 318;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Finance Statusless Member", "P-218", new DateOnly(2020, 1, 1));
            dbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType { Id = chargeTypeId, Name = "Членский взнос", IsActive = true });
            dbContext.Charges.Add(new Charge
            {
                Id = 518,
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 150m,
                ChargeDate = new DateOnly(2026, 8, 1),
                CancelledAtUtc = new DateTime(2026, 8, 3, 8, 15, 0, DateTimeKind.Utc),
                CancellationReason = "Тестовая отмена начисления",
                Description = "Отмененное начисление"
            });
            dbContext.Payments.Add(new Payment
            {
                Id = 318,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 2),
                Amount = 120m,
                PaymentMethod = PaymentMethod.Cash,
                ReferenceNumber = "CANCELLED-MEMBER-REF",
                CancelledAtUtc = new DateTime(2026, 8, 4, 10, 30, 0, DateTimeKind.Utc),
                CancellationReason = "Тестовая отмена платежа",
                Description = "Отмененный платеж",
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Member");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chargesHeader = ExtractSection(html, "<h2 class=\"h5 mb-0\">Начисления</h2>", "</thead>");
        var paymentsHeader = ExtractSection(html, "<h2 class=\"h5 mb-0\">Платежи</h2>", "</thead>");

        Assert.DoesNotContain("Статус", chargesHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("Статус", paymentsHeader, StringComparison.Ordinal);
        Assert.Contains("badge text-bg-secondary ms-2", html, StringComparison.Ordinal);
        Assert.Contains("Тестовая отмена начисления", html, StringComparison.Ordinal);
        Assert.Contains("Тестовая отмена платежа", html, StringComparison.Ordinal);
        Assert.Contains("colspan=\"5\"", html, StringComparison.Ordinal);
        Assert.Contains("colspan=\"6\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_TablesDoNotShowStatusColumns_AndCancelledItemsRemainVisible()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-plot-statusless";
        const int memberId = 119;
        const int plotId = 219;
        const int chargeTypeId = 319;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Plot Finance Member", "P-219", new DateOnly(2020, 1, 1));
            dbContext.ChargeTypes.Add(new ChargeTypeEntity { Id = chargeTypeId, Name = "Электроэнергия", Code = "Electricity", IsActive = true });
            dbContext.Charges.Add(new Charge
            {
                Id = 519,
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 200m,
                ChargeDate = new DateOnly(2026, 8, 1),
                Description = "Отмененное начисление по участку",
                CancelledAtUtc = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc),
                CancellationReason = "Причина отмены начисления"
            });
            dbContext.Payments.Add(new Payment
            {
                Id = 319,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 2),
                Amount = 90m,
                PaymentMethod = PaymentMethod.Card,
                Description = "Отмененный платеж по участку",
                CancelledAtUtc = new DateTime(2026, 8, 6, 12, 0, 0, DateTimeKind.Utc),
                CancellationReason = "Причина отмены платежа",
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var chargesHeader = ExtractSection(html, "<h2 class=\"h5 mb-0\">Начисления</h2>", "</thead>");
        var paymentsHeader = ExtractSection(html, "<h2 class=\"h5 mb-0\">Платежи</h2>", "</thead>");

        Assert.DoesNotContain("Статус", chargesHeader, StringComparison.Ordinal);
        Assert.DoesNotContain("Статус", paymentsHeader, StringComparison.Ordinal);
        Assert.Contains("badge text-bg-secondary ms-2", html, StringComparison.Ordinal);
        Assert.Contains("Причина отмены начисления", html, StringComparison.Ordinal);
        Assert.Contains("Причина отмены платежа", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_WhenHistoricalBalanceShowsOverpayment_RendersDebtBeforeAndOverpaymentAfter()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-balance-overpay";
        const int memberId = 120;
        const int plotId = 220;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Balance Receipt Member", "P-220", new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 320,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 7),
                Amount = 1500m,
                BalanceBeforePayment = 1250m,
                BalanceAfterPayment = -250m,
                PaymentMethod = PaymentMethod.BankTransfer,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/320/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("До платежа", html, StringComparison.Ordinal);
        Assert.Contains("Задолженность", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("1[\\s\\u00A0]?250[,.]00\\s*₴", RegexOptions.CultureInvariant), html);
        Assert.Contains("После платежа", html, StringComparison.Ordinal);
        Assert.Contains("Переплата", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("250[,.]00\\s*₴", RegexOptions.CultureInvariant), html);
    }

    [Fact]
    public async Task GetReceipt_WhenHistoricalBalanceClosesDebt_RendersNoDebtAfterPayment()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-balance-zero";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, 121, userId, 221, "Zero Balance Member", "P-221", new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 321,
                MemberId = 121,
                PlotId = 221,
                PaymentDate = new DateOnly(2026, 8, 7),
                Amount = 150m,
                BalanceBeforePayment = 150m,
                BalanceAfterPayment = 0m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/321/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Задолженности нет", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_ForLegacyPaymentWithoutHistoricalBalance_ShowsUnavailableMessage()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-legacy-balance";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, 122, userId, 222, "Legacy Balance Member", "P-222", new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 322,
                MemberId = 122,
                PlotId = 222,
                PaymentDate = new DateOnly(2026, 8, 7),
                Amount = 75m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/322/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Исторический баланс недоступен", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_WithElectricityAllocation_ShowsAllocatedReadingButNotLatestUnrelatedReading()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-electricity";
        const int memberId = 123;
        const int plotId = 223;
        const int meterId = 323;
        const int chargeTypeId = 423;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Electricity Receipt Member", "P-223", new DateOnly(2020, 1, 1));
            dbContext.ChargeTypes.Add(new ChargeTypeEntity { Id = chargeTypeId, Name = "Электроэнергия", Code = "Electricity", IsActive = true });
            dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
            {
                Id = meterId,
                MemberId = memberId,
                BillingPlotId = plotId,
                Name = "Счётчик кухни",
                IsActive = true
            });
            dbContext.MemberElectricityReadings.AddRange(
                new MemberElectricityReading
                {
                    Id = 623,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 7, 1),
                    CurrentReading = 100m,
                    IsInitialReading = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                new MemberElectricityReading
                {
                    Id = 624,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 8, 1),
                    CurrentReading = 150m,
                    ChargeId = 523,
                    Amount = 250m,
                    IsInitialReading = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                new MemberElectricityReading
                {
                    Id = 625,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 9, 1),
                    CurrentReading = 190m,
                    ChargeId = 524,
                    Amount = 200m,
                    IsInitialReading = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            dbContext.Charges.AddRange(
                new Charge
                {
                    Id = 523,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    Amount = 250m,
                    ChargeDate = new DateOnly(2026, 8, 1),
                    Description = "Электроэнергия за август",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Charge
                {
                    Id = 524,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    Amount = 200m,
                    ChargeDate = new DateOnly(2026, 9, 1),
                    Description = "Электроэнергия за сентябрь",
                    CreatedAtUtc = DateTime.UtcNow
                });
            dbContext.Payments.Add(new Payment
            {
                Id = 323,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 8, 5),
                Amount = 250m,
                BalanceBeforePayment = 250m,
                BalanceAfterPayment = 0m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });
            dbContext.PaymentAllocations.Add(new PaymentAllocation
            {
                PaymentId = 323,
                ChargeId = 523,
                Amount = 250m
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/323/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Показания электросчётчика", html, StringComparison.Ordinal);
        Assert.Contains("Счётчик кухни", html, StringComparison.Ordinal);
        Assert.Contains("01.08.2026", html, StringComparison.Ordinal);
        Assert.Matches(new Regex("Предыдущее:\\s*100[,.]000", RegexOptions.CultureInvariant), html);
        Assert.Matches(new Regex("Текущее:\\s*150[,.]000", RegexOptions.CultureInvariant), html);
        Assert.Matches(new Regex("Расход:\\s*50[,.]000", RegexOptions.CultureInvariant), html);
        Assert.DoesNotContain("190.000", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Электроэнергия за сентябрь", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_WithoutElectricityAllocation_DoesNotRenderElectricitySection()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-no-electricity";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, 124, userId, 224, "No Electricity Receipt Member", "P-224", new DateOnly(2020, 1, 1));
            dbContext.Payments.Add(new Payment
            {
                Id = 324,
                MemberId = 124,
                PlotId = 224,
                PaymentDate = new DateOnly(2026, 8, 8),
                Amount = 100m,
                BalanceBeforePayment = 100m,
                BalanceAfterPayment = 0m,
                PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/324/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Показания электросчётчика", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReceipt_WithMultipleElectricityAllocations_RendersEachAllocatedReading()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-receipt-electricity-multi";
        const int memberId = 125;
        const int plotId = 225;
        const int meterId = 325;
        const int chargeTypeId = 425;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            SeedMemberWithUserAndPlot(dbContext, memberId, userId, plotId, "Multiple Electricity Member", "P-225", new DateOnly(2020, 1, 1), meterType: MemberElectricityMeterType.DayNight);
            dbContext.ChargeTypes.Add(new ChargeTypeEntity { Id = chargeTypeId, Name = "Электроэнергия", Code = "Electricity", IsActive = true });
            dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
            {
                Id = meterId,
                MemberId = memberId,
                BillingPlotId = plotId,
                MeterNumber = "DN-325",
                IsActive = true
            });
            dbContext.Charges.AddRange(
                new Charge
                {
                    Id = 525,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    Amount = 180m,
                    ChargeDate = new DateOnly(2026, 8, 1),
                    CreatedAtUtc = DateTime.UtcNow
                },
                new Charge
                {
                    Id = 526,
                    PlotId = plotId,
                    ChargeTypeId = chargeTypeId,
                    Amount = 210m,
                    ChargeDate = new DateOnly(2026, 9, 1),
                    CreatedAtUtc = DateTime.UtcNow
                });
            dbContext.MemberElectricityReadings.AddRange(
                new MemberElectricityReading
                {
                    Id = 626,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 7, 1),
                    CurrentReading = 100m,
                    CurrentNightReading = 40m,
                    IsInitialReading = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                new MemberElectricityReading
                {
                    Id = 627,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 8, 1),
                    CurrentReading = 140m,
                    CurrentNightReading = 55m,
                    ChargeId = 525,
                    Amount = 180m,
                    IsInitialReading = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                },
                new MemberElectricityReading
                {
                    Id = 628,
                    MemberElectricityMeterId = meterId,
                    ReadingDate = new DateOnly(2026, 9, 1),
                    CurrentReading = 185m,
                    CurrentNightReading = 70m,
                    ChargeId = 526,
                    Amount = 210m,
                    IsInitialReading = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            dbContext.Payments.Add(new Payment
            {
                Id = 325,
                MemberId = memberId,
                PlotId = plotId,
                PaymentDate = new DateOnly(2026, 9, 5),
                Amount = 390m,
                BalanceBeforePayment = 390m,
                BalanceAfterPayment = 0m,
                PaymentMethod = PaymentMethod.BankTransfer,
                CreatedAtUtc = DateTime.UtcNow
            });
            dbContext.PaymentAllocations.AddRange(
                new PaymentAllocation { PaymentId = 325, ChargeId = 525, Amount = 180m },
                new PaymentAllocation { PaymentId = 325, ChargeId = 526, Amount = 210m });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.GetAsync("/Payments/325/Receipt");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("DN-325", html, StringComparison.Ordinal);
        Assert.Contains("01.08.2026", html, StringComparison.Ordinal);
        Assert.Contains("01.09.2026", html, StringComparison.Ordinal);
        Assert.Contains("День:", html, StringComparison.Ordinal);
        Assert.Contains("Ночь:", html, StringComparison.Ordinal);
        Assert.Contains("Расход день:", html, StringComparison.Ordinal);
        Assert.Contains("Расход ночь:", html, StringComparison.Ordinal);
    }

    private static void SeedMemberWithUserAndPlot(
        ApplicationDbContext dbContext,
        int memberId,
        string userId,
        int plotId,
        string fullName,
        string plotNumber,
        DateOnly validFrom,
        MemberElectricityMeterType meterType = MemberElectricityMeterType.SingleRate,
        DateOnly? validTo = null)
    {
        SeedMemberWithUser(dbContext, memberId, userId, fullName, meterType);
        dbContext.Plots.Add(new Plot
        {
            Id = plotId,
            Number = plotNumber,
            Address = $"Address {plotNumber}",
            IsActive = true
        });
        dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            Id = memberId * 10 + plotId,
            PlotId = plotId,
            MemberId = memberId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            IsPrimaryContact = true
        });
    }

    private static void SeedMemberWithUser(
        ApplicationDbContext dbContext,
        int memberId,
        string userId,
        string fullName,
        MemberElectricityMeterType meterType = MemberElectricityMeterType.SingleRate)
    {
        dbContext.Users.Add(CreateUser(userId, $"{userId}@example.com"));
        dbContext.Members.Add(new Member
        {
            Id = memberId,
            FullName = fullName,
            ApplicationUserId = userId,
            ElectricityMeterType = meterType,
            IsActive = true
        });
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
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Test",
            LastName = "User",
            DisplayName = email,
            MustChangePassword = false,
            IsActive = true
        };
    }

    private static string ExtractSection(string html, string startMarker, string endMarker)
    {
        var startIndex = html.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Start marker '{startMarker}' was not found.");

        var endIndex = html.IndexOf(endMarker, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"End marker '{endMarker}' was not found.");

        return html[startIndex..endIndex];
    }
}
