using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Imports;
using Xunit;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;
using static Neftyanik.Portal.Web.Imports.YearWorkbook;

namespace Neftyanik.Portal.Web.Tests;

public sealed class YearTransferTests
{
    private const string Admin = "annual-admin";
    private static YearTransferService Service(ApplicationDbContext db)
    {
        var audit = new FinancialAuditService(db, new HttpContextAccessor());
        return new(db, audit, new MemberElectricityService(db, audit), new AssociationElectricityService(db, audit));
    }
    internal static YearTransferRow Payment(string key = "P1", int year = 2026, PaymentMethod method = PaymentMethod.Cash) =>
        new(2, key, new(year, 6, 1), "", "Annual Owner", "001", "", false, 50m, null, null, null, method, key, "Payment from Excel", false, "");
    internal static YearTransferRow Reading(string key = "R1", bool initial = true, string scope = "member") =>
        new(2, key, new(2026, 1, 1), scope, scope == "member" ? "Annual Owner" : "", scope == "member" ? "001" : "",
            scope == "member" ? "M-1" : "", initial, 100m, 50m, null, null, 0, "", "", false, "");
    internal static async Task Seed(ApplicationDbContext db, bool two = false, bool tariffs = true)
    {
        db.Users.Add(new ApplicationUser { Id = Admin, UserName = Admin });
        db.Members.Add(new Member { Id = 701, FullName = "Annual Owner", ElectricityMeterType = MemberElectricityMeterType.DayNight, HasTwoElectricityMeters = two });
        db.Plots.Add(new Plot { Id = 702, Number = "001" });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 701, PlotId = 702, ValidFrom = new(2020, 1, 1) });
        db.MemberElectricityMeters.Add(new MemberElectricityMeter { Id = 703, MemberId = 701, BillingPlotId = 702, MeterNumber = "M-1" });
        await db.SaveChangesAsync();
        var plot = await db.Plots.FindAsync(702); plot!.MemberElectricityMeterId = 703;
        db.ChargeTypes.Add(new ChargeType { Id = 704, Name = "Test charge" });
        db.Charges.Add(new Charge { MemberId = 701, PlotId = 702, ChargeTypeId = 704, ChargeDate = new(2025, 1, 1), Amount = 200 });
        if (tariffs)
        {
            db.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new(2020, 1, 1), Rate = 5, NightRate = 2 });
            db.AssociationElectricityTariffs.Add(new AssociationElectricityTariff { EffectiveFrom = new(2020, 1, 1), DayRate = 6, NightRate = 3 });
        }
        await db.SaveChangesAsync();
    }
    private static List<YearTransferRow> Parsed(string kind, params YearTransferRow[] rows)
    {
        var parsed = Read(Write(kind, 2026, rows), kind, 2026);
        Assert.Empty(parsed.Issues); return parsed.Rows;
    }

    [Theory]
    [InlineData(Payments)]
    [InlineData(Readings)]
    public void WorkbookRoundTripPreservesDataAndRejectsWrongYearOrKind(string kind)
    {
        var bytes = Write(kind, 2026, [kind == Payments ? Payment() : Reading()]);
        var result = Read(bytes, kind, 2026);
        Assert.Empty(result.Issues); Assert.Single(result.Rows); Assert.Equal("001", result.Rows[0].Plot);
        Assert.NotEmpty(Read(bytes, kind, 2025).Issues);
        Assert.NotEmpty(Read(bytes, kind == Payments ? Readings : Payments, 2026).Issues);
    }

    [Fact]
    public void FullYearExportCanExceedThePerUploadLimit()
    {
        var bytes = Write(Payments, 2026, Enumerable.Range(1, MaxRows + 1).Select(i => Payment("P" + i)));
        using var book = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(MaxRows + 2, book.Worksheet("Data").LastRowUsed()!.RowNumber());
        Assert.NotEmpty(Read(bytes, Payments, 2026).Issues);
    }

    [Theory]
    [InlineData("formula")]
    [InlineData("duplicate")]
    [InlineData("date")]
    [InlineData("amount")]
    [InlineData("cancel")]
    [InlineData("extraColumn")]
    public void InvalidPaymentWorkbookShowsRowErrors(string error)
    {
        using var book = new XLWorkbook(new MemoryStream(Write(Payments, 2026, [Payment()])));
        var sheet = book.Worksheet("Data");
        switch (error)
        {
            case "formula": sheet.Cell(2, 5).FormulaA1 = "1+2"; break;
            case "duplicate": sheet.Row(2).CopyTo(sheet.Row(3)); break;
            case "date": sheet.Cell(2, 2).Value = "2025-12-31"; break;
            case "amount": sheet.Cell(2, 5).Value = 1.234m; break;
            case "cancel": sheet.Cell(2, 9).Value = "1"; break;
            case "extraColumn": sheet.Cell(2, 11).Value = "unexpected"; break;
        }
        using var stream = new MemoryStream(); book.SaveAs(stream);
        Assert.NotEmpty(Read(stream.ToArray(), Payments, 2026).Issues);
    }

    [Fact]
    public async Task PaymentPreviewRollsBackAndConfirmationAllocatesAndRoundTripsWithoutDuplicates()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var service = Service(db); var rows = Parsed(Payments, Payment());
            var preview = await service.ExecuteAsync(Payments, 2026, rows, Admin, false, default);
            Assert.Empty(preview.Issues); Assert.Equal(1, preview.Added);
            Assert.Equal(0, await db.Payments.CountAsync()); Assert.Equal(0, await db.FinancialAuditLogs.CountAsync());
            Assert.False(await db.SystemSettings.AnyAsync(s => s.Key.StartsWith("YearTransfer:")));
            var saved = await service.ExecuteAsync(Payments, 2026, rows, Admin, true, default);
            Assert.Empty(saved.Issues); Assert.Equal(1, saved.Added);
            Assert.Equal(50m, (await db.PaymentAllocations.SingleAsync()).Amount);
            var replay = await service.ExecuteAsync(Payments, 2026, rows, Admin, true, default);
            Assert.Empty(replay.Issues); Assert.Equal(1, replay.Skipped);
            var exported = Read(await service.ExportAsync(Payments, 2026, default), Payments, 2026);
            Assert.Empty(exported.Issues); Assert.Equal("P1", Assert.Single(exported.Rows).Key);
            var roundTrip = await service.ExecuteAsync(Payments, 2026, exported.Rows, Admin, true, default);
            Assert.Empty(roundTrip.Issues); Assert.Equal(1, roundTrip.Skipped); Assert.Equal(1, await db.Payments.CountAsync());
            var changed = await service.ExecuteAsync(Payments, 2026, rows.Select(r => r with { Value = 60 }).ToList(), Admin, true, default);
            Assert.NotEmpty(changed.Issues); Assert.Equal(50m, (await db.Payments.SingleAsync()).Amount);
        });
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.BankTransfer)]
    [InlineData(PaymentMethod.Card)]
    [InlineData(PaymentMethod.Other)]
    [InlineData(PaymentMethod.OpeningBalance)]
    public async Task HistoricalPaymentMethodsArePreserved(PaymentMethod method)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var result = await Service(db).ExecuteAsync(Payments, 2026, Parsed(Payments, Payment(method: method)), Admin, true, default);
            Assert.Empty(result.Issues); Assert.Equal(method, (await db.Payments.SingleAsync()).PaymentMethod);
            var summary = new Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel(db); await summary.OnGetAsync(default);
            Assert.Equal(method == PaymentMethod.OpeningBalance ? 0m : 50m, summary.Summary.CurrentCashAmount);
        });
    }

    [Fact]
    public async Task CancelledPaymentsRemainCancelledAndDoNotAffectCash()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var result = await Service(db).ExecuteAsync(Payments, 2026, Parsed(Payments, Payment() with { Cancelled = true, CancellationReason = "Cancelled source payment" }), Admin, true, default);
            Assert.Empty(result.Issues); var payment = await db.Payments.SingleAsync(); Assert.NotNull(payment.CancelledAtUtc);
            var summary = new Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel(db); await summary.OnGetAsync(default);
            Assert.Equal(0m, summary.Summary.CurrentCashAmount);
        });
    }

    [Fact]
    public async Task PaymentFailureRollsBackAllEarlierRows()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db);
            var rows = Parsed(Payments, Payment(), Payment("P2") with { Member = "Missing member", Date = new(2026, 7, 1) });
            var result = await Service(db).ExecuteAsync(Payments, 2026, rows, Admin, true, default);
            Assert.NotEmpty(result.Issues); Assert.Equal(0, await db.Payments.CountAsync()); Assert.Equal(0, await db.PaymentAllocations.CountAsync());
            Assert.False(await db.SystemSettings.AnyAsync(s => s.Key.StartsWith("YearTransfer:")));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemberReadingsAreSortedBilledAndRoundTripWithPhysicalMeters(bool two)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, two);
            var initial = Reading();
            var next = Reading("R2", false) with { Date = new(2026, 2, 1), Value = two ? 70 : 150, Night = two ? 30 : 65, Second = two ? 80 : null, SecondNight = two ? 35 : null };
            var service = Service(db); var rows = Parsed(Readings, next, initial);
            var preview = await service.ExecuteAsync(Readings, 2026, rows, Admin, false, default);
            Assert.Empty(preview.Issues); Assert.Equal(0, await db.MemberElectricityReadings.CountAsync());
            Assert.Equal(280m, preview.Lines.Single(l => l.Key == "R2").Amount);
            var result = await service.ExecuteAsync(Readings, 2026, rows, Admin, true, default);
            Assert.Empty(result.Issues); Assert.Equal(2, await db.MemberElectricityReadings.CountAsync());
            var regular = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.Equal(150m, regular.CurrentReading); Assert.Equal(65m, regular.CurrentNightReading); Assert.Equal(280m, regular.Amount);
            Assert.Equal(two, regular.PhysicalMeterReadingsJson != null);
            var exported = Read(await service.ExportAsync(Readings, 2026, default), Readings, 2026); Assert.Empty(exported.Issues);
            Assert.Contains(exported.Rows, r => r.Key == "R2" && (!two || r.Second == 80));
            var replay = await service.ExecuteAsync(Readings, 2026, exported.Rows, Admin, true, default);
            Assert.Empty(replay.Issues); Assert.Equal(2, replay.Skipped); Assert.Equal(2, await db.Charges.CountAsync());
        });
    }

    [Fact]
    public async Task MainMeterReadingsSupportAtomicPreviewAndTariffCalculation()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var service = Service(db);
            var rows = Parsed(Readings, Reading(scope: "association"), Reading("R2", false, "association") with { Date = new(2026, 2, 1), Value = 110, Night = 55 });
            var preview = await service.ExecuteAsync(Readings, 2026, rows, Admin, false, default);
            Assert.Empty(preview.Issues); Assert.Equal(0, await db.AssociationElectricityReadings.CountAsync());
            var saved = await service.ExecuteAsync(Readings, 2026, rows, Admin, true, default); Assert.Empty(saved.Issues);
            Assert.Equal(75m, (await db.AssociationElectricityReadings.SingleAsync(r => !r.IsInitialReading)).TotalSupplierAmount);
            var exported = Read(await service.ExportAsync(Readings, 2026, default), Readings, 2026);
            Assert.Empty(exported.Issues); Assert.All(exported.Rows, r => Assert.Equal("association", r.Scope));
            Assert.Empty((await service.ExecuteAsync(Readings, 2026, exported.Rows, Admin, true, default)).Issues);
            Assert.Equal(2, await db.AssociationElectricityReadings.CountAsync());
        });
    }

    [Fact]
    public async Task MissingTariffOrConflictingHistoryRollsBackReadingsAndCharges()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db, tariffs: false); var service = Service(db);
            var rows = Parsed(Readings, Reading(), Reading("R2", false) with { Date = new(2026, 2, 1), Value = 120, Night = 60 });
            Assert.NotEmpty((await service.ExecuteAsync(Readings, 2026, rows, Admin, true, default)).Issues);
            Assert.Equal(0, await db.MemberElectricityReadings.CountAsync()); Assert.Equal(1, await db.Charges.CountAsync());
        });
    }

    [Fact]
    public async Task NightValuesForSingleRateMetersAreRejectedWithoutLosingInput()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var member = await db.Members.FindAsync(701); member!.ElectricityMeterType = MemberElectricityMeterType.SingleRate; await db.SaveChangesAsync();
            var result = await Service(db).ExecuteAsync(Readings, 2026, Parsed(Readings, Reading()), Admin, true, default);
            Assert.NotEmpty(result.Issues); Assert.Equal(0, await db.MemberElectricityReadings.CountAsync());
        });
    }

    [Fact]
    public async Task ExportFiltersYearAndImportDoesNotUseOtherAssociationsRecords()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db); var service = Service(db);
            db.Payments.AddRange(new Payment { MemberId = 701, PlotId = 702, PaymentDate = new(2025, 12, 31), Amount = 10, PaymentMethod = PaymentMethod.Cash },
                new Payment { MemberId = 701, PlotId = 702, PaymentDate = new(2027, 1, 1), Amount = 20, PaymentMethod = PaymentMethod.Card });
            await db.SaveChangesAsync();
            Assert.Empty((await service.ExecuteAsync(Payments, 2026, Parsed(Payments, Payment()), Admin, true, default)).Issues);
            var parsed = Read(await service.ExportAsync(Payments, 2026, default), Payments, 2026); Assert.Empty(parsed.Issues);
            Assert.Equal(2026, Assert.Single(parsed.Rows).Date.Year);
            db.Associations.Add(new Association { Id = 22, Name = "Other", Slug = "other" }); await db.SaveChangesAsync();
        });
        await factory.ExecuteDbContextAsync(async other =>
        {
            var rejected = await Service(other).ExecuteAsync(Payments, 2026, Parsed(Payments, Payment()), Admin, true, default);
            Assert.NotEmpty(rejected.Issues); Assert.Equal(0, await other.Payments.CountAsync());
        }, "other");
    }
}
