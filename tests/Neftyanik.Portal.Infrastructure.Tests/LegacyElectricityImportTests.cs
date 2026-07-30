using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.LegacyImport;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.LegacyImport;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class LegacyElectricityImportTests
{
    [Fact]
    public void ParsePlotNumbers_SplitsCompoundPlotIdentifier()
    {
        var plots = LegacyElectricityImportValidator.ParsePlotNumbers("167.133");

        Assert.Equal(["167", "133"], plots);
    }

    [Fact]
    public void ParsePlotNumbers_PreservesSinglePlotNumber()
    {
        var plots = LegacyElectricityImportValidator.ParsePlotNumbers("132.0");

        Assert.Equal(["132"], plots);
    }

    [Fact]
    public async Task WorkbookReader_DoesNotInferPlotsFromFloatingPointFormatting()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 167.133m;
            sheet.Cell("B2").Value = "Тестов";
        });

        var reader = new LegacyElectricityWorkbookReader();
        var workbook = await reader.ReadAsync(workbookPath, CancellationToken.None);

        Assert.Equal("167.133", workbook.Sheets.Single().Rows.Single(row => row.RowNumber == 2).GetValue("A"));
    }

    [Fact]
    public void NormalizeWhitespace_TrimsAndCollapsesSpaces()
    {
        Assert.Equal("Иванов Иван", LegacyElectricityImportValidator.NormalizeWhitespace("  Иванов   Иван  "));
    }

    [Fact]
    public async Task Validator_RecognizesContinuationRowAsSecondMeter()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 153.157m;
            sheet.Cell("B2").Value = "Фоменко дом1";
            sheet.Cell("C2").Value = 323m;
            sheet.Cell("D2").Value = 353m;
            sheet.Cell("B3").Value = "Фоменко дом2";
            sheet.Cell("C3").Value = 2969m;
            sheet.Cell("D3").Value = 2983m;
        });

        var validated = await ReadAndValidateAsync(workbookPath);

        Assert.Equal(2, validated.Candidates.Count);
        Assert.Equal("Фоменко", validated.Candidates[0].NormalizedMemberName);
        Assert.Equal("дом1", validated.Candidates[0].MeterLabel, ignoreCase: true);
        Assert.Equal("дом2", validated.Candidates[1].MeterLabel, ignoreCase: true);
        Assert.Equal(["153", "157"], validated.Candidates[1].PlotNumbers);
    }

    [Fact]
    public async Task Validator_CombinesT1AndT2Values()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 137m;
            sheet.Cell("B2").Value = "Бондаренко";
            sheet.Cell("C2").Value = "т1 23069";
            sheet.Cell("D2").Value = 23100m;
            sheet.Cell("C3").Value = "т2 13258";
            sheet.Cell("D3").Value = 13270m;
        });

        var validated = await ReadAndValidateAsync(workbookPath);
        var candidate = Assert.Single(validated.Candidates);

        Assert.True(candidate.HasCombinedTariffSource);
        Assert.Equal(36327m, candidate.PreviousReadingValue);
        Assert.Equal(36370m, candidate.CurrentReadingValue);
    }

    [Fact]
    public async Task Validator_ParsesExplicitReadingDate()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 135.142m;
            sheet.Cell("B2").Value = "Кулик";
            sheet.Cell("C2").Value = "1795,дані на 19.05.23 р";
        });

        var validated = await ReadAndValidateAsync(workbookPath);
        var candidate = Assert.Single(validated.Candidates);

        Assert.Equal(new DateOnly(2023, 5, 19), candidate.PreviousReadingDate);
        Assert.Equal(1795m, candidate.PreviousReadingValue);
    }

    [Fact]
    public async Task Validator_ReportsDisconnectedRowWithoutCreatingReading()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 134m;
            sheet.Cell("B2").Value = "Лазарева";
            sheet.Cell("C2").Value = "отключена";
        });

        var validated = await ReadAndValidateAsync(workbookPath);
        var candidate = Assert.Single(validated.Candidates);

        Assert.Null(candidate.PreviousReadingValue);
        Assert.Contains(validated.Issues, issue => issue.Code == "MissingReadingMarker");
    }

    [Fact]
    public async Task ImportService_DryRunDoesNotChangeDatabase()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 132m;
            sheet.Cell("B2").Value = "Тестов";
            sheet.Cell("C2").Value = 100m;
            sheet.Cell("D2").Value = 120m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);

        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(false, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.False(result.Committed);
        Assert.Equal(0, await database.Context.Members.CountAsync());
        Assert.Equal(0, await database.Context.Plots.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task ImportService_CommitWithBlockingIssues_DoesNotCommit()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var primary = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(primary);
            primary.Cell("A2").Value = 100m;
            primary.Cell("B2").Value = "Иванов";
            primary.Cell("C2").Value = 100m;
            primary.Cell("D2").Value = 120m;

            var supplemental = workbook.AddWorksheet("Лист2");
            supplemental.Cell("A1").Value = 100m;
            supplemental.Cell("B1").Value = "Петров";
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);

        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.True(result.CommitRequested);
        Assert.False(result.ForceRequested);
        Assert.False(result.Committed);
        Assert.False(result.CommittedWithBlockingIssues);
        Assert.True(result.BlockingIssueCount > 0);
        Assert.Equal(0, await database.Context.Members.CountAsync());
        Assert.Equal(0, await database.Context.Plots.CountAsync());
        Assert.Equal(0, await database.Context.PlotOwnerships.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task ImportService_ForceWithoutCommit_DoesNotCommit()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 132m;
            sheet.Cell("B2").Value = "Тестов";
            sheet.Cell("C2").Value = 100m;
            sheet.Cell("D2").Value = 120m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);

        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(false, true, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.False(result.CommitRequested);
        Assert.True(result.ForceRequested);
        Assert.False(result.Committed);
        Assert.False(result.CommittedWithBlockingIssues);
        Assert.Equal(0, await database.Context.Members.CountAsync());
        Assert.Equal(0, await database.Context.Plots.CountAsync());
        Assert.Equal(0, await database.Context.PlotOwnerships.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task ImportService_ForceCommit_CommitsSafeEntitiesAndSkipsConflictingOwnerships()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var primary = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(primary);
            primary.Cell("A2").Value = 100m;
            primary.Cell("B2").Value = "Иванов";
            primary.Cell("C2").Value = 100m;
            primary.Cell("D2").Value = 120m;
            primary.Cell("A3").Value = 100m;
            primary.Cell("B3").Value = "Петров";
            primary.Cell("C3").Value = 130m;
            primary.Cell("D3").Value = 150m;
            primary.Cell("A4").Value = 200m;
            primary.Cell("B4").Value = "Сидоров";
            primary.Cell("C4").Value = 200m;
            primary.Cell("D4").Value = 230m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);

        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, true, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.True(result.CommitRequested);
        Assert.True(result.ForceRequested);
        Assert.True(result.Committed);
        Assert.True(result.CommittedWithBlockingIssues);
        Assert.True(result.BlockingIssueCount > 0);
        Assert.Equal(3, await database.Context.Members.CountAsync());
        Assert.Equal(2, await database.Context.Plots.CountAsync());
        Assert.Equal(1, await database.Context.PlotOwnerships.CountAsync());
        Assert.Equal(1, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(2, await database.Context.MemberElectricityReadings.CountAsync());
        Assert.Equal(2, result.SkippedBecauseOfConflict.Ownerships);
        Assert.Equal(2, result.SkippedBecauseOfConflict.Meters);
        Assert.Equal(4, result.SkippedBecauseOfConflict.Readings);
        Assert.Contains(result.Issues, issue => issue.Code == "OwnershipAndMeterImportSkippedBecauseOfConflict");

        var plots = await database.Context.Plots
            .Include(plot => plot.PlotOwnerships.Where(ownership => ownership.ValidTo == null))
            .ToListAsync();

        Assert.All(plots, plot => Assert.True(plot.PlotOwnerships.Count <= 1));
        Assert.Empty(plots.Single(plot => plot.Number == "100").PlotOwnerships);
        Assert.Single(plots.Single(plot => plot.Number == "200").PlotOwnerships);
    }

    [Fact]
    public async Task ImportService_ForcedRerun_DoesNotCreateDuplicates()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var primary = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(primary);
            primary.Cell("A2").Value = 100m;
            primary.Cell("B2").Value = "Иванов";
            primary.Cell("C2").Value = 100m;
            primary.Cell("D2").Value = 120m;
            primary.Cell("A3").Value = 100m;
            primary.Cell("B3").Value = "Петров";
            primary.Cell("C3").Value = 130m;
            primary.Cell("D3").Value = 150m;
            primary.Cell("A4").Value = 200m;
            primary.Cell("B4").Value = "Сидоров";
            primary.Cell("C4").Value = 200m;
            primary.Cell("D4").Value = 230m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);
        var reportsPath = Path.Combine(database.RootPath, "Reports");

        var first = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, true, WorkbookRelativePath: workbookPath, ReportsRelativePath: reportsPath));
        var second = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, true, WorkbookRelativePath: workbookPath, ReportsRelativePath: reportsPath));

        Assert.True(first.Committed);
        Assert.True(second.Committed);
        Assert.Equal(3, await database.Context.Members.CountAsync());
        Assert.Equal(2, await database.Context.Plots.CountAsync());
        Assert.Equal(1, await database.Context.PlotOwnerships.CountAsync());
        Assert.Equal(1, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(2, await database.Context.MemberElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task ImportService_IsIdempotentOnSecondCommit()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 132m;
            sheet.Cell("B2").Value = "Тестов";
            sheet.Cell("C2").Value = 100m;
            sheet.Cell("D2").Value = 120m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);
        var reportsPath = Path.Combine(database.RootPath, "Reports");

        var first = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, WorkbookRelativePath: workbookPath, ReportsRelativePath: reportsPath));
        var second = await service.ExecuteAsync(new LegacyElectricityImportRequest(true, WorkbookRelativePath: workbookPath, ReportsRelativePath: reportsPath));

        Assert.True(first.Committed);
        Assert.True(second.Committed);
        Assert.Equal(1, await database.Context.Members.CountAsync());
        Assert.Equal(1, await database.Context.Plots.CountAsync());
        Assert.Equal(1, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(2, await database.Context.MemberElectricityReadings.CountAsync());
    }

    [Fact]
    public async Task ImportService_ReportsConflictingOwnersForSamePlot()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var primary = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(primary);
            primary.Cell("A2").Value = 132m;
            primary.Cell("B2").Value = "Жуков";
            primary.Cell("C2").Value = 100m;

            var supplemental = workbook.AddWorksheet("Лист2");
            supplemental.Cell("A1").Value = 132m;
            supplemental.Cell("B1").Value = "Лазарева";
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath);

        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(false, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.Contains(result.Issues, issue => issue.Code == "ConflictingPlotOwnerInWorkbook" && issue.Severity == LegacyElectricityImportIssueSeverity.Error);
    }

    [Fact]
    public async Task ImportService_DetectsDuplicateReadingConflict()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 132m;
            sheet.Cell("B2").Value = "Тестов";
            sheet.Cell("C2").Value = 100m;
            sheet.Cell("D2").Value = 120m;
        });

        await using var database = await CreateDatabaseAsync();
        database.Context.Members.Add(new Member { FullName = "Тестов", IsActive = true });
        await database.Context.SaveChangesAsync();
        var member = await database.Context.Members.SingleAsync();
        database.Context.Plots.Add(new Plot { Number = "132", IsActive = true, CreatedAtUtc = DateTime.UtcNow });
        await database.Context.SaveChangesAsync();
        var plot = await database.Context.Plots.SingleAsync();
        database.Context.PlotOwnerships.Add(new PlotOwnership { PlotId = plot.Id, MemberId = member.Id, ValidFrom = new DateOnly(2025, 7, 1), IsPrimaryContact = true });
        database.Context.MemberElectricityMeters.Add(new MemberElectricityMeter { MemberId = member.Id, BillingPlotId = plot.Id, MeterNumber = LegacyElectricityDataImportService.BuildLegacyMeterKey("Тестов", ["132"], null, false), Name = "Legacy meter", IsActive = true });
        await database.Context.SaveChangesAsync();
        var meter = await database.Context.MemberElectricityMeters.SingleAsync();
        plot.MemberElectricityMeterId = meter.Id;
        database.Context.MemberElectricityReadings.Add(new MemberElectricityReading { MemberElectricityMeterId = meter.Id, ReadingDate = new DateOnly(2025, 6, 1), CurrentReading = 999m, IsInitialReading = true });
        await database.Context.SaveChangesAsync();

        var service = CreateService(database.Context, database.RootPath);
        var result = await service.ExecuteAsync(new LegacyElectricityImportRequest(false, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports")));

        Assert.Contains(result.Issues, issue => issue.Code == "DuplicateReadingDateConflict");
    }

    [Fact]
    public async Task ImportService_RollsBackOnCriticalFailure()
    {
        var workbookPath = CreateWorkbook(workbook =>
        {
            var sheet = workbook.AddWorksheet("Лист1");
            AddPrimaryHeader(sheet);
            sheet.Cell("A2").Value = 132m;
            sheet.Cell("B2").Value = "Тестов";
            sheet.Cell("C2").Value = 100m;
            sheet.Cell("D2").Value = 120m;
        });

        await using var database = await CreateDatabaseAsync();
        var service = CreateService(database.Context, database.RootPath, new ThrowingExecutionHook());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(new LegacyElectricityImportRequest(true, WorkbookRelativePath: workbookPath, ReportsRelativePath: Path.Combine(database.RootPath, "Reports"))));
        Assert.Equal(0, await database.Context.Members.CountAsync());
        Assert.Equal(0, await database.Context.Plots.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityMeters.CountAsync());
        Assert.Equal(0, await database.Context.MemberElectricityReadings.CountAsync());
    }

    private static async Task<LegacyValidatedWorkbook> ReadAndValidateAsync(string workbookPath)
    {
        var reader = new LegacyElectricityWorkbookReader();
        var workbook = await reader.ReadAsync(workbookPath, CancellationToken.None);
        var validator = new LegacyElectricityImportValidator();
        return validator.Validate(workbook, new LegacyElectricityImportConfigurationSnapshot(new DateOnly(2025, 6, 1), new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 1), workbookPath, Path.GetDirectoryName(workbookPath)!));
    }

    private static string CreateWorkbook(Action<XLWorkbook> configure)
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy-electricity-{Guid.NewGuid():N}.xlsx");
        using var workbook = new XLWorkbook();
        configure(workbook);
        workbook.SaveAs(path);
        return path;
    }

    private static void AddPrimaryHeader(IXLWorksheet sheet)
    {
        sheet.Cell("A1").Value = "номер участка";
        sheet.Cell("B1").Value = "Фамилия";
        sheet.Cell("C1").Value = "показания на 1 июня";
        sheet.Cell("D1").Value = "показания на 1 июля";
    }

    private static LegacyElectricityDataImportService CreateService(ApplicationDbContext dbContext, string contentRootPath, ILegacyElectricityImportExecutionHook? hook = null)
    {
        return new LegacyElectricityDataImportService(
            dbContext,
            new TestHostEnvironment(contentRootPath),
            new LegacyElectricityWorkbookReader(),
            new LegacyElectricityImportValidator(),
            hook ?? new NoOpLegacyElectricityImportExecutionHook(),
            Options.Create(new LegacyElectricityImportOptions
            {
                WorkbookRelativePath = "unused.xlsx",
                ReportsRelativePath = Path.Combine(contentRootPath, "Reports"),
                DefaultPreviousReadingDate = new DateOnly(2025, 6, 1),
                DefaultCurrentReadingDate = new DateOnly(2025, 7, 1),
                OwnershipEffectiveFrom = new DateOnly(2025, 7, 1)
            }),
            NullLogger<LegacyElectricityDataImportService>.Instance);
    }

    private static async Task<TestDatabaseScope> CreateDatabaseAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "legacy-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        var databasePath = Path.Combine(rootPath, "test.db");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
        return new TestDatabaseScope(rootPath, dbContext);
    }

    private sealed class TestDatabaseScope : IAsyncDisposable
    {
        public TestDatabaseScope(string rootPath, ApplicationDbContext context)
        {
            RootPath = rootPath;
            Context = context;
        }

        public string RootPath { get; }

        public ApplicationDbContext Context { get; }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
        }

        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class ThrowingExecutionHook : ILegacyElectricityImportExecutionHook
    {
        public Task OnBeforeCommitAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated failure before commit.");
        }
    }
}
