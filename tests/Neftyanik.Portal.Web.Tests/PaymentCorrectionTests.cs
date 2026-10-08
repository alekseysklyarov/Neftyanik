using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Pages.Finance;
using Xunit;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Tests;

public class PaymentCorrectionTests
{
    private static readonly DateOnly PaymentDate = new(2025, 10, 8);
    private const string Url = "/neftyanik/Administration/Members/Finance/501/Payments/801/Edit";
    private static FinancialAuditService Audit(ApplicationDbContext db) => new(db, new HttpContextAccessor());
    private static Task<Payment> Load(ApplicationDbContext db) => db.Payments.Include(p => p.PaymentAllocations).ThenInclude(a => a.Charge).SingleAsync(p => p.Id == 801);

    [Theory]
    [InlineData(80, 80, 90)]
    [InlineData(250, 170, -80)]
    public async Task CorrectAmount_PreservesOtherPaymentAndUpdatesAllocationsBalancesReceiptsAndAudit(decimal amount, decimal allocated, decimal balance)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, PaymentDate);
            var version = PaymentCorrectionService.Version(await Load(db));
            Assert.Null(await new PaymentCorrectionService(db, Audit(db)).CorrectAsync(501, 801, amount, PaymentMethod.Card, "NEW", "Исправлено", "Ошибка суммы", version));
            db.ChangeTracker.Clear();
            var payment = await Load(db);
            Assert.Equal(amount, payment.Amount);
            Assert.Equal(allocated, payment.PaymentAllocations.Sum(a => a.Amount));
            Assert.Equal(PaymentMethod.Card, payment.PaymentMethod);
            Assert.Equal(200 - amount, payment.BalanceAfterPayment);
            Assert.Equal(balance, await db.CalculateActiveBalanceAsync(501, [601]));
            var later = await db.Payments.SingleAsync(p => p.Id == 802);
            Assert.Equal(30, later.Amount);
            Assert.Equal(balance, later.BalanceAfterPayment);
            Assert.Equal(balance + 30, later.BalanceBeforePayment);
            Assert.Equal(30, (await db.PaymentAllocations.SingleAsync(a => a.PaymentId == 802)).Amount);
            Assert.False(await db.PaymentAllocations.AnyAsync(a => a.ChargeId == 703));
            var log = await db.FinancialAuditLogs.SingleAsync(a => a.EntityType == nameof(Payment) && a.EntityId == "801");
            Assert.Contains("150", log.OldValuesJson);
            Assert.Contains("Allocations", log.NewValuesJson);
            Assert.Contains("Ошибка суммы", log.Description);
            var overview = new Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel(db);
            await overview.OnGetAsync(default);
            var cash = overview.Summary;
            Assert.Equal(30, cash.CurrentCashOnlyAmount);
            Assert.Equal(amount, cash.CurrentNonCashAmount);
            Assert.Equal(amount + 30, cash.CurrentCashAmount);
        });
    }

    [Fact]
    public async Task MethodOnly_KeepsAllocationIdsAndRejectsStaleForm()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, PaymentDate);
            var payment = await Load(db);
            var ids = payment.PaymentAllocations.Select(a => a.Id).Order().ToArray();
            var version = PaymentCorrectionService.Version(payment);
            var service = new PaymentCorrectionService(db, Audit(db));
            Assert.Null(await service.CorrectAsync(501, 801, 150, PaymentMethod.Card, null, null, "Способ оплаты", version));
            Assert.Equal(ids, payment.PaymentAllocations.Select(a => a.Id).Order().ToArray());
            Assert.NotNull(await service.CorrectAsync(501, 801, 90, PaymentMethod.Cash, null, null, "Устаревшая форма", version));
            Assert.Equal(150, payment.Amount);
        });
    }

    [Theory]
    [InlineData(0, false, 501)]
    [InlineData(1, false, 501)]
    [InlineData(-1, true, 501)]
    [InlineData(-1, false, 502)]
    public async Task RejectsEarlierCancelledAndOtherMember(int otherDateOffset, bool cancelled, int memberId)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, PaymentDate);
            var payment = await Load(db);
            (await db.Payments.SingleAsync(p => p.Id == 802)).PaymentDate = PaymentDate.AddDays(otherDateOffset);
            if (cancelled) payment.CancelledAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Assert.NotNull(await new PaymentCorrectionService(db, Audit(db)).CorrectAsync(memberId, 801, 50, PaymentMethod.Card, null, null, "Ошибка", PaymentCorrectionService.Version(payment)));
            Assert.Equal(150, payment.Amount);
            Assert.Empty(await db.FinancialAuditLogs.ToListAsync());
        });
    }

    [Fact]
    public async Task AuditFailure_RollsBackMoneyAndAllocations()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, PaymentDate);
            var version = PaymentCorrectionService.Version(await Load(db));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new PaymentCorrectionService(db, new FailingAudit())
                .CorrectAsync(501, 801, 80, PaymentMethod.Card, null, null, "Ошибка", version));
            db.ChangeTracker.Clear();
            var payment = await Load(db);
            Assert.Equal(150, payment.Amount);
            Assert.Equal(150, payment.PaymentAllocations.Sum(a => a.Amount));
            Assert.Equal(20, (await db.Payments.SingleAsync(p => p.Id == 802)).BalanceAfterPayment);
        });
    }

    [Fact]
    public async Task Eligibility_SkipsCancelledAndOtherMemberPayments_AcrossAllPlotsAndYears()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, PaymentDate);
            db.Plots.Add(new Plot { Id = 602, Number = "602" });
            db.Payments.AddRange(
                new Payment { Id = 803, MemberId = 501, PlotId = 602, PaymentDate = PaymentDate.AddYears(1), Amount = 10, CancelledAtUtc = DateTime.UtcNow },
                new Payment { Id = 804, MemberId = 502, PlotId = 601, PaymentDate = PaymentDate.AddYears(1), Amount = 10 });
            await db.SaveChangesAsync();
            Assert.Equal(801L, await PaymentCorrectionService.LatestActivePaymentIdAsync(db, 501));
            var version = PaymentCorrectionService.Version(await Load(db));
            Assert.Null(await new PaymentCorrectionService(db, Audit(db)).CorrectAsync(501, 801, 150, PaymentMethod.Card, null, null, "Исправление старого последнего платежа", version));
            version = PaymentCorrectionService.Version(await Load(db));
            (await db.Payments.SingleAsync(p => p.Id == 803)).CancelledAtUtc = null;
            await db.SaveChangesAsync();
            Assert.Equal(803L, await PaymentCorrectionService.LatestActivePaymentIdAsync(db, 501));
            Assert.Contains("последний активный", await new PaymentCorrectionService(db, Audit(db))
                .CorrectAsync(501, 801, 90, PaymentMethod.Card, null, null, "Открытая ранее форма", version));
            Assert.Equal(150, (await Load(db)).Amount);
        });
    }

    [Fact]
    public async Task NewPaymentAfterOpeningForm_BlocksPostAndHidesOldYearEditButton()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("last-payment-accountant", RoleNames.Accountant), cultureName: "ru-RU");
        await factory.ExecuteDbContextAsync(db => Seed(db, PaymentDate));
        var html = await (await client.GetAsync(Url)).ReadDecodedHtmlAsync();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Payments.Add(new Payment { Id = 803, MemberId = 501, PlotId = 601, PaymentDate = PaymentDate.AddYears(1), Amount = 10, PaymentMethod = PaymentMethod.Cash });
            await db.SaveChangesAsync();
        });
        var response = await client.PostAsync(Url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken"), ["Input.Version"] = Field(html, "Input.Version"),
            ["Input.Amount"] = "80,50", ["Input.PaymentMethod"] = "3", ["Input.Reason"] = "Исправление"
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Можно исправить только последний активный платёж", await response.ReadDecodedHtmlAsync());
        var direct = await (await client.GetAsync(Url)).ReadDecodedHtmlAsync();
        Assert.DoesNotContain("Сохранить исправление", direct);
        var finance = await (await client.GetAsync("/neftyanik/Administration/Members/Finance/501/Finance?year=2025")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain(Url, finance);
        Assert.DoesNotContain(Url.Replace("/801/", "/802/"), finance);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(150, (await Load(db)).Amount);
            Assert.Empty(await db.FinancialAuditLogs.ToListAsync());
        });
    }

    [Fact]
    public async Task Accountant_CanEditFromMemberFinance_AndMemberCannotAccess()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("correction-accountant", RoleNames.Accountant), cultureName: "ru-RU");
        await factory.ExecuteDbContextAsync(db => Seed(db, PaymentDate));
        var finance = await (await client.GetAsync("/neftyanik/Administration/Members/Finance/501/Finance?year=2025")).ReadDecodedHtmlAsync();
        Assert.Contains(Url, finance);
        Assert.DoesNotContain(Url.Replace("/801/", "/802/"), finance);
        var html = await (await client.GetAsync(Url)).ReadDecodedHtmlAsync();
        var response = await client.PostAsync(Url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = Field(html, "__RequestVerificationToken"), ["Input.Version"] = Field(html, "Input.Version"),
            ["Input.Amount"] = "80,50", ["Input.PaymentMethod"] = "3", ["Input.Reason"] = "Исправление суммы",
            ["Input.PaymentDate"] = "2000-01-01", ["Input.MemberId"] = "502", ["Input.PlotId"] = "602"
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Administration/Members/Finance/501/Finance", response.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url.Replace("/501/", "/502/"))).StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var payment = await Load(db);
            Assert.Equal(80.50m, payment.Amount);
            Assert.Equal(501, payment.MemberId);
            Assert.Equal(601, payment.PlotId);
            Assert.Equal(PaymentDate, payment.PaymentDate);
        });
        using var member = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("correction-member", RoleNames.Member));
        var denied = await member.GetAsync(Url);
        Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
    }

    private static string Field(string html, string name) => WebUtility.HtmlDecode(Regex.Match(html, $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailingAudit : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null) => throw new InvalidOperationException("Audit unavailable");
    }
    private static async Task Seed(ApplicationDbContext db, DateOnly date)
    {
        db.Members.AddRange(new Member { Id = 501, FullName = "Correction Member" }, new Member { Id = 502, FullName = "Other Member" });
        db.Plots.Add(new Plot { Id = 601, Number = "601" });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 501, PlotId = 601 });
        db.ChargeTypes.Add(new ChargeType { Id = 701, Name = "Fee" });
        db.Charges.AddRange(new Charge { Id = 701, MemberId = 501, PlotId = 601, ChargeTypeId = 701, Amount = 100, ChargeDate = date.AddDays(-2) },
            new Charge { Id = 702, MemberId = 501, PlotId = 601, ChargeTypeId = 701, Amount = 100, ChargeDate = date.AddDays(-1) },
            new Charge { Id = 703, MemberId = 502, PlotId = 601, ChargeTypeId = 701, Amount = 100, ChargeDate = date.AddDays(-3) });
        db.Payments.AddRange(new Payment { Id = 801, MemberId = 501, PlotId = 601, Amount = 150, PaymentDate = date, PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = date.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc), BalanceBeforePayment = 200, BalanceAfterPayment = 50 },
            new Payment { Id = 802, MemberId = 501, PlotId = 601, Amount = 30, PaymentDate = date.AddDays(-1), PaymentMethod = PaymentMethod.Cash,
                CreatedAtUtc = date.ToDateTime(new TimeOnly(11, 0), DateTimeKind.Utc), BalanceBeforePayment = 50, BalanceAfterPayment = 20 });
        db.PaymentAllocations.AddRange(new PaymentAllocation { PaymentId = 801, ChargeId = 701, Amount = 100 },
            new PaymentAllocation { PaymentId = 801, ChargeId = 702, Amount = 50 }, new PaymentAllocation { PaymentId = 802, ChargeId = 702, Amount = 30 });
        await db.SaveChangesAsync();
    }
}
