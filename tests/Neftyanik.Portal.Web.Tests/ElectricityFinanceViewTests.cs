using System.Net;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Finance;
using Xunit;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Tests;

public sealed class ElectricityFinanceViewTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(40, 60)]
    [InlineData(100, 0)]
    public async Task Summary_UsesAllocationsAndCalendarMonth_ExcludesCancelledAndOtherCharges(decimal paid, decimal remaining)
    {
        using var factory = new PortalWebApplicationFactory();
        var today = new DateOnly(2027, 1, 4);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, paid);
            var summary = await ElectricityFinanceViewData.LoadSummaryAsync(db, 701, [801], today, default);
            Assert.Equal(new DateOnly(2026, 12, 1), summary.Month);
            Assert.Equal(1, summary.ChargeCount);
            Assert.Equal(100, summary.Charged);
            Assert.Equal(paid, summary.Paid);
            Assert.Equal(remaining, summary.Outstanding);
            Assert.Equal(50 + remaining, summary.TotalOutstanding);
            Assert.Equal(2, summary.Meters.Count);
            var meter = Assert.Single(summary.Meters, m => m.Name == "METER-DAY-NIGHT");
            Assert.Equal(1250, meter.Reading!.Day);
            Assert.Equal(640, meter.Reading.Night);
            Assert.True(meter.HasCurrentMonthReading);
            Assert.Equal(1400, meter.LatestReading!.Day);
            Assert.Null(Assert.Single(summary.Meters, m => m.Name == "METER-MISSING").Reading);
            Assert.False(Assert.Single(summary.Meters, m => m.Name == "METER-MISSING").HasCurrentMonthReading);
        });
    }

    [Fact]
    public async Task Summary_CurrentReadingIsVisible_WhenPreviousMonthHasNoReadings()
    {
        using var factory = new PortalWebApplicationFactory();
        var today = new DateOnly(2027, 1, 4);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, 40);
            db.MemberElectricityReadings.Remove(await db.MemberElectricityReadings.SingleAsync(r => r.Id == 952));
            await db.SaveChangesAsync();
            var summary = await ElectricityFinanceViewData.LoadSummaryAsync(db, 701, [801], today, default);
            var meter = Assert.Single(summary.Meters, m => m.Name == "METER-DAY-NIGHT");
            Assert.Null(meter.Reading);
            Assert.True(meter.HasCurrentMonthReading);
            Assert.Equal(today, meter.LatestReading!.Date);
            Assert.Equal(1400, meter.LatestReading.Day);
        });
    }

    [Fact]
    public async Task Summary_PreviousAndFutureReadings_DoNotCountAsCurrentSubmission()
    {
        using var factory = new PortalWebApplicationFactory();
        var today = new DateOnly(2027, 1, 4);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, 40);
            var future = await db.MemberElectricityReadings.SingleAsync(r => r.Id == 953);
            future.ReadingDate = today.AddDays(1);
            await db.SaveChangesAsync();
            var summary = await ElectricityFinanceViewData.LoadSummaryAsync(db, 701, [801], today, default);
            var meter = Assert.Single(summary.Meters, m => m.Name == "METER-DAY-NIGHT");
            Assert.False(meter.HasCurrentMonthReading);
            Assert.Equal(1250, meter.LatestReading!.Day);
            Assert.Equal(meter.Reading, meter.LatestReading);
        });
    }

    [Fact]
    public async Task ChargeDetails_IncludeUnpaidCharges_AndRespectAuthorizedPlots()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, new DateOnly(2027, 1, 4), 0);
            var details = await ElectricityFinanceViewData.LoadChargesAsync(db, [801], [901, 902, 904], default);
            Assert.Equal(2, details.Count);
            Assert.Equal(1000, details[902].Previous!.Day);
            Assert.Equal(500, details[902].Previous!.Night);
            Assert.Equal(1250, details[902].Current!.Day);
            Assert.Null(details[901].Current);
            Assert.Empty(await ElectricityFinanceViewData.LoadChargesAsync(db, [999], [902], default));
        });
    }

    [Fact]
    public async Task Summary_RespectsExplicitPeriod_AndDoesNotTreatNoChargesAsPaid()
    {
        using var factory = new PortalWebApplicationFactory();
        var today = new DateOnly(2027, 1, 4);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, 100);
            var charge = await db.Charges.SingleAsync(c => c.Id == 902);
            charge.PeriodYear = 2026;
            charge.PeriodMonth = 11;
            await db.SaveChangesAsync();
            var summary = await ElectricityFinanceViewData.LoadSummaryAsync(db, 701, [801], today, default);
            Assert.Equal(0, summary.ChargeCount);
            Assert.Equal("text-muted", summary.StatusClass);
            Assert.Equal(50, summary.TotalOutstanding);
        });
    }

    [Fact]
    public async Task PaymentDetails_FollowActualChargeDespiteLatePayment_KeepDayNightAndMissingHistory()
    {
        using var factory = new PortalWebApplicationFactory();
        var today = new DateOnly(2027, 1, 4);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, 40);
            var payment = await db.Payments.SingleAsync(p => p.Id == 1001);
            payment.PaymentDate = today.AddMonths(3);
            db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = 1001, ChargeId = 901, Amount = 10 });
            await db.SaveChangesAsync();
            var details = await ElectricityFinanceViewData.LoadPaymentsAsync(db, 701, [1001], default);
            var billed = Assert.Single(details[1001], d => d.ChargeId == 902);
            Assert.Equal(1000, billed.Previous!.Day);
            Assert.Equal(500, billed.Previous.Night);
            Assert.Equal(1250, billed.Current!.Day);
            Assert.Equal(640, billed.Current.Night);
            Assert.Equal(40, billed.Allocated);
            Assert.Null(Assert.Single(details[1001], d => d.ChargeId == 901).Current);
            Assert.Empty(await ElectricityFinanceViewData.LoadPaymentsAsync(db, 999, [1001], default));
            Assert.Empty(await ElectricityFinanceViewData.LoadPaymentsAsync(db, 701, [1003], default));
        });
    }

    [Theory]
    [InlineData("/neftyanik/Administration/Members/Finance/701/Finance", RoleNames.Administrator, true, false)]
    [InlineData("/neftyanik/Member", RoleNames.Member, true, false)]
    [InlineData("/neftyanik/Member/Plots/801/Finance", RoleNames.Member, false, false)]
    [InlineData("/neftyanik/Administration/Members/Finance/701/Finance", RoleNames.Administrator, true, true)]
    [InlineData("/neftyanik/Member", RoleNames.Member, true, true)]
    [InlineData("/neftyanik/Member/Plots/801/Finance", RoleNames.Member, false, true)]
    public async Task Pages_RenderElectricityDetails_AndSummaryForMemberAndAdministrator(string url, string role, bool hasSummary, bool chargedToday)
    {
        using var factory = new PortalWebApplicationFactory();
        var today = DateOnly.FromDateTime(DateTime.Today);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, today, 40);
            if (chargedToday)
            {
                db.MemberElectricityReadings.Remove(await db.MemberElectricityReadings.SingleAsync(r => r.Id == 953));
                await db.SaveChangesAsync();
                (await db.Charges.SingleAsync(c => c.Id == 902)).ChargeDate = today;
                (await db.MemberElectricityReadings.SingleAsync(r => r.Id == 952)).ReadingDate = today;
                await db.SaveChangesAsync();
            }
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("electricity-view-user", role), cultureName: "ru-RU");
        var response = await client.GetAsync(url);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Предыдущие показания:", html);
        var expectedLabel = chargedToday ? "Показания на текущую дату:" : "Показания на дату начисления:";
        Assert.Equal(3, html.Split(expectedLabel).Length - 1); // The charge, active payment and cancelled payment.
        Assert.Contains("METER-DAY-NIGHT", html);
        Assert.Contains("1250", html);
        Assert.Contains("640", html);
        if (hasSummary)
        {
            Assert.Contains("Показания за предыдущий месяц", html);
            if (!chargedToday) Assert.Contains("Оплачено частично", html);
            Assert.Contains("Показания за текущий месяц переданы", html);
            Assert.Contains("Показания за текущий месяц не переданы", html);
            Assert.Contains("Последние показания:", html);
            Assert.Contains("Всего к оплате за электричество", html);
        }
    }

    private static async Task SeedAsync(ApplicationDbContext db, DateOnly today, decimal paid)
    {
        var month = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        db.Users.Add(new ApplicationUser
        {
            Id = "electricity-view-user", UserName = "electricity-view@example.com", NormalizedUserName = "ELECTRICITY-VIEW@EXAMPLE.COM",
            Email = "electricity-view@example.com", SecurityStamp = Guid.NewGuid().ToString(),
            FirstName = "Test", LastName = "Member", IsActive = true, MustChangePassword = false
        });
        db.Members.Add(new Member { Id = 701, FullName = "Electricity Test", ApplicationUserId = "electricity-view-user", ElectricityMeterType = MemberElectricityMeterType.DayNight });
        db.Plots.Add(new Plot { Id = 801, Number = "E-801" });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 701, PlotId = 801, ValidFrom = new DateOnly(2020, 1, 1) });
        db.ChargeTypes.AddRange(new ChargeType { Id = 811, Code = ChargeTypeCodes.Electricity, Name = "Electricity test" }, new ChargeType { Id = 812, Name = "Dues test" });
        db.MemberElectricityMeters.AddRange(
            new MemberElectricityMeter { Id = 821, MemberId = 701, BillingPlotId = 801, Name = "METER-DAY-NIGHT" },
            new MemberElectricityMeter { Id = 822, MemberId = 701, BillingPlotId = 801, Name = "METER-MISSING" });
        db.Charges.AddRange(
            new Charge { Id = 901, PlotId = 801, ChargeTypeId = 811, ChargeDate = month.AddMonths(-1), Amount = 50 },
            new Charge { Id = 902, PlotId = 801, ChargeTypeId = 811, ChargeDate = month.AddDays(20), Amount = 100 },
            new Charge { Id = 903, PlotId = 801, ChargeTypeId = 811, ChargeDate = month.AddDays(21), Amount = 500, CancelledAtUtc = DateTime.UtcNow },
            new Charge { Id = 904, PlotId = 801, ChargeTypeId = 812, ChargeDate = month, Amount = 800 });
        db.MemberElectricityReadings.AddRange(
            new MemberElectricityReading { Id = 951, MemberElectricityMeterId = 821, ReadingDate = month.AddDays(-1), CurrentReading = 1000, CurrentNightReading = 500, IsInitialReading = true },
            new MemberElectricityReading { Id = 952, MemberElectricityMeterId = 821, ReadingDate = month.AddDays(20), CurrentReading = 1250, CurrentNightReading = 640, ChargeId = 902 },
            new MemberElectricityReading { Id = 953, MemberElectricityMeterId = 821, ReadingDate = today, CurrentReading = 1400, CurrentNightReading = 700 });
        db.Payments.AddRange(
            new Payment { Id = 1001, MemberId = 701, PlotId = 801, PaymentDate = today, Amount = 1000, PaymentMethod = PaymentMethod.Cash },
            new Payment { Id = 1002, MemberId = 701, PlotId = 801, PaymentDate = today.AddDays(-1), Amount = 100, CancelledAtUtc = DateTime.UtcNow, PaymentMethod = PaymentMethod.Cash },
            new Payment { Id = 1003, MemberId = 701, PlotId = 801, PaymentDate = today, Amount = 800, PaymentMethod = PaymentMethod.Cash });
        if (paid > 0) db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = 1001, ChargeId = 902, Amount = paid });
        db.PaymentAllocations.AddRange(
            new PaymentAllocation { PaymentId = 1002, ChargeId = 902, Amount = 100 },
            new PaymentAllocation { PaymentId = 1003, ChargeId = 904, Amount = 800 });
        await db.SaveChangesAsync();
    }
}
