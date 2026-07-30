using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.LegacyImport;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.LegacyImport;

internal sealed class LegacyElectricityDataImportService : ILegacyElectricityImportService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly LegacyElectricityWorkbookReader _workbookReader;
    private readonly LegacyElectricityImportValidator _validator;
    private readonly ILegacyElectricityImportExecutionHook _executionHook;
    private readonly IOptions<LegacyElectricityImportOptions> _options;
    private readonly ILogger<LegacyElectricityDataImportService> _logger;

    public LegacyElectricityDataImportService(
        ApplicationDbContext dbContext,
        IHostEnvironment hostEnvironment,
        LegacyElectricityWorkbookReader workbookReader,
        LegacyElectricityImportValidator validator,
        ILegacyElectricityImportExecutionHook executionHook,
        IOptions<LegacyElectricityImportOptions> options,
        ILogger<LegacyElectricityDataImportService> logger)
    {
        _dbContext = dbContext;
        _hostEnvironment = hostEnvironment;
        _workbookReader = workbookReader;
        _validator = validator;
        _executionHook = executionHook;
        _options = options;
        _logger = logger;
    }

    public async Task<LegacyElectricityImportResult> ExecuteAsync(LegacyElectricityImportRequest request, CancellationToken cancellationToken = default)
    {
        if (!_hostEnvironment.IsDevelopment())
        {
            throw new InvalidOperationException("Legacy electricity import is available only in the Development environment.");
        }

        var configuration = ResolveConfiguration(request);
        var workbookPath = ResolvePath(configuration.WorkbookRelativePath);
        if (!File.Exists(workbookPath))
        {
            throw new FileNotFoundException($"Legacy workbook was not found at '{workbookPath}'.", workbookPath);
        }

        Directory.CreateDirectory(ResolvePath(configuration.ReportsRelativePath));
        var workbook = await _workbookReader.ReadAsync(workbookPath, cancellationToken);
        var validatedWorkbook = _validator.Validate(workbook, configuration);

        var issues = new List<LegacyElectricityImportIssue>(validatedWorkbook.Issues);
        var entityStats = new MutableImportStatistics(validatedWorkbook.Diagnostics.PhysicalSourceRowsInspected);
        var context = await BuildDatabaseContextAsync(cancellationToken);
        var executableCandidates = await PrepareCandidatesAsync(validatedWorkbook.Candidates, issues, context, entityStats, configuration, request.Force, cancellationToken);

        var blockingIssueCount = issues.Count(issue => issue.Severity >= LegacyElectricityImportIssueSeverity.Error);
        var hasBlockingIssues = blockingIssueCount > 0;
        var committed = false;
        var committedWithBlockingIssues = false;

        if (request.Commit)
        {
            if (!hasBlockingIssues)
            {
                committed = await ApplyAsync(executableCandidates, context, entityStats, cancellationToken);
            }
            else if (request.Force)
            {
                committed = await ApplyAsync(executableCandidates, context, entityStats, cancellationToken);
                committedWithBlockingIssues = committed;
            }
        }

        var result = BuildResult(
            configuration,
            workbook,
            validatedWorkbook,
            issues,
            entityStats,
            request.Commit,
            request.Force,
            committed,
            committedWithBlockingIssues,
            blockingIssueCount,
            hasBlockingIssues);

        await WriteReportsAsync(result, cancellationToken);
        return result;
    }

    private LegacyElectricityImportConfigurationSnapshot ResolveConfiguration(LegacyElectricityImportRequest request)
    {
        var options = _options.Value;
        return new LegacyElectricityImportConfigurationSnapshot(
            request.DefaultPreviousReadingDate ?? options.DefaultPreviousReadingDate,
            request.DefaultCurrentReadingDate ?? options.DefaultCurrentReadingDate,
            request.OwnershipEffectiveFrom ?? options.OwnershipEffectiveFrom,
            request.WorkbookRelativePath ?? options.WorkbookRelativePath,
            request.ReportsRelativePath ?? options.ReportsRelativePath);
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        return Path.IsPathRooted(relativeOrAbsolutePath)
            ? relativeOrAbsolutePath
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, relativeOrAbsolutePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private async Task<LegacyDatabaseContext> BuildDatabaseContextAsync(CancellationToken cancellationToken)
    {
        var members = await _dbContext.Members.ToListAsync(cancellationToken);
        var plots = await _dbContext.Plots
            .Include(plot => plot.PlotOwnerships.Where(ownership => ownership.ValidTo == null))
            .Include(plot => plot.MemberElectricityMeter)
            .ToListAsync(cancellationToken);
        var meters = await _dbContext.MemberElectricityMeters
            .Include(meter => meter.Plots)
            .Include(meter => meter.Readings)
            .ToListAsync(cancellationToken);

        return new LegacyDatabaseContext(members, plots, meters);
    }

    private Task<List<ExecutableCandidate>> PrepareCandidatesAsync(
        IReadOnlyList<LegacyElectricityImportCandidate> candidates,
        List<LegacyElectricityImportIssue> issues,
        LegacyDatabaseContext context,
        MutableImportStatistics stats,
        LegacyElectricityImportConfigurationSnapshot configuration,
        bool forceRequested,
        CancellationToken cancellationToken)
    {
        var executable = new List<ExecutableCandidate>();
        var importMeterKeys = new Dictionary<string, LegacyElectricityImportCandidate>(StringComparer.OrdinalIgnoreCase);
        var importOwnershipKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var member = context.FindMember(candidate.NormalizedMemberName);
            var plotRecords = candidate.PlotNumbers.Select(plotNumber => context.FindOrCreatePlotStub(plotNumber)).ToList();
            var conflictingOwners = plotRecords
                .Where(plot => plot.ActiveOwnerName is not null && !string.Equals(plot.ActiveOwnerName, candidate.NormalizedMemberName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var conflictingPlot in conflictingOwners)
            {
                issues.Add(CreateConflictIssue(candidate, "ExistingPlotOwnerConflict", $"Plot {conflictingPlot.Number} already belongs to member '{conflictingPlot.ActiveOwnerName}'."));
            }

            var sortedPlots = candidate.PlotNumbers.OrderBy(NaturalPlotSortKey).ToList();
            var billingPlot = sortedPlots.FirstOrDefault();
            var meterKey = BuildLegacyMeterKey(candidate.NormalizedMemberName, sortedPlots, candidate.MeterLabel, candidate.HasCombinedTariffSource);
            if (importMeterKeys.TryGetValue(meterKey, out var existingImportMeter))
            {
                issues.Add(CreateConflictIssue(candidate, "DuplicateMeterInImport", $"The import contains duplicate meter key '{meterKey}' from rows {string.Join(", ", existingImportMeter.SourceRows)} and {string.Join(", ", candidate.SourceRows)}."));
            }
            else
            {
                importMeterKeys[meterKey] = candidate;
            }

            if (billingPlot is null)
            {
                issues.Add(CreateConflictIssue(candidate, "BillingPlotMissing", "A billing plot could not be determined for the candidate meter."));
            }

            if (candidate.PreviousReadingValue.HasValue && candidate.CurrentReadingValue.HasValue && candidate.CurrentReadingDate == candidate.PreviousReadingDate)
            {
                issues.Add(CreateConflictIssue(candidate, "DuplicateReadingDateInRow", "The row contains two different readings for the same date."));
            }

            var existingMeter = context.FindMeter(meterKey, candidate.NormalizedMemberName, sortedPlots);
            foreach (var plot in plotRecords.Where(plot => plot.AssignedMeterKey is not null && !string.Equals(plot.AssignedMeterKey, meterKey, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add(CreateConflictIssue(candidate, "PlotAssignedToDifferentMeter", $"Plot {plot.Number} is already assigned to another electricity meter."));
            }

            var readingPlans = BuildReadingPlans(candidate, issues);
            if (existingImportMeter is not null)
            {
                ValidateDuplicateImportMeterReadings(existingImportMeter, candidate, readingPlans, issues);
            }

            ValidateReadingHistory(candidate, existingMeter, readingPlans, issues);
            var blockingIssues = GetBlockingIssues(candidate, issues);
            var hasBlockingIssues = blockingIssues.Count > 0;
            var hasNonOwnershipBlockingIssues = blockingIssues.Any(issue => !IsOwnershipConflictIssue(issue));

            if (!hasBlockingIssues)
            {
                var ownershipPlans = new List<OwnershipPlan>();
                if (candidate.CreatesOwnerships)
                {
                    foreach (var plotRecord in plotRecords)
                    {
                        var ownershipKey = $"{candidate.NormalizedMemberName}|{plotRecord.Number}";
                        if (!importOwnershipKeys.Add(ownershipKey))
                        {
                            issues.Add(CreateCandidateIssue(candidate, LegacyElectricityImportIssueSeverity.Warning, "DuplicateOwnershipCandidatePrevented", $"Duplicate ownership candidate for member '{candidate.NormalizedMemberName}' and plot {plotRecord.Number} was prevented."));
                            continue;
                        }

                        ownershipPlans.Add(new OwnershipPlan(
                            plotRecord.Number,
                            plotRecord.ActiveOwnerName is null ? ImportActionKind.Create : ImportActionKind.Match));
                    }
                }

                var meterAction = existingMeter is null ? ImportActionKind.Create : ImportActionKind.Match;
                var readingActions = readingPlans
                    .Select(plan => existingMeter?.Readings.Any(reading => reading.ReadingDate == plan.ReadingDate && reading.CurrentReading == plan.Value) == true
                        ? ImportActionKind.Match
                        : ImportActionKind.Create)
                    .ToList();

                stats.RecordMember(candidate.NormalizedMemberName, member is null ? ImportActionKind.Create : ImportActionKind.Match);
                foreach (var plotRecord in plotRecords)
                {
                    stats.RecordPlot(plotRecord.Number, plotRecord.Entity is null ? ImportActionKind.Create : ImportActionKind.Match);
                }

                foreach (var ownershipPlan in ownershipPlans)
                {
                    stats.RecordOwnership(ownershipPlan.Action);
                }

                stats.RecordMeter(meterAction);
                foreach (var readingAction in readingActions)
                {
                    stats.RecordReading(readingAction);
                }

                executable.Add(new ExecutableCandidate(candidate, meterKey, billingPlot!, member, plotRecords, ownershipPlans, existingMeter?.Entity, readingPlans, configuration.OwnershipEffectiveFrom));
            }
            else if (forceRequested && !hasNonOwnershipBlockingIssues)
            {
                stats.RecordMember(candidate.NormalizedMemberName, member is null ? ImportActionKind.Create : ImportActionKind.Match);
                foreach (var plotRecord in plotRecords)
                {
                    stats.RecordPlot(plotRecord.Number, plotRecord.Entity is null ? ImportActionKind.Create : ImportActionKind.Match);
                }

                stats.MarkConflictSkippedForOwnership(candidate);
                stats.MarkConflictSkippedForMeterAndReadings(candidate, hasMeter: existingMeter is not null);
                issues.Add(CreateCandidateIssue(candidate, LegacyElectricityImportIssueSeverity.Warning, "OwnershipAndMeterImportSkippedBecauseOfConflict", "Forced commit will skip ownership, meter, and reading import for this row because ownership evidence is ambiguous."));
                executable.Add(new ExecutableCandidate(candidate, meterKey, billingPlot!, member, plotRecords, [], existingMeter?.Entity, [], configuration.OwnershipEffectiveFrom, ImportMeterAndReadings: false));
            }
            else
            {
                stats.RowsExcluded++;
                stats.MarkSkippedForCandidate(candidate, hasMeter: existingMeter is not null);
                if (forceRequested)
                {
                    stats.MarkConflictSkippedForCandidate(candidate, hasMeter: existingMeter is not null);
                    issues.Add(CreateCandidateIssue(candidate, LegacyElectricityImportIssueSeverity.Warning, "CandidateSkippedBecauseOfConflict", "Forced commit skipped this candidate because it contains blocking conflicts that are not safe to import."));
                }
            }
        }

        return Task.FromResult(executable);
    }

    private static void ValidateDuplicateImportMeterReadings(
        LegacyElectricityImportCandidate existingCandidate,
        LegacyElectricityImportCandidate currentCandidate,
        IReadOnlyList<ReadingPlan> currentReadingPlans,
        List<LegacyElectricityImportIssue> issues)
    {
        var existingReadingPlans = new List<ReadingPlan>();
        if (existingCandidate.PreviousReadingValue.HasValue)
        {
            existingReadingPlans.Add(new ReadingPlan(existingCandidate.PreviousReadingDate, existingCandidate.PreviousReadingValue.Value));
        }

        if (existingCandidate.CurrentReadingValue.HasValue)
        {
            existingReadingPlans.Add(new ReadingPlan(existingCandidate.CurrentReadingDate, existingCandidate.CurrentReadingValue.Value));
        }

        foreach (var currentPlan in currentReadingPlans)
        {
            var existingPlan = existingReadingPlans.FirstOrDefault(plan => plan.ReadingDate == currentPlan.ReadingDate);
            if (existingPlan is not null && existingPlan.Value != currentPlan.Value)
            {
                issues.Add(CreateConflictIssue(currentCandidate, "SameMeterDateDifferentReadingValues", $"Import rows for the same meter contain different values for {currentPlan.ReadingDate:yyyy-MM-dd}: {existingPlan.Value:0.###} and {currentPlan.Value:0.###}."));
            }
        }
    }

    private static IReadOnlyList<ReadingPlan> BuildReadingPlans(LegacyElectricityImportCandidate candidate, List<LegacyElectricityImportIssue> issues)
    {
        var rawPlans = new List<ReadingPlan>();
        if (candidate.PreviousReadingValue.HasValue)
        {
            rawPlans.Add(new ReadingPlan(candidate.PreviousReadingDate, candidate.PreviousReadingValue.Value));
        }

        if (candidate.CurrentReadingValue.HasValue)
        {
            rawPlans.Add(new ReadingPlan(candidate.CurrentReadingDate, candidate.CurrentReadingValue.Value));
        }

        var plans = new List<ReadingPlan>();
        foreach (var group in rawPlans.GroupBy(plan => plan.ReadingDate).OrderBy(group => group.Key))
        {
            if (group.Select(plan => plan.Value).Distinct().Count() > 1)
            {
                issues.Add(CreateConflictIssue(candidate, "DuplicateReadingDateConflict", $"The import generated different reading values for the same date {group.Key:yyyy-MM-dd}."));
                continue;
            }

            plans.Add(group.First());
        }

        return plans;
    }

    private static void ValidateReadingHistory(LegacyElectricityImportCandidate candidate, ExistingMeterSnapshot? existingMeter, IReadOnlyList<ReadingPlan> readingPlans, List<LegacyElectricityImportIssue> issues)
    {
        var history = new List<ReadingPlan>();
        if (existingMeter is not null)
        {
            history.AddRange(existingMeter.Readings.Select(reading => new ReadingPlan(reading.ReadingDate, reading.CurrentReading)));
        }

        history.AddRange(readingPlans);

        var normalizedHistory = new List<ReadingPlan>();
        foreach (var group in history.GroupBy(plan => plan.ReadingDate).OrderBy(group => group.Key))
        {
            if (group.Select(plan => plan.Value).Distinct().Count() > 1)
            {
                issues.Add(CreateConflictIssue(candidate, "DuplicateReadingDateConflict", $"Meter already has multiple different readings for {group.Key:yyyy-MM-dd}."));
                continue;
            }

            normalizedHistory.Add(group.First());
        }

        for (var index = 1; index < normalizedHistory.Count; index++)
        {
            if (normalizedHistory[index].Value < normalizedHistory[index - 1].Value)
            {
                issues.Add(CreateConflictIssue(candidate, "ReadingLowerThanExistingHistory", $"Reading {normalizedHistory[index].Value:0.###} on {normalizedHistory[index].ReadingDate:yyyy-MM-dd} is lower than the earlier reading {normalizedHistory[index - 1].Value:0.###} on {normalizedHistory[index - 1].ReadingDate:yyyy-MM-dd}."));
            }
        }
    }

    private async Task<bool> ApplyAsync(
        IReadOnlyList<ExecutableCandidate> candidates,
        LegacyDatabaseContext context,
        MutableImportStatistics stats,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction = null;
        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            }

            var membersByName = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
            var plotsByNumber = new Dictionary<string, Plot>(StringComparer.OrdinalIgnoreCase);
            var metersByKey = new Dictionary<string, MemberElectricityMeter>(StringComparer.OrdinalIgnoreCase);

            foreach (var executable in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var member = executable.ExistingMemberEntity
                    ?? membersByName.GetValueOrDefault(executable.Candidate.NormalizedMemberName)
                    ?? new Member
                    {
                        FullName = executable.Candidate.NormalizedMemberName,
                        IsActive = true,
                        Notes = "Imported from legacy electricity workbook.",
                        CreatedAtUtc = DateTime.UtcNow
                    };

                if (executable.ExistingMemberEntity is null && !membersByName.TryGetValue(executable.Candidate.NormalizedMemberName, out _))
                {
                    _dbContext.Members.Add(member);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                membersByName[executable.Candidate.NormalizedMemberName] = member;

                var plotEntities = new List<Plot>();
                foreach (var plotStub in executable.PlotStubs)
                {
                    var plot = plotStub.Entity
                        ?? plotsByNumber.GetValueOrDefault(plotStub.Number)
                        ?? new Plot
                    {
                        Number = plotStub.Number,
                        IsActive = true,
                        Notes = "Imported from legacy electricity workbook.",
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    if (plotStub.Entity is null && !plotsByNumber.ContainsKey(plotStub.Number))
                    {
                        _dbContext.Plots.Add(plot);
                    }

                    plotEntities.Add(plot);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                foreach (var plot in plotEntities)
                {
                    plotsByNumber[LegacyElectricityImportValidator.NormalizeWhitespace(plot.Number)] = plot;
                }

                foreach (var plot in plotEntities.Where(plot => executable.OwnershipPlans.Any(plan => string.Equals(plan.PlotNumber, plot.Number, StringComparison.OrdinalIgnoreCase))))
                {
                    var activeOwnership = await _dbContext.PlotOwnerships
                        .Where(ownership => ownership.PlotId == plot.Id && ownership.ValidTo == null)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (activeOwnership is null)
                    {
                        _dbContext.PlotOwnerships.Add(new PlotOwnership
                        {
                            PlotId = plot.Id,
                            MemberId = member.Id,
                            ValidFrom = executable.OwnershipEffectiveFrom,
                            IsPrimaryContact = !plotEntities.Any(existingPlot => existingPlot.Id != plot.Id)
                        });
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (!executable.ImportMeterAndReadings)
                {
                    continue;
                }

                var orderedPlots = plotEntities.OrderBy(plot => NaturalPlotSortKey(plot.Number)).ToList();
                var billingPlot = orderedPlots.First(plot => string.Equals(plot.Number, executable.BillingPlotNumber, StringComparison.OrdinalIgnoreCase));
                var meter = executable.ExistingMeterEntity
                    ?? metersByKey.GetValueOrDefault(executable.MeterKey)
                    ?? new MemberElectricityMeter
                {
                    MemberId = member.Id,
                    BillingPlotId = billingPlot.Id,
                    MeterNumber = executable.MeterKey,
                    Name = BuildMeterName(executable.Candidate),
                    IsActive = true,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                if (executable.ExistingMeterEntity is null && !metersByKey.ContainsKey(executable.MeterKey))
                {
                    _dbContext.MemberElectricityMeters.Add(meter);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                metersByKey[executable.MeterKey] = meter;

                foreach (var plot in plotEntities)
                {
                    if (plot.MemberElectricityMeterId is null)
                    {
                        plot.MemberElectricityMeterId = meter.Id;
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                var existingReadings = await _dbContext.MemberElectricityReadings
                    .Where(reading => reading.MemberElectricityMeterId == meter.Id)
                    .OrderBy(reading => reading.ReadingDate)
                    .ThenBy(reading => reading.Id)
                    .ToListAsync(cancellationToken);

                foreach (var readingPlan in executable.ReadingPlans)
                {
                    var existingReading = existingReadings.SingleOrDefault(reading => reading.ReadingDate == readingPlan.ReadingDate);
                    if (existingReading is not null)
                    {
                        stats.ReadingsMatched++;
                        continue;
                    }

                    var isInitial = existingReadings.Count == 0;
                    var reading = new MemberElectricityReading
                    {
                        MemberElectricityMeterId = meter.Id,
                        ReadingDate = readingPlan.ReadingDate,
                        CurrentReading = readingPlan.Value,
                        IsInitialReading = isInitial,
                        SubmittedByMember = false,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };

                    _dbContext.MemberElectricityReadings.Add(reading);
                    existingReadings.Add(reading);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await _executionHook.OnBeforeCommitAsync(_dbContext, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return true;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private LegacyElectricityImportResult BuildResult(
        LegacyElectricityImportConfigurationSnapshot configuration,
        LegacyElectricityWorkbookData workbook,
        LegacyValidatedWorkbook validatedWorkbook,
        IReadOnlyList<LegacyElectricityImportIssue> issues,
        MutableImportStatistics stats,
        bool commitRequested,
        bool forceRequested,
        bool committed,
        bool committedWithBlockingIssues,
        int blockingIssueCount,
        bool hasBlockingIssues)
    {
        var reportDirectory = ResolvePath(configuration.ReportsRelativePath);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var markdownPath = Path.Combine(reportDirectory, $"legacy-electricity-import-{timestamp}.md");
        var jsonPath = Path.Combine(reportDirectory, $"legacy-electricity-import-{timestamp}.json");
        var summary = committed
            ? $"Legacy electricity import completed. CommitRequested={commitRequested}. ForceRequested={forceRequested}. Committed={committed}. CommittedWithBlockingIssues={committedWithBlockingIssues}."
            : commitRequested && hasBlockingIssues
                ? $"Legacy electricity import was not committed because blocking issues were found. CommitRequested={commitRequested}. ForceRequested={forceRequested}. Committed={committed}. CommittedWithBlockingIssues={committedWithBlockingIssues}."
                : $"Legacy electricity import dry-run completed. CommitRequested={commitRequested}. ForceRequested={forceRequested}. Committed={committed}. CommittedWithBlockingIssues={committedWithBlockingIssues}.";

        return new LegacyElectricityImportResult(
            true,
            commitRequested,
            forceRequested,
            committed,
            committedWithBlockingIssues,
            blockingIssueCount,
            workbook.WorkbookPath,
            workbook.WorkbookHash,
            validatedWorkbook.SheetsInspected,
            configuration,
            stats.ToImmutable(),
            stats.ToConflictSkipCount(),
            validatedWorkbook.Diagnostics,
            issues,
            markdownPath,
            jsonPath,
            summary);
    }

    private async Task WriteReportsAsync(LegacyElectricityImportResult result, CancellationToken cancellationToken)
    {
        var markdown = BuildMarkdownReport(result);
        await File.WriteAllTextAsync(result.MarkdownReportPath, markdown, Encoding.UTF8, cancellationToken);

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(result.JsonReportPath, json, Encoding.UTF8, cancellationToken);

        _logger.LogInformation(
            "Legacy electricity import completed. Commit={Committed}. Issues={IssueCount}. MarkdownReport={MarkdownReportPath}. JsonReport={JsonReportPath}.",
            result.Committed,
            result.Issues.Count,
            result.MarkdownReportPath,
            result.JsonReportPath);
    }

    private static string BuildMarkdownReport(LegacyElectricityImportResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Legacy electricity import report");
        builder.AppendLine();
        builder.AppendLine($"- Mode: {(result.Committed ? "commit" : "dry-run")}");
        builder.AppendLine($"- Commit requested: {result.CommitRequested}");
        builder.AppendLine($"- Force requested: {result.ForceRequested}");
        builder.AppendLine($"- Workbook path: `{result.WorkbookPath}`");
        builder.AppendLine($"- Workbook hash: `{result.WorkbookHash}`");
        builder.AppendLine($"- Execution time (UTC): `{DateTimeOffset.UtcNow:O}`");
        builder.AppendLine($"- Blocking issue count: {result.BlockingIssueCount}");
        builder.AppendLine($"- Committed with blocking issues: {result.CommittedWithBlockingIssues}");
        builder.AppendLine($"- Default previous reading date: `{result.Configuration.DefaultPreviousReadingDate:yyyy-MM-dd}`");
        builder.AppendLine($"- Default current reading date: `{result.Configuration.DefaultCurrentReadingDate:yyyy-MM-dd}`");
        builder.AppendLine($"- Ownership effective from: `{result.Configuration.OwnershipEffectiveFrom:yyyy-MM-dd}`");
        builder.AppendLine();
        builder.AppendLine("## Sheets inspected");
        foreach (var sheet in result.SheetsInspected)
        {
            builder.AppendLine($"- {sheet}");
        }

        builder.AppendLine();
        builder.AppendLine("## Totals");
        builder.AppendLine($"- Source row count: {result.Statistics.SourceRowCount}");
        builder.AppendLine($"- Rows excluded: {result.Statistics.RowsExcluded}");
        builder.AppendLine($"- Members: created {result.Statistics.Members.Created}, matched {result.Statistics.Members.Matched}, skipped {result.Statistics.Members.Skipped}");
        builder.AppendLine($"- Plots: created {result.Statistics.Plots.Created}, matched {result.Statistics.Plots.Matched}, skipped {result.Statistics.Plots.Skipped}");
        builder.AppendLine($"- Ownerships: created {result.Statistics.Ownerships.Created}, matched {result.Statistics.Ownerships.Matched}, skipped {result.Statistics.Ownerships.Skipped}");
        builder.AppendLine($"- Meters: created {result.Statistics.Meters.Created}, matched {result.Statistics.Meters.Matched}, skipped {result.Statistics.Meters.Skipped}");
        builder.AppendLine($"- Readings: created {result.Statistics.Readings.Created}, matched {result.Statistics.Readings.Matched}, skipped {result.Statistics.Readings.Skipped}");
        builder.AppendLine($"- Skipped because of conflict: members {result.SkippedBecauseOfConflict.Members}, plots {result.SkippedBecauseOfConflict.Plots}, ownerships {result.SkippedBecauseOfConflict.Ownerships}, meters {result.SkippedBecauseOfConflict.Meters}, readings {result.SkippedBecauseOfConflict.Readings}");
        builder.AppendLine();
        builder.AppendLine("## Issues");
        foreach (var issue in result.Issues)
        {
            builder.AppendLine($"- [{issue.Severity}] {issue.Code} — {issue.SheetName} row {issue.RowNumber}: {issue.Message}");
            builder.AppendLine($"  - Original plot: `{issue.OriginalValues.GetValueOrDefault("Plot") ?? string.Empty}`");
            builder.AppendLine($"  - Original member: `{issue.OriginalValues.GetValueOrDefault("Member") ?? string.Empty}`");
            builder.AppendLine($"  - Original previous reading: `{issue.OriginalValues.GetValueOrDefault("PreviousReading") ?? string.Empty}`");
            builder.AppendLine($"  - Original current reading: `{issue.OriginalValues.GetValueOrDefault("CurrentReading") ?? string.Empty}`");
            builder.AppendLine($"  - Normalized plots: `{issue.NormalizedValues.GetValueOrDefault("PlotNumbers") ?? string.Empty}`");
            builder.AppendLine($"  - Normalized member: `{issue.NormalizedValues.GetValueOrDefault("MemberName") ?? string.Empty}`");
            builder.AppendLine($"  - Conflict rows: `{issue.NormalizedValues.GetValueOrDefault("ConflictRows") ?? string.Empty}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Transaction result");
        builder.AppendLine($"- Commit requested: {result.CommitRequested}");
        builder.AppendLine($"- Force requested: {result.ForceRequested}");
        builder.AppendLine($"- Committed: {result.Committed}");
        builder.AppendLine($"- Committed with blocking issues: {result.CommittedWithBlockingIssues}");
        return builder.ToString();
    }

    internal static string BuildLegacyMeterKey(string memberName, IReadOnlyList<string> plotNumbers, string? meterLabel, bool hasCombinedTariffSource)
    {
        var input = string.Join("|", memberName, string.Join(",", plotNumbers), meterLabel ?? string.Empty, hasCombinedTariffSource ? "T1T2" : "Single");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))[..20];
        return $"LEGACY-{hash}";
    }

    internal static string BuildMeterName(LegacyElectricityImportCandidate candidate)
    {
        var label = string.IsNullOrWhiteSpace(candidate.MeterLabel) ? "Legacy meter" : $"Legacy {candidate.MeterLabel}";
        return candidate.HasCombinedTariffSource ? $"{label} (T1+T2 combined)" : label;
    }

    internal static string NaturalPlotSortKey(string plotNumber)
    {
        return int.TryParse(plotNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
            ? numeric.ToString("D10", CultureInfo.InvariantCulture)
            : plotNumber;
    }

    private static LegacyElectricityImportIssue CreateConflictIssue(LegacyElectricityImportCandidate candidate, string code, string message)
    {
        return CreateCandidateIssue(candidate, LegacyElectricityImportIssueSeverity.Error, code, message);
    }

    private static LegacyElectricityImportIssue CreateCandidateIssue(LegacyElectricityImportCandidate candidate, LegacyElectricityImportIssueSeverity severity, string code, string message)
    {
        return new LegacyElectricityImportIssue(
            severity,
            code,
            candidate.SheetName,
            candidate.RowNumber,
            new Dictionary<string, string?>
            {
                ["Plot"] = candidate.OriginalPlotValue,
                ["Member"] = candidate.OriginalMemberValue,
                ["PreviousReading"] = null,
                ["CurrentReading"] = null
            },
            new Dictionary<string, string?>
            {
                ["PlotNumbers"] = string.Join(", ", candidate.PlotNumbers),
                ["MemberName"] = candidate.NormalizedMemberName,
                ["ConflictRows"] = string.Join(", ", candidate.SourceRows.Select(row => $"{candidate.SheetName}:{row}"))
            },
            message);
    }

    private static bool HasBlockingIssues(LegacyElectricityImportCandidate candidate, IEnumerable<LegacyElectricityImportIssue> issues)
    {
        return GetBlockingIssues(candidate, issues).Count > 0;
    }

    private static IReadOnlyList<LegacyElectricityImportIssue> GetBlockingIssues(LegacyElectricityImportCandidate candidate, IEnumerable<LegacyElectricityImportIssue> issues)
    {
        var sourceRows = candidate.SourceRows.ToHashSet();
        return issues
            .Where(issue => issue.Severity >= LegacyElectricityImportIssueSeverity.Error
                && string.Equals(issue.SheetName, candidate.SheetName, StringComparison.OrdinalIgnoreCase)
                && sourceRows.Contains(issue.RowNumber))
            .ToList();
    }

    private static bool IsOwnershipConflictIssue(LegacyElectricityImportIssue issue)
    {
        return issue.Code is "ConflictingPlotOwnerInWorkbook"
            or "SupplementalMemberPlotDisagreement"
            or "ExistingPlotOwnerConflict";
    }

    private sealed class MutableImportStatistics
    {
        private readonly HashSet<string> _memberActions = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _plotActions = new(StringComparer.OrdinalIgnoreCase);

        public MutableImportStatistics(int sourceRowCount)
        {
            SourceRowCount = sourceRowCount;
        }

        public int SourceRowCount { get; }
        public int RowsExcluded { get; set; }
        public int MembersCreated { get; set; }
        public int MembersMatched { get; set; }
        public int PlotsCreated { get; set; }
        public int PlotsMatched { get; set; }
        public int OwnershipsCreated { get; set; }
        public int OwnershipsMatched { get; set; }
        public int MetersCreated { get; set; }
        public int MetersMatched { get; set; }
        public int ReadingsCreated { get; set; }
        public int ReadingsMatched { get; set; }
        public int MembersSkipped { get; set; }
        public int PlotsSkipped { get; set; }
        public int OwnershipsSkipped { get; set; }
        public int MetersSkipped { get; set; }
        public int ReadingsSkipped { get; set; }
        public int MembersSkippedBecauseOfConflict { get; set; }
        public int PlotsSkippedBecauseOfConflict { get; set; }
        public int OwnershipsSkippedBecauseOfConflict { get; set; }
        public int MetersSkippedBecauseOfConflict { get; set; }
        public int ReadingsSkippedBecauseOfConflict { get; set; }

        public void RecordMember(string memberName, ImportActionKind action)
        {
            var key = $"{memberName}:{action}";
            if (_memberActions.Add(key))
            {
                if (action == ImportActionKind.Create)
                {
                    MembersCreated++;
                }
                else
                {
                    MembersMatched++;
                }
            }
        }

        public void RecordPlot(string plotNumber, ImportActionKind action)
        {
            var key = $"{plotNumber}:{action}";
            if (_plotActions.Add(key))
            {
                if (action == ImportActionKind.Create)
                {
                    PlotsCreated++;
                }
                else
                {
                    PlotsMatched++;
                }
            }
        }

        public void RecordOwnership(ImportActionKind action)
        {
            if (action == ImportActionKind.Create)
            {
                OwnershipsCreated++;
            }
            else
            {
                OwnershipsMatched++;
            }
        }

        public void RecordMeter(ImportActionKind action)
        {
            if (action == ImportActionKind.Create)
            {
                MetersCreated++;
            }
            else
            {
                MetersMatched++;
            }
        }

        public void RecordReading(ImportActionKind action)
        {
            if (action == ImportActionKind.Create)
            {
                ReadingsCreated++;
            }
            else
            {
                ReadingsMatched++;
            }
        }

        public void MarkSkippedForCandidate(LegacyElectricityImportCandidate candidate, bool hasMeter)
        {
            MembersSkipped++;
            PlotsSkipped += candidate.PlotNumbers.Count;
            OwnershipsSkipped += candidate.CreatesOwnerships ? candidate.PlotNumbers.Count : 0;
            MetersSkipped += hasMeter ? 0 : 1;
            ReadingsSkipped += (candidate.PreviousReadingValue.HasValue ? 1 : 0) + (candidate.CurrentReadingValue.HasValue ? 1 : 0);
        }

        public void MarkConflictSkippedForCandidate(LegacyElectricityImportCandidate candidate, bool hasMeter)
        {
            MembersSkippedBecauseOfConflict++;
            PlotsSkippedBecauseOfConflict += candidate.PlotNumbers.Count;
            OwnershipsSkippedBecauseOfConflict += candidate.CreatesOwnerships ? candidate.PlotNumbers.Count : 0;
            MetersSkippedBecauseOfConflict += hasMeter ? 0 : 1;
            ReadingsSkippedBecauseOfConflict += (candidate.PreviousReadingValue.HasValue ? 1 : 0) + (candidate.CurrentReadingValue.HasValue ? 1 : 0);
        }

        public void MarkConflictSkippedForOwnership(LegacyElectricityImportCandidate candidate)
        {
            OwnershipsSkippedBecauseOfConflict += candidate.CreatesOwnerships ? candidate.PlotNumbers.Count : 0;
        }

        public void MarkConflictSkippedForMeterAndReadings(LegacyElectricityImportCandidate candidate, bool hasMeter)
        {
            MetersSkippedBecauseOfConflict += hasMeter ? 0 : 1;
            ReadingsSkippedBecauseOfConflict += (candidate.PreviousReadingValue.HasValue ? 1 : 0) + (candidate.CurrentReadingValue.HasValue ? 1 : 0);
        }

        public LegacyElectricityImportStatistics ToImmutable()
        {
            return new LegacyElectricityImportStatistics(
                SourceRowCount,
                RowsExcluded,
                new LegacyElectricityEntityImportCount(MembersCreated, MembersMatched, MembersSkipped),
                new LegacyElectricityEntityImportCount(PlotsCreated, PlotsMatched, PlotsSkipped),
                new LegacyElectricityEntityImportCount(OwnershipsCreated, OwnershipsMatched, OwnershipsSkipped),
                new LegacyElectricityEntityImportCount(MetersCreated, MetersMatched, MetersSkipped),
                new LegacyElectricityEntityImportCount(ReadingsCreated, ReadingsMatched, ReadingsSkipped));
        }

        public LegacyElectricityConflictSkipCount ToConflictSkipCount()
        {
            return new LegacyElectricityConflictSkipCount(
                MembersSkippedBecauseOfConflict,
                PlotsSkippedBecauseOfConflict,
                OwnershipsSkippedBecauseOfConflict,
                MetersSkippedBecauseOfConflict,
                ReadingsSkippedBecauseOfConflict);
        }
    }

    private sealed record ExecutableCandidate(
        LegacyElectricityImportCandidate Candidate,
        string MeterKey,
        string BillingPlotNumber,
        Member? ExistingMemberEntity,
        IReadOnlyList<PlotStub> PlotStubs,
        IReadOnlyList<OwnershipPlan> OwnershipPlans,
        MemberElectricityMeter? ExistingMeterEntity,
        IReadOnlyList<ReadingPlan> ReadingPlans,
        DateOnly OwnershipEffectiveFrom,
        bool ImportMeterAndReadings = true);

    private sealed record OwnershipPlan(string PlotNumber, ImportActionKind Action);
    private sealed record ReadingPlan(DateOnly ReadingDate, decimal Value);

    private sealed class LegacyDatabaseContext
    {
        private readonly Dictionary<string, Member> _membersByName;
        private readonly Dictionary<string, PlotStub> _plotsByNumber;
        private readonly List<ExistingMeterSnapshot> _meters;

        public LegacyDatabaseContext(IReadOnlyList<Member> members, IReadOnlyList<Plot> plots, IReadOnlyList<MemberElectricityMeter> meters)
        {
            var memberNamesById = members.ToDictionary(member => member.Id, member => LegacyElectricityImportValidator.NormalizeWhitespace(member.FullName));
            _membersByName = members
                .GroupBy(member => LegacyElectricityImportValidator.NormalizeWhitespace(member.FullName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            _plotsByNumber = plots
                .ToDictionary(
                    plot => LegacyElectricityImportValidator.NormalizeWhitespace(plot.Number),
                    plot => new PlotStub(
                        LegacyElectricityImportValidator.NormalizeWhitespace(plot.Number),
                        plot,
                        plot.PlotOwnerships.FirstOrDefault(ownership => ownership.ValidTo == null) is { } activeOwnership && memberNamesById.TryGetValue(activeOwnership.MemberId, out var activeOwnerName)
                            ? activeOwnerName
                            : null,
                        plot.MemberElectricityMeter?.MeterNumber),
                    StringComparer.OrdinalIgnoreCase);
            _meters = meters.Select(meter => new ExistingMeterSnapshot(
                meter,
                meter.MeterNumber,
                members.FirstOrDefault(member => member.Id == meter.MemberId)?.FullName is { } memberName
                    ? LegacyElectricityImportValidator.NormalizeWhitespace(memberName)
                    : string.Empty,
                meter.Plots.Select(plot => LegacyElectricityImportValidator.NormalizeWhitespace(plot.Number)).OrderBy(LegacyElectricityDataImportService.NaturalPlotSortKey).ToList(),
                meter.Readings.Select(reading => new ExistingReadingSnapshot(reading.Id, reading.ReadingDate, reading.CurrentReading)).ToList())).ToList();
        }

        public Member? FindMember(string memberName) => _membersByName.GetValueOrDefault(memberName);

        public PlotStub? FindPlot(string plotNumber) => _plotsByNumber.GetValueOrDefault(plotNumber);

        public PlotStub FindOrCreatePlotStub(string plotNumber)
        {
            if (!_plotsByNumber.TryGetValue(plotNumber, out var stub))
            {
                stub = new PlotStub(plotNumber, null, null, null);
                _plotsByNumber[plotNumber] = stub;
            }

            return stub;
        }

        public ExistingMeterSnapshot? FindMeter(string meterKey, string memberName, IReadOnlyList<string> plotNumbers)
        {
            return _meters.FirstOrDefault(meter => string.Equals(meter.MeterKey, meterKey, StringComparison.OrdinalIgnoreCase))
                ?? _meters.FirstOrDefault(meter => string.Equals(meter.MemberName, memberName, StringComparison.OrdinalIgnoreCase)
                    && meter.PlotNumbers.SequenceEqual(plotNumbers, StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed record PlotStub(string Number, Plot? Entity, string? ActiveOwnerName, string? AssignedMeterKey);
    private sealed record ExistingMeterSnapshot(MemberElectricityMeter Entity, string? MeterKey, string MemberName, IReadOnlyList<string> PlotNumbers, IReadOnlyList<ExistingReadingSnapshot> Readings);
    private sealed record ExistingReadingSnapshot(long Id, DateOnly ReadingDate, decimal CurrentReading);
    private enum ImportActionKind { Create, Match }
}
