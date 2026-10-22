using System.Globalization;
using System.Security.Claims;
using ClosedXML.Excel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Imports;
using Xunit;
using ImportModel = Neftyanik.Portal.Web.Pages.Administration.Import.IndexModel;
using FinanceModel = Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel;
using FundsModel = Neftyanik.Portal.Web.Pages.Administration.Finance.FundsModel;

namespace Neftyanik.Portal.Web.Tests;

public class ExcelOnboardingTests
{
    private static readonly DateOnly Date = new(2026, 1, 1);
    private static OnboardingRow Row(int row = 2, string key = "M1", string name = "Test Owner", string plot = "001", decimal balance = 100m) =>
        new(row, key, name, "+380501234567", "test@example.com", plot, plot.Length > 0 ? "Street 1" : "", plot.Length > 0 ? 600 : null, "", balance);

    private static byte[] Workbook(Action<IXLWorksheet> fill)
    {
        using var book = new XLWorkbook(new MemoryStream(ExcelOnboarding.Template()));
        fill(book.Worksheet("Import"));
        using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }

    [Theory]
    [InlineData("ru-RU")]
    [InlineData("uk-UA")]
    [InlineData("en-US")]
    public void TemplateRoundTrip_HandlesLocalizedHeadersDecimalCommaAndLeadingZeros(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var bytes = Workbook(s => { s.Cell(2, 1).Value = "M01"; s.Cell(2, 2).Value = "Test Owner"; s.Cell(2, 5).Value = "001"; s.Cell(2, 7).Value = "600,25"; s.Cell(2, 9).Value = "-100,50"; });
            var result = ExcelOnboarding.Parse(bytes);
            Assert.Empty(result.Issues);
            var row = Assert.Single(result.Rows);
            Assert.Equal("001", row.PlotNumber); Assert.Equal(600.25m, row.Area); Assert.Equal(-100.50m, row.Balance);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("formula")]
    [InlineData("header")]
    [InlineData("number")]
    [InlineData("tooManyRows")]
    [InlineData("extraColumn")]
    public void ParserRejectsUnsafeOrInvalidInput(string kind)
    {
        var bytes = Workbook(s =>
        {
            s.Cell(2, 1).Value = "M1"; s.Cell(2, 2).Value = "Test Owner";
            switch (kind)
            {
                case "formula": s.Cell(2, 9).FormulaA1 = "1+2"; break;
                case "header": s.Cell(1, 1).Value = "Wrong"; break;
                case "number": s.Cell(2, 9).Value = "123.456"; break;
                case "tooManyRows": s.Cell(1002, 1).Value = "M2"; break;
                case "extraColumn": s.Cell(2, 10).Value = "unexpected"; break;
            }
        });
        Assert.NotEmpty(ExcelOnboarding.Parse(bytes).Issues);
    }

    [Fact]
    public void ParserRejectsCorruptFile() => Assert.NotEmpty(ExcelOnboarding.Parse([1, 2, 3]).Issues);

    [Fact]
    public async Task ImportGroupsOwnersPreservesBalancesAndDoesNotCreateCash()
    {
        await using var f = await Fixture.Create();
        var rows = new List<OnboardingRow> { Row(), Row(3, plot: "002", balance: -40), Row(4, "M2", "Member without plot", "", 0),
            new(5, "", "", "", "", "003", "", 500, "", 0) };
        var result = await ExcelOnboarding.SaveAsync(f.Db, rows, Date, "import-admin", default);
        Assert.Empty(result.Issues);
        Assert.Equal(2, await f.Db.Members.CountAsync()); Assert.Equal(3, await f.Db.Plots.CountAsync());
        Assert.Equal(2, await f.Db.PlotOwnerships.CountAsync());
        Assert.Equal(100m, (await f.Db.Charges.ToListAsync()).Sum(c => c.Amount));
        var payment = await f.Db.Payments.SingleAsync(); Assert.Equal(40m, payment.Amount); Assert.Equal(PaymentMethod.OpeningBalance, payment.PaymentMethod);
        var firstMember = await f.Db.Members.SingleAsync(m => m.FullName == "Test Owner");
        Assert.All(await f.Db.PlotOwnerships.ToListAsync(), o => Assert.Equal(firstMember.Id, o.MemberId));
        Assert.Equal(firstMember.Id, payment.MemberId);
        Assert.Equal(firstMember.Id, (await f.Db.Charges.SingleAsync(c => c.Amount == 100)).MemberId);
        var plotTotals = await f.Db.LoadActivePaymentTotalsByPlotAsync(await f.Db.Plots.Select(p => p.Id).ToArrayAsync(), default);
        Assert.Equal(40m, plotTotals[payment.PlotId!.Value]);
        var finance = new FinanceModel(f.Db); await finance.OnGetAsync(default);
        Assert.Equal(60m, finance.Summary.TotalCurrentDebt); Assert.Equal(0m, finance.Summary.CurrentCashAmount); Assert.Equal(0m, finance.Summary.CurrentNonCashAmount);
        var funds = new FundsModel(f.Db) { From = Date, To = Date.AddDays(1) }; await funds.OnGetAsync(default);
        Assert.All(funds.Rows, r => Assert.Equal(0m, r.Received));
        Assert.Single(await f.Db.FinancialAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task RepeatAndChangedFileCannotDuplicateExistingRecords()
    {
        await using var f = await Fixture.Create();
        Assert.Empty((await ExcelOnboarding.SaveAsync(f.Db, [Row()], Date, "import-admin", default)).Issues);
        Assert.NotEmpty((await ExcelOnboarding.SaveAsync(f.Db, [Row()], Date, "import-admin", default)).Issues);
        Assert.NotEmpty((await ExcelOnboarding.SaveAsync(f.Db, [Row(balance: 500)], Date.AddDays(1), "import-admin", default)).Issues);
        Assert.Equal(1, await f.Db.Members.CountAsync()); Assert.Equal(1, await f.Db.Charges.CountAsync());
    }

    [Theory]
    [InlineData("duplicatePlot")]
    [InlineData("conflictingMember")]
    [InlineData("duplicateName")]
    [InlineData("missingOwner")]
    [InlineData("negativeArea")]
    [InlineData("duplicateMemberOnly")]
    public async Task ValidationErrorsSaveNothing(string kind)
    {
        await using var f = await Fixture.Create();
        var rows = new List<OnboardingRow> { Row() };
        rows.Add(kind switch
        {
            "duplicatePlot" => Row(3),
            "conflictingMember" => Row(3, name: "Different", plot: "002"),
            "duplicateName" => Row(3, key: "M2", plot: "002"),
            "missingOwner" => Row(3, key: "", name: "", plot: "002"),
            "negativeArea" => Row(3, plot: "002") with { Area = -2 },
            _ => Row(3, plot: "")
        });
        if (kind == "duplicateMemberOnly") rows.Add(Row(4, plot: ""));
        var result = await ExcelOnboarding.SaveAsync(f.Db, rows, Date, "import-admin", default);
        Assert.NotEmpty(result.Issues);
        Assert.Equal(0, await f.Db.Members.CountAsync()); Assert.Equal(0, await f.Db.Plots.CountAsync()); Assert.Equal(0, await f.Db.Charges.CountAsync());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackWholeImport()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<DbUpdateException>(() => ExcelOnboarding.SaveAsync(f.Db, [Row()], Date, "nonexistent-user", default));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(0, await f.Db.Members.CountAsync()); Assert.Equal(0, await f.Db.Plots.CountAsync());
        Assert.False(await f.Db.SystemSettings.AnyAsync(s => s.Key.StartsWith("ExcelImport:")));
    }

    [Fact]
    public async Task SameFileCanBeImportedIntoSeparateAssociationsWithoutCrossTenantData()
    {
        await using var f = await Fixture.Create();
        f.Db.Associations.Add(new Association { Id = 2, Name = "Second", Slug = "second" }); await f.Db.SaveChangesAsync();
        Assert.Empty((await ExcelOnboarding.SaveAsync(f.Db, [Row()], Date, "import-admin", default)).Issues);
        await using var second = new ApplicationDbContext(f.Options, TestAssociations.Resolved(2, "second"));
        Assert.Empty((await ExcelOnboarding.SaveAsync(second, [Row()], Date, "import-admin", default)).Issues);
        Assert.Equal(1, await second.Members.CountAsync()); Assert.Equal(1, await f.Db.Members.CountAsync());
        Assert.All(await second.Charges.ToListAsync(), c => Assert.Equal(2, c.AssociationId));
    }

    [Fact]
    public async Task PreviewDoesNotWriteAndConfirmationUsesProtectedRows()
    {
        await using var f = await Fixture.Create();
        var protection = new EphemeralDataProtectionProvider();
        var model = Model(f.Db, protection);
        var bytes = Workbook(s => { s.Cell(2, 1).Value = "M1"; s.Cell(2, 2).Value = "Preview Owner"; s.Cell(2, 5).Value = "001"; s.Cell(2, 9).Value = "100"; });
        model.Upload = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Upload", "import.xlsx"); model.TransferDate = Date;
        Assert.IsType<PageResult>(await model.OnPostPreviewAsync(default)); Assert.NotNull(model.PreviewToken);
        Assert.Equal(0, await f.Db.Members.CountAsync());
        var confirm = Model(f.Db, protection); confirm.PreviewToken = model.PreviewToken; confirm.TransferDate = Date.AddDays(10);
        Assert.IsType<RedirectToPageResult>(await confirm.OnPostConfirmAsync(default));
        Assert.Equal(Date, (await f.Db.Charges.SingleAsync()).ChargeDate);
        var replay = Model(f.Db, protection); replay.PreviewToken = model.PreviewToken;
        Assert.IsType<PageResult>(await replay.OnPostConfirmAsync(default)); Assert.NotEmpty(replay.Preview!.Issues);
    }

    [Theory]
    [InlineData("tamper")]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("expired")]
    public async Task InvalidPreviewCannotBeConfirmed(string kind)
    {
        await using var f = await Fixture.Create();
        var protection = new EphemeralDataProtectionProvider();
        var protector = protection.CreateProtector("DachaHub.ExcelOnboarding.v1");
        var envelope = new ImportModel.PreviewEnvelope(kind == "tenant" ? 2 : 1, kind == "user" ? "other" : "import-admin",
            DateTimeOffset.UtcNow.AddMinutes(kind == "expired" ? -1 : 30), Date, [Row()]);
        var model = Model(f.Db, protection);
        model.PreviewToken = kind == "tamper" ? "tampered" : protector.Protect(System.Text.Json.JsonSerializer.Serialize(envelope));
        Assert.IsType<PageResult>(await model.OnPostConfirmAsync(default)); Assert.False(model.ModelState.IsValid);
        Assert.Equal(0, await f.Db.Members.CountAsync());
    }

    private static ImportModel Model(ApplicationDbContext db, IDataProtectionProvider protection)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "import-admin")], "test")) };
        var model = new ImportModel(db, protection, NullLogger<ImportModel>.Instance) { PageContext = new PageContext { HttpContext = http } };
        model.TempData = new TempDataDictionary(http, new MemoryTempData()); return model;
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public required SqliteConnection Connection { get; init; }
        public required DbContextOptions<ApplicationDbContext> Options { get; init; }
        public required ApplicationDbContext Db { get; init; }
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;
            var db = new ApplicationDbContext(options, TestAssociations.Neftyanik); await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser { Id = "import-admin", UserName = "import-admin", NormalizedUserName = "IMPORT-ADMIN" }); await db.SaveChangesAsync();
            return new Fixture { Connection = connection, Options = options, Db = db };
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
}
