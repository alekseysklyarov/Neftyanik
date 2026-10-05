using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PaymentPriorityTests
{
    private const string PageUrl = "/neftyanik/Administration/Members/Finance/701/RegisterPayment";

    [Theory]
    [InlineData(2, 20, 0, 20, 0, 0)]
    [InlineData(2, 70, 0, 60, 10, 0)]
    [InlineData(2, 130, 30, 60, 40, 0)]
    [InlineData(2, 650, 500, 60, 40, 50)]
    [InlineData(1, 70, 70, 0, 0, 0)]
    [InlineData(null, 70, 70, 0, 0, 0)]
    public async Task Service_AllocatesPriorityFirst_ThenOldestDebts_ThenAdvance(
        int? priority, decimal amount, decimal annual, decimal firstElectricity, decimal secondElectricity, decimal advance)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var result = await Service(db).CreateMemberPaymentAsync(Request(amount, priority));
            Assert.True(result.Succeeded);
            Assert.Equal(advance, result.AdvanceAmount);
            Assert.Equal(amount - advance, result.AllocatedAmount);
            var allocations = await db.PaymentAllocations.ToDictionaryAsync(a => a.ChargeId, a => a.Amount);
            Assert.Equal(annual, allocations.GetValueOrDefault(101));
            Assert.Equal(firstElectricity, allocations.GetValueOrDefault(102));
            Assert.Equal(secondElectricity, allocations.GetValueOrDefault(103));
            var audit = await db.FinancialAuditLogs.SingleAsync();
            using var json = JsonDocument.Parse(audit.NewValuesJson!);
            var storedPriority = json.RootElement.GetProperty("PriorityChargeTypeId");
            Assert.Equal(priority, storedPriority.ValueKind == JsonValueKind.Null ? null : storedPriority.GetInt32());
        });
    }

    [Theory]
    [InlineData(3)] // Debt of another member.
    [InlineData(4)] // Only a cancelled charge.
    [InlineData(999)]
    public async Task Service_RejectsUnavailablePriority_WithoutWritingPayment(int priority)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var result = await Service(db).CreateMemberPaymentAsync(Request(20, priority));
            Assert.Equal(CreateMemberPaymentResultCode.InvalidPaymentPriority, result.Code);
            Assert.Empty(await db.Payments.ToListAsync());
            Assert.Empty(await db.PaymentAllocations.ToListAsync());
            Assert.Empty(await db.FinancialAuditLogs.ToListAsync());
        });
    }

    [Fact]
    public async Task PaidTypeDisappears_CancellationRestoresIt_StalePriorityIsRejected()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var service = Service(db);
            var paid = await service.CreateMemberPaymentAsync(Request(500, 1));
            var charges = await db.LoadOutstandingPaymentChargesAsync([801, 802]);
            Assert.All(charges, c => Assert.Equal(2, c.ChargeTypeId));
            Assert.Equal(CreateMemberPaymentResultCode.InvalidPaymentPriority,
                (await service.CreateMemberPaymentAsync(Request(10, 1))).Code);
            Assert.Single(await db.Payments.ToListAsync());
            Assert.True((await service.CancelPaymentAsync(new(paid.PaymentId!.Value, "Correction"))).Succeeded);
            charges = await db.LoadOutstandingPaymentChargesAsync([801, 802]);
            Assert.Equal(500, charges.Single(c => c.ChargeTypeId == 1).OutstandingAmount);
        });
    }

    [Fact]
    public async Task Page_ShowsOnlyUnpaidTypes_AndPostsElectricityPriorityToService()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(SeedAsync);
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("priority-admin", RoleNames.Administrator), cultureName: "ru-RU");
        var get = await client.GetAsync(PageUrl);
        var html = await get.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var select = Regex.Match(html, "<select[^>]*id=\"Input_PriorityChargeTypeId\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Value;
        Assert.Contains("Annual fee", select);
        Assert.Contains("Electricity", select);
        Assert.DoesNotContain("Foreign type", select);
        Assert.DoesNotContain("Cancelled type", select);
        var post = await PostPaymentAsync(client, html, "2", "70");
        Assert.Equal(HttpStatusCode.Found, post.StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var allocations = await db.PaymentAllocations.ToListAsync();
            Assert.Equal(2, allocations.Count);
            Assert.DoesNotContain(allocations, a => a.ChargeId == 101);
            Assert.Equal(70, allocations.Sum(a => a.Amount));
        });
    }

    [Fact]
    public async Task Page_SelectsOnlyElectricityAfterAnnualFeePaid_ThenOffersAdvance()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Service(db).CreateMemberPaymentAsync(Request(500, 1))).Succeeded);
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("priority-admin", RoleNames.Accountant), cultureName: "ru-RU");
        var html = await (await client.GetAsync(PageUrl)).ReadDecodedHtmlAsync();
        var select = Regex.Match(html, "<select[^>]*id=\"Input_PriorityChargeTypeId\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Value;
        Assert.Equal(1, Regex.Matches(select, "<option").Count);
        Assert.Matches("<option(?=[^>]*selected)(?=[^>]*value=\"2\")[^>]*>", select);
        var response = await client.GetAsync(PageUrl + "?handler=Priorities&paymentDate=2026-10-05");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("value").GetString());
        Assert.Equal(HttpStatusCode.Found, (await PostPaymentAsync(client, html, "2", "100")).StatusCode);
        html = await (await client.GetAsync(PageUrl)).ReadDecodedHtmlAsync();
        Assert.Contains("Нет задолженности — аванс", html);
        Assert.Equal(HttpStatusCode.Found, (await PostPaymentAsync(client, html, null, "25")).StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var advance = await db.Payments.OrderByDescending(p => p.Id).FirstAsync();
            Assert.Equal(25, advance.Amount);
            Assert.False(await db.PaymentAllocations.AnyAsync(a => a.PaymentId == advance.Id));
        });
    }

    [Fact]
    public async Task Page_RejectsTamperedPriority_AndDateEndpointHonorsOwnership()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(SeedAsync);
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("priority-admin", RoleNames.Administrator), cultureName: "ru-RU");
        var html = await (await client.GetAsync(PageUrl)).ReadDecodedHtmlAsync();
        var response = await PostPaymentAsync(client, html, "3", "20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Выберите приоритет", await response.ReadDecodedHtmlAsync());
        await factory.ExecuteDbContextAsync(async db => Assert.Empty(await db.Payments.ToListAsync()));
        response = await client.GetAsync(PageUrl + "?handler=Priorities&paymentDate=2019-01-01");
        Assert.Equal("[]", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(PageUrl + "?handler=Priorities&paymentDate=invalid")).StatusCode);
    }

    private static Task<HttpResponseMessage> PostPaymentAsync(HttpClient client, string html, string? priority, string amount)
    {
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return client.PostAsync(PageUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.PlotId"] = "801", ["Input.PaymentDate"] = "2026-10-05",
            ["Input.Amount"] = amount, ["Input.PaymentMethod"] = "Cash", ["Input.PriorityChargeTypeId"] = priority ?? ""
        }));
    }

    private static PaymentService Service(ApplicationDbContext db) => new(db, new FinancialAuditService(db, new HttpContextAccessor()));
    private static CreateMemberPaymentRequest Request(decimal amount, int? priority) =>
        new(701, 801, new DateOnly(2026, 10, 5), amount, PaymentMethod.Cash, null, null, null, PriorityChargeTypeId: priority);

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Members.AddRange(new Member { Id = 701, FullName = "Priority Member" }, new Member { Id = 702, FullName = "Other Member" });
        db.Plots.AddRange(new Plot { Id = 801, Number = "P-801" }, new Plot { Id = 802, Number = "P-802" }, new Plot { Id = 803, Number = "P-803" });
        db.PlotOwnerships.AddRange(new PlotOwnership { MemberId = 701, PlotId = 801, ValidFrom = new DateOnly(2020, 1, 1) },
            new PlotOwnership { MemberId = 701, PlotId = 802, ValidFrom = new DateOnly(2020, 1, 1) },
            new PlotOwnership { MemberId = 702, PlotId = 803, ValidFrom = new DateOnly(2020, 1, 1) });
        db.ChargeTypes.AddRange(new ChargeType { Id = 1, Name = "Annual fee", IsYearly = true },
            new ChargeType { Id = 2, Name = "Electricity", Code = ChargeTypeCodes.Electricity },
            new ChargeType { Id = 3, Name = "Foreign type" }, new ChargeType { Id = 4, Name = "Cancelled type" });
        db.Charges.AddRange(new Charge { Id = 101, PlotId = 801, ChargeTypeId = 1, Amount = 500, ChargeDate = new DateOnly(2026, 1, 1) },
            new Charge { Id = 102, PlotId = 802, ChargeTypeId = 2, Amount = 60, ChargeDate = new DateOnly(2026, 9, 1) },
            new Charge { Id = 103, PlotId = 801, ChargeTypeId = 2, Amount = 40, ChargeDate = new DateOnly(2026, 10, 1) },
            new Charge { Id = 104, PlotId = 803, ChargeTypeId = 3, Amount = 100, ChargeDate = new DateOnly(2026, 1, 1) },
            new Charge { Id = 105, PlotId = 801, ChargeTypeId = 4, Amount = 100, ChargeDate = new DateOnly(2026, 1, 1), CancelledAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
