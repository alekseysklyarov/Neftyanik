using System.Net;
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

public sealed class AdvancePaymentTests
{
    private static readonly DateOnly Date = new(2026, 9, 21);
    private static FinancialAuditService Audit(ApplicationDbContext db) => new(db, new HttpContextAccessor());

    [Theory]
    [InlineData(458.5, 180, 278.5)]
    [InlineData(80, 80, 0)]
    [InlineData(0, 0, 0)]
    public async Task Reading_UsesAvailableAdvance_AndSummaryMatchesAllocations(decimal advance, decimal paid, decimal remainingAdvance)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            if (advance > 0)
                Assert.True((await new PaymentService(db, Audit(db)).CreateMemberPaymentAsync(
                    new(701, 801, Date.AddDays(14), advance, PaymentMethod.Cash, null, null, null))).Succeeded);
            var result = await new MemberElectricityService(db, Audit(db)).CreateReadingAsync(new(901, Date, 140, null, null));
            Assert.True(result.Succeeded, result.ErrorMessage);
            var allocated = (await db.PaymentAllocations.Select(a => a.Amount).ToListAsync()).Sum();
            Assert.Equal(paid, allocated);
            Assert.Equal(remainingAdvance, advance - allocated);
            var summary = await ElectricityFinanceViewData.LoadSummaryAsync(db, 701, [801, 802], new(2026, 10, 5), default);
            Assert.Equal(180, summary.Charged);
            Assert.Equal(paid, summary.Paid);
            Assert.Equal(180 - paid, summary.TotalOutstanding);
            var charge = await db.Charges.SingleAsync();
            Assert.Equal(0, await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [charge.Id]));
            Assert.Equal(allocated, (await db.PaymentAllocations.Select(a => a.Amount).ToListAsync()).Sum());
        });
    }

    [Fact]
    public async Task Batch_UsesAdvanceAcrossOwnedPlots_OldestChargeFirst_AndExcludesCancelledPayments()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Payments.AddRange(Payment(1, 701, 801, 70), Payment(2, 702, 803, 1000),
                new Payment { Id = 3, MemberId = 701, PlotId = 801, Amount = 1000, PaymentDate = Date, CancelledAtUtc = DateTime.UtcNow });
            db.Charges.AddRange(Charge(101, 801, 50, Date), Charge(102, 802, 50, Date.AddDays(-1)));
            await db.SaveChangesAsync();
            Assert.Equal(70, await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [101, 102]));
            var allocations = await db.PaymentAllocations.ToListAsync();
            Assert.All(allocations, a => Assert.Equal(1, a.PaymentId));
            Assert.Equal(50, allocations.Single(a => a.ChargeId == 102).Amount);
            Assert.Equal(20, allocations.Single(a => a.ChargeId == 101).Amount);
            Assert.Equal(2, await db.FinancialAuditLogs.CountAsync());
        });
    }

    [Fact]
    public async Task Cancellation_ReleasesAdvance_AndCancelledPaymentNoLongerPaysCharge()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Payments.Add(Payment(1, 701, 801, 100));
            db.Charges.Add(Charge(101, 801, 100, Date));
            await db.SaveChangesAsync();
            await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [101]);
            Assert.True((await new ChargeService(db, Audit(db)).CancelChargeAsync(new(101, "Correction"))).Succeeded);
            db.Charges.Add(Charge(102, 801, 80, Date));
            await db.SaveChangesAsync();
            Assert.Equal(80, await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [101, 102]));
            Assert.True((await new PaymentService(db, Audit(db)).CancelPaymentAsync(new(1, "Correction"))).Succeeded);
            Assert.Equal(0, await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [102]));
            Assert.Equal(80, (await db.LoadOutstandingPaymentChargesAsync([801])).Single().OutstandingAmount);
        });
    }

    [Fact]
    public async Task PreviousOwnerAdvance_DoesNotPayNewOwnersCharge()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            (await db.PlotOwnerships.SingleAsync(o => o.PlotId == 801)).ValidTo = Date.AddDays(-1);
            db.PlotOwnerships.Add(new PlotOwnership { MemberId = 702, PlotId = 801, ValidFrom = Date });
            db.Payments.Add(Payment(1, 701, 801, 100));
            db.Charges.Add(Charge(101, 801, 100, Date));
            await db.SaveChangesAsync();
            Assert.Equal(0, await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [101]));
            Assert.Empty(await db.PaymentAllocations.ToListAsync());
        });
    }

    [Fact]
    public async Task ManualChargePage_AutomaticallyUsesAdvance()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Payments.Add(Payment(1, 701, 801, 200));
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("advance-admin", RoleNames.Administrator));
        const string url = "/neftyanik/Administration/Members/Finance/701/CreateCharge";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token), ["Input.PlotId"] = "801",
            ["Input.ChargeTypeId"] = "2", ["Input.Amount"] = "180", ["Input.ChargeDate"] = "2026-09-21"
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await factory.ExecuteDbContextAsync(async db => Assert.Equal(180, (await db.PaymentAllocations.SingleAsync()).Amount));
    }

    [Fact]
    public async Task AuditFailure_RollsBackReadingChargeAndAdvanceAllocation()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Payments.Add(Payment(1, 701, 801, 200));
            await db.SaveChangesAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new MemberElectricityService(db, new ThrowOnAdvanceAudit())
                .CreateReadingAsync(new(901, Date, 140, null, null)));
            db.ChangeTracker.Clear();
            Assert.Empty(await db.Charges.ToListAsync());
            Assert.Empty(await db.PaymentAllocations.ToListAsync());
            Assert.Single(await db.MemberElectricityReadings.ToListAsync());
        });
    }

    [Fact]
    public async Task Transfer_KeepsOldDebtAndPaidChargesWithPreviousOwner()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Charges.AddRange(Charge(101, 801, 100, Date.AddDays(-10)), Charge(102, 801, 50, Date.AddDays(-9)));
            db.Payments.Add(Payment(1, 701, 801, 100));
            await db.SaveChangesAsync();
            await AdvancePaymentAllocator.ApplyAsync(db, Audit(db), [101, 102]);
            (await db.PlotOwnerships.SingleAsync(o => o.PlotId == 801)).ValidTo = Date.AddDays(-1);
            db.PlotOwnerships.Add(new PlotOwnership { MemberId = 702, PlotId = 801, ValidFrom = Date });
            await db.SaveChangesAsync();
            Assert.Equal(50, await db.CalculateActiveBalanceAsync(701, []));
            Assert.Equal(0, await db.CalculateActiveBalanceAsync(702, [801]));
            Assert.Equal(100, (await db.PaymentAllocations.SingleAsync()).Amount);
            Assert.All(await db.Charges.ToListAsync(), c => Assert.Equal(701, c.MemberId));
            var payment = await new PaymentService(db, Audit(db)).CreateMemberPaymentAsync(
                new(701, 801, Date, 50, PaymentMethod.Cash, null, null, null));
            Assert.True(payment.Succeeded);
            Assert.Equal(0, await db.CalculateActiveBalanceAsync(701, []));
            Assert.Equal(0, await db.CalculateActiveBalanceAsync(702, [801]));
        });
    }

    [Fact]
    public async Task Correction_RecalculatesLatestIntervalAndPreservesEarlierChargePaymentsAndAudit()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("correction-admin", RoleNames.Administrator));
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            db.Payments.Add(Payment(1, 701, 801, 300));
            await db.SaveChangesAsync();
            var service = new MemberElectricityService(db, Audit(db));
            Assert.True((await service.CreateReadingAsync(new(901, Date, 140, null, null))).Succeeded);
            Assert.True((await service.CreateReadingAsync(new(901, Date.AddDays(1), 160, null, null))).Succeeded);
            var earlierChargeId = (await db.MemberElectricityReadings.SingleAsync(r => r.ReadingDate == Date)).ChargeId;
            var original = await db.MemberElectricityReadings.SingleAsync(r => r.ReadingDate == Date.AddDays(1));
            var originalChargeId = original.ChargeId;
            var result = await new MemberReadingCorrectionService(db, Audit(db))
                .CorrectAsync(901, original.Id, 150, null, "Ошибка ввода", "correction-admin");
            Assert.True(result.Succeeded, result.ErrorMessage);
            db.ChangeTracker.Clear();
            var active = await db.Charges.Where(c => c.CancelledAtUtc == null).OrderBy(c => c.ChargeDate).ToListAsync();
            Assert.Equal(new decimal[] { 180, 45 }, active.Select(c => c.Amount));
            Assert.Equal(earlierChargeId, active[0].Id);
            Assert.Equal(1, await db.Charges.CountAsync(c => c.CancelledAtUtc != null));
            Assert.NotEqual(originalChargeId, (await db.MemberElectricityReadings.SingleAsync(r => r.Id == original.Id)).ChargeId);
            Assert.Equal(300, (await db.Payments.SingleAsync()).Amount);
            Assert.Equal(225, (await db.PaymentAllocations.Where(a => a.Charge!.CancelledAtUtc == null).Select(a => a.Amount).ToListAsync()).Sum());
            Assert.Equal(-75, await db.CalculateActiveBalanceAsync(701, []));
            Assert.True(await db.FinancialAuditLogs.AnyAsync(a => a.EntityType == nameof(MemberElectricityReading)
                && a.Action == FinancialAuditLogActions.Updated && a.OldValuesJson != null));
            Assert.False((await new MemberReadingCorrectionService(db, Audit(db))
                .CorrectAsync(901, original.Id, 130, null, "Ошибка ввода", "correction-admin")).Succeeded);
            Assert.Equal(3, await db.Charges.CountAsync());
        });
    }

    [Fact]
    public async Task CorrectionPage_PostUsesEnteredValue()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("correct-page-admin", RoleNames.Administrator), cultureName: "ru-RU");
        long readingId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await new MemberElectricityService(db, Audit(db)).CreateReadingAsync(new(901, Date, 140, null, null))).Succeeded);
            readingId = (await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading)).Id;
        });
        var url = $"/neftyanik/Administration/Electricity/Meters/901/Readings/{readingId}/Correct";
        const string memberUrl = "/neftyanik/Administration/Members/Finance/701/Finance";
        var memberHtml = await (await client.GetAsync(memberUrl)).ReadDecodedHtmlAsync();
        Assert.Contains(url, memberHtml);
        Assert.Contains("Исправить последнее показание", memberHtml);
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        Assert.Contains(memberUrl, html);
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token), ["Input.Day"] = "130", ["Input.Reason"] = "Ошибка ввода"
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(memberUrl, response.Headers.Location?.OriginalString);
        await factory.ExecuteDbContextAsync(async db =>
            Assert.Equal(130, (await db.MemberElectricityReadings.SingleAsync(r => r.Id == readingId)).CurrentReading));
    }

    [Fact]
    public async Task Correction_RejectsEarlierReadingIncludingStaleFormWithoutChangingFinance()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("stale-correction-admin", RoleNames.Administrator));
        long earlierId = 0, latestId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await new MemberElectricityService(db, Audit(db)).CreateReadingAsync(new(901, Date, 140, null, null))).Succeeded);
            earlierId = (await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading)).Id;
        });
        var url = $"/neftyanik/Administration/Electricity/Meters/901/Readings/{earlierId}/Correct";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.True((await new MemberElectricityService(db, Audit(db)).CreateReadingAsync(new(901, Date.AddDays(1), 160, null, null))).Succeeded);
            latestId = (await db.MemberElectricityReadings.SingleAsync(r => r.ReadingDate == Date.AddDays(1))).Id;
            var auditCount = await db.FinancialAuditLogs.CountAsync();
            foreach (var id in await db.MemberElectricityReadings.Where(r => r.Id != latestId).Select(r => r.Id).ToListAsync())
            {
                var result = await new MemberReadingCorrectionService(db, Audit(db)).CorrectAsync(901, id, 130, null, "Ошибка", "stale-correction-admin");
                Assert.False(result.Succeeded);
                Assert.Contains("только последнее", result.ErrorMessage);
            }
            Assert.Equal(auditCount, await db.FinancialAuditLogs.CountAsync());
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token), ["Input.Day"] = "130", ["Input.Reason"] = "Ошибка"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var memberHtml = await (await client.GetAsync("/neftyanik/Administration/Members/Finance/701/Finance")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain(url, memberHtml);
        Assert.Contains($"/neftyanik/Administration/Electricity/Meters/901/Readings/{latestId}/Correct", memberHtml);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(140, (await db.MemberElectricityReadings.SingleAsync(r => r.Id == earlierId)).CurrentReading);
            Assert.Equal(2, await db.Charges.CountAsync());
            Assert.False(await db.Charges.AnyAsync(c => c.CancelledAtUtc != null));
        });
    }

    private sealed class ThrowOnAdvanceAudit : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            if (description?.StartsWith("Зачтён аванс") == true) throw new InvalidOperationException("Audit failed");
        }
    }

    private static Payment Payment(long id, int memberId, int plotId, decimal amount) => new()
        { Id = id, MemberId = memberId, PlotId = plotId, Amount = amount, PaymentDate = Date, PaymentMethod = PaymentMethod.Cash };
    private static Charge Charge(long id, int plotId, decimal amount, DateOnly date) => new()
        { Id = id, PlotId = plotId, Amount = amount, ChargeDate = date, ChargeTypeId = 2 };

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Members.AddRange(new Member { Id = 701, FullName = "Advance Member" }, new Member { Id = 702, FullName = "Other Member" });
        db.Plots.AddRange(new Plot { Id = 801, Number = "A-801" }, new Plot { Id = 802, Number = "A-802" }, new Plot { Id = 803, Number = "A-803" });
        db.PlotOwnerships.AddRange(new PlotOwnership { MemberId = 701, PlotId = 801, ValidFrom = new(2020, 1, 1) },
            new PlotOwnership { MemberId = 701, PlotId = 802, ValidFrom = new(2020, 1, 1) },
            new PlotOwnership { MemberId = 702, PlotId = 803, ValidFrom = new(2020, 1, 1) });
        db.ChargeTypes.Add(new ChargeType { Id = 2, Name = "Electricity", Code = ChargeTypeCodes.Electricity, IsActive = true });
        db.MemberElectricityMeters.Add(new MemberElectricityMeter { Id = 901, MemberId = 701, BillingPlotId = 801, IsActive = true,
            Readings = [new MemberElectricityReading { ReadingDate = new(2026, 9, 1), CurrentReading = 100, IsInitialReading = true }] });
        db.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new(2020, 1, 1), Rate = 4.5m });
        await db.SaveChangesAsync();
        (await db.Plots.SingleAsync(p => p.Id == 801)).MemberElectricityMeterId = 901;
        await db.SaveChangesAsync();
    }
}
