using System.Globalization;
using System.Text.RegularExpressions;
using Neftyanik.Portal.Application.LegacyImport;

namespace Neftyanik.Portal.Infrastructure.LegacyImport;

internal sealed partial class LegacyElectricityImportValidator
{
    private static readonly string[] DisconnectedMarkers = ["отключена", "відключена", "немає даних", "нет данных", "no data", "нет показаний", "нема даних"];
    private static readonly string[] MeterLabelKeywords = ["дом", "дом1", "дом2", "будинок", "будин", "дім", "скважина", "скваж", "свердловина", "well", "house"];
    private static readonly Regex MeterLabelPattern = new(@"^(?<member>.+?)\s+(?<label>(дом(?:[12])?|будинок|будин\S*|дім\S*|скважина|скваж\S*|свердловина|well|house).*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EmbeddedDatePattern = new(@"(?<day>\d{1,2})\.(?<month>\d{1,2})\.(?<year>\d{2,4})", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public LegacyValidatedWorkbook Validate(LegacyElectricityWorkbookData workbook, LegacyElectricityImportConfigurationSnapshot configuration)
    {
        var issues = new List<LegacyElectricityImportIssue>();
        var candidates = new List<LegacyElectricityImportCandidate>();
        var supplementalRows = new List<LegacySupplementalOwnerRow>();
        var memberDiagnostics = new List<LegacyElectricityMemberDiagnostic>();
        var plotDiagnostics = new List<LegacyElectricityPlotDiagnostic>();
        var sheetStatistics = new List<LegacyElectricitySheetDiagnostics>();

        var primarySheet = workbook.Sheets.FirstOrDefault(IsPrimarySheet);
        if (primarySheet is null)
        {
            issues.Add(new LegacyElectricityImportIssue(
                LegacyElectricityImportIssueSeverity.Critical,
                "PrimarySheetNotFound",
                string.Empty,
                0,
                new Dictionary<string, string?>(),
                new Dictionary<string, string?>(),
                "The workbook does not contain a primary electricity sheet with plot, member and reading columns."));

            return new LegacyValidatedWorkbook([], [], issues, workbook.Sheets.Select(sheet => sheet.Name).ToList(), [], LegacyElectricityImportDiagnostics.Empty, []);
        }

        ParsePrimarySheet(primarySheet, configuration, candidates, issues, memberDiagnostics, plotDiagnostics, sheetStatistics);

        foreach (var sheet in workbook.Sheets.Where(sheet => !ReferenceEquals(sheet, primarySheet)))
        {
            ParseSupplementalSheet(sheet, supplementalRows, issues, memberDiagnostics, plotDiagnostics, sheetStatistics);
        }

        DetectWorkbookOwnershipConflicts(candidates, supplementalRows, issues);
        DetectSupplementalMemberPlotDisagreements(candidates, supplementalRows, issues);

        var primaryRows = candidates
            .Where(candidate => string.Equals(candidate.SheetName, primarySheet.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var uniqueMembers = primaryRows
            .Select(candidate => candidate.NormalizedMemberName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var uniquePlots = primaryRows
            .SelectMany(candidate => candidate.PlotNumbers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var memberToPlotRelationships = primaryRows
            .SelectMany(candidate => candidate.PlotNumbers.Select(plot => $"{candidate.NormalizedMemberName}|{plot}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var ownershipCandidates = primaryRows
            .Where(candidate => candidate.CreatesOwnerships)
            .SelectMany(candidate => candidate.PlotNumbers.Select(plot => new LegacyOwnershipCandidate(candidate.SheetName, candidate.RowNumber, plot, candidate.NormalizedMemberName)))
            .ToList();
        var deduplicatedOwnerships = ownershipCandidates
            .Select(candidate => $"{candidate.MemberName}|{candidate.PlotNumber}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var physicalSourceRowsInspected = sheetStatistics
            .Where(sheet => string.Equals(sheet.SheetName, primarySheet.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(sheet => sheet.PhysicalRowsInspected);
        var eligibleEntityImportRows = sheetStatistics
            .Where(sheet => string.Equals(sheet.SheetName, primarySheet.Name, StringComparison.OrdinalIgnoreCase))
            .Sum(sheet => sheet.EligibleEntityImportRows);
        var diagnostics = new LegacyElectricityImportDiagnostics(
            physicalSourceRowsInspected,
            eligibleEntityImportRows,
            uniqueMembers,
            uniquePlots,
            memberToPlotRelationships,
            ownershipCandidates.Count,
            deduplicatedOwnerships,
            ownershipCandidates.Count - deduplicatedOwnerships,
            sheetStatistics,
            memberDiagnostics,
            plotDiagnostics);

        return new LegacyValidatedWorkbook(
            candidates,
            supplementalRows,
            issues.OrderByDescending(issue => issue.Severity).ThenBy(issue => issue.SheetName, StringComparer.Ordinal).ThenBy(issue => issue.RowNumber).ToList(),
            workbook.Sheets.Select(sheet => sheet.Name).ToList(),
            BuildSheetUsage(primarySheet, workbook.Sheets.Where(sheet => !ReferenceEquals(sheet, primarySheet)).ToList()),
            diagnostics,
            ownershipCandidates);
    }

    private static bool IsPrimarySheet(LegacyElectricityWorksheetData sheet)
    {
        var headerRow = sheet.Rows.FirstOrDefault();
        if (headerRow is null)
        {
            return false;
        }

        var columnA = headerRow.GetValue("A") ?? string.Empty;
        var columnB = headerRow.GetValue("B") ?? string.Empty;
        var columnC = headerRow.GetValue("C") ?? string.Empty;
        var columnD = headerRow.GetValue("D") ?? string.Empty;

        return columnA.Contains("участ", StringComparison.OrdinalIgnoreCase)
            && columnB.Contains("фам", StringComparison.OrdinalIgnoreCase)
            && columnC.Contains("июн", StringComparison.OrdinalIgnoreCase)
            && columnD.Contains("июл", StringComparison.OrdinalIgnoreCase);
    }

    private void ParsePrimarySheet(
        LegacyElectricityWorksheetData sheet,
        LegacyElectricityImportConfigurationSnapshot configuration,
        List<LegacyElectricityImportCandidate> candidates,
        List<LegacyElectricityImportIssue> issues,
        List<LegacyElectricityMemberDiagnostic> memberDiagnostics,
        List<LegacyElectricityPlotDiagnostic> plotDiagnostics,
        List<LegacyElectricitySheetDiagnostics> sheetStatistics)
    {
        var rowLookup = sheet.Rows.ToDictionary(row => row.RowNumber);
        var physicalRowsInspected = Math.Max(sheet.LastRowNumber - 1, 0);
        var blankRows = 0;
        var primaryRows = 0;
        var continuationMeterRows = 0;
        var continuationTariffRows = 0;
        var eligibleEntityImportRows = 0;
        LegacyElectricityImportCandidate? previousCandidate = null;

        for (var rowNumber = 2; rowNumber <= sheet.LastRowNumber; rowNumber++)
        {
            if (!rowLookup.TryGetValue(rowNumber, out var row))
            {
                blankRows++;
                continue;
            }

            var plotValue = row.GetValue("A");
            var memberValue = row.GetValue("B");
            var previousCellValue = row.GetValue("C");
            var currentCellValue = row.GetValue("D");
            var manualDifferenceValue = row.GetValue("E");
            var paymentValue = row.GetValue("F");
            var noteValue = row.GetValue("G");

            if (IsBlank(plotValue) && IsBlank(memberValue) && IsBlank(previousCellValue) && IsBlank(currentCellValue))
            {
                blankRows++;
                continue;
            }

            if (IsBlank(plotValue) && IsBlank(memberValue))
            {
                if (previousCandidate is not null && LooksLikeTariffComponentContinuation(previousCellValue, currentCellValue))
                {
                    continuationTariffRows++;
                    AttachTariffContinuation(previousCandidate, sheet.Name, row, configuration, issues);
                }
                else
                {
                    issues.Add(CreateIssue(
                        LegacyElectricityImportIssueSeverity.Error,
                        LooksLikeTariffComponentContinuation(previousCellValue, currentCellValue)
                            ? "T2ContinuationWithoutParentContext"
                            : "UnresolvedContinuationRow",
                        sheet.Name,
                        row.RowNumber,
                        plotValue,
                        memberValue,
                        previousCellValue,
                        currentCellValue,
                        null,
                        null,
                        "The row does not contain a plot or member and could not be matched as a continuation row."));
                }

                continue;
            }

            if (IsBlank(plotValue) && !IsBlank(memberValue))
            {
                continuationMeterRows++;
                if (previousCandidate is null)
                {
                    issues.Add(CreateIssue(
                        LegacyElectricityImportIssueSeverity.Error,
                        "ContinuationWithoutAnchor",
                        sheet.Name,
                        row.RowNumber,
                        plotValue,
                        memberValue,
                        previousCellValue,
                        currentCellValue,
                        null,
                        null,
                        "The row looks like an additional meter row but there is no previous row to inherit plot numbers from."));
                    continue;
                }

                eligibleEntityImportRows++;
                var continuationMember = ParseMemberIdentity(memberValue!);
                if (!SharesBaseMember(previousCandidate.NormalizedMemberName, continuationMember.NormalizedName))
                {
                    issues.Add(CreateIssue(
                        LegacyElectricityImportIssueSeverity.Error,
                        "ContinuationMemberMismatch",
                        sheet.Name,
                        row.RowNumber,
                        plotValue,
                        memberValue,
                        previousCellValue,
                        currentCellValue,
                        string.Join(", ", previousCandidate.PlotNumbers),
                        continuationMember.NormalizedName,
                        "The continuation row member name does not match the base member from the previous row."));
                    continue;
                }

                var continuationCandidate = CreateCandidate(
                    sheet.Name,
                    row,
                    previousCandidate.PlotNumbers,
                    plotValue,
                    memberValue,
                    continuationMember,
                    previousCellValue,
                    currentCellValue,
                    configuration,
                    issues,
                    manualDifferenceValue,
                    paymentValue,
                    noteValue,
                    createsOwnerships: false);

                candidates.Add(continuationCandidate);
                memberDiagnostics.Add(new LegacyElectricityMemberDiagnostic(sheet.Name, row.RowNumber, memberValue, continuationCandidate.NormalizedMemberName, continuationCandidate.MeterLabel));
                plotDiagnostics.Add(new LegacyElectricityPlotDiagnostic(sheet.Name, row.RowNumber, plotValue, continuationCandidate.PlotNumbers, continuationCandidate.NormalizedMemberName));
                previousCandidate = continuationCandidate;
                continue;
            }

            primaryRows++;
            eligibleEntityImportRows++;
            var parsedPlots = ParsePlotNumbers(plotValue);
            var memberIdentity = ParseMemberIdentity(memberValue);

            var candidate = CreateCandidate(
                sheet.Name,
                row,
                parsedPlots,
                plotValue,
                memberValue,
                memberIdentity,
                previousCellValue,
                currentCellValue,
                configuration,
                issues,
                manualDifferenceValue,
                paymentValue,
                noteValue,
                createsOwnerships: true);

            candidates.Add(candidate);
            memberDiagnostics.Add(new LegacyElectricityMemberDiagnostic(sheet.Name, row.RowNumber, memberValue, candidate.NormalizedMemberName, candidate.MeterLabel));
            plotDiagnostics.Add(new LegacyElectricityPlotDiagnostic(sheet.Name, row.RowNumber, plotValue, candidate.PlotNumbers, candidate.NormalizedMemberName));
            previousCandidate = candidate;
        }

        sheetStatistics.Add(new LegacyElectricitySheetDiagnostics(
            sheet.Name,
            physicalRowsInspected,
            sheet.LastRowNumber > 0 ? 1 : 0,
            blankRows,
            primaryRows,
            continuationMeterRows,
            continuationTariffRows,
            0,
            eligibleEntityImportRows));
    }

    private void AttachTariffContinuation(
        LegacyElectricityImportCandidate previousCandidate,
        string sheetName,
        LegacyElectricityWorksheetRow row,
        LegacyElectricityImportConfigurationSnapshot configuration,
        List<LegacyElectricityImportIssue> issues)
    {
        var previousPart = ParseReadingCell(row.GetValue("C"), configuration.DefaultPreviousReadingDate);
        var currentPart = ParseReadingCell(row.GetValue("D"), configuration.DefaultCurrentReadingDate);

        previousCandidate.SourceRows.Add(row.RowNumber);

        if (!previousCandidate.HasExplicitT1Marker)
        {
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Error,
                "T2ContinuationWithoutT1Context",
                sheetName,
                row.RowNumber,
                row.GetValue("A"),
                row.GetValue("B"),
                row.GetValue("C"),
                row.GetValue("D"),
                string.Join(", ", previousCandidate.PlotNumbers),
                previousCandidate.NormalizedMemberName,
                "The T2 continuation row does not have a preceding T1 context for the same meter."));
            return;
        }

        if (previousPart.Component != LegacyTariffComponent.T2 && currentPart.Component != LegacyTariffComponent.T2)
        {
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Error,
                "ContinuationNotT2",
                sheetName,
                row.RowNumber,
                row.GetValue("A"),
                row.GetValue("B"),
                row.GetValue("C"),
                row.GetValue("D"),
                string.Join(", ", previousCandidate.PlotNumbers),
                previousCandidate.NormalizedMemberName,
                "The continuation row was expected to contain T2 values but did not."));
            return;
        }

        if (previousPart.NumericValue.HasValue)
        {
            previousCandidate.AddPreviousTariffComponent(previousPart.NumericValue.Value, LegacyTariffComponent.T2, previousPart.ExplicitDate ?? configuration.DefaultPreviousReadingDate);
        }

        if (currentPart.NumericValue.HasValue)
        {
            previousCandidate.AddCurrentTariffComponent(currentPart.NumericValue.Value, LegacyTariffComponent.T2, currentPart.ExplicitDate ?? configuration.DefaultCurrentReadingDate);
        }

        previousCandidate.HasCombinedTariffSource = true;

        issues.Add(CreateIssue(
            LegacyElectricityImportIssueSeverity.Information,
            "CombinedTariffReading",
            sheetName,
            row.RowNumber,
            row.GetValue("A"),
            row.GetValue("B"),
            row.GetValue("C"),
            row.GetValue("D"),
            string.Join(", ", previousCandidate.PlotNumbers),
            previousCandidate.NormalizedMemberName,
            "The importer combined T1 and T2 legacy readings into a single cumulative reading."));
    }

    private LegacyElectricityImportCandidate CreateCandidate(
        string sheetName,
        LegacyElectricityWorksheetRow row,
        IReadOnlyList<string> plotNumbers,
        string? originalPlotValue,
        string? originalMemberValue,
        LegacyMemberIdentity memberIdentity,
        string? previousCellValue,
        string? currentCellValue,
        LegacyElectricityImportConfigurationSnapshot configuration,
        List<LegacyElectricityImportIssue> issues,
        string? manualDifferenceValue,
        string? paymentValue,
        string? noteValue,
        bool createsOwnerships)
    {
        var candidate = new LegacyElectricityImportCandidate(
            sheetName,
            row.RowNumber,
            [row.RowNumber],
            originalPlotValue,
            originalMemberValue,
            plotNumbers.ToList(),
            memberIdentity.NormalizedName,
            memberIdentity.MeterLabel,
            manualDifferenceValue,
            paymentValue,
            noteValue,
            createsOwnerships);

        if (plotNumbers.Count == 0)
        {
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Error,
                "PlotNumberMissing",
                sheetName,
                row.RowNumber,
                originalPlotValue,
                originalMemberValue,
                previousCellValue,
                currentCellValue,
                null,
                memberIdentity.NormalizedName,
                "The row does not contain any valid plot numbers."));
        }
        else
        {
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Information,
                "PlotNumberParsed",
                sheetName,
                row.RowNumber,
                originalPlotValue,
                originalMemberValue,
                previousCellValue,
                currentCellValue,
                string.Join(", ", plotNumbers),
                memberIdentity.NormalizedName,
                "Plot numbers were parsed from the source row."));
        }

        if (string.IsNullOrWhiteSpace(memberIdentity.NormalizedName))
        {
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Error,
                "MemberNameMissing",
                sheetName,
                row.RowNumber,
                originalPlotValue,
                originalMemberValue,
                previousCellValue,
                currentCellValue,
                string.Join(", ", plotNumbers),
                memberIdentity.NormalizedName,
                "The row does not contain a valid member name."));
        }

        var previousReading = ParseReadingCell(previousCellValue, configuration.DefaultPreviousReadingDate);
        var currentReading = ParseReadingCell(currentCellValue, configuration.DefaultCurrentReadingDate);

        AddReadingCellIssues(sheetName, row.RowNumber, originalPlotValue, originalMemberValue, string.Join(", ", plotNumbers), memberIdentity.NormalizedName, previousCellValue, currentCellValue, previousReading, true, issues);
        AddReadingCellIssues(sheetName, row.RowNumber, originalPlotValue, originalMemberValue, string.Join(", ", plotNumbers), memberIdentity.NormalizedName, previousCellValue, currentCellValue, currentReading, false, issues);

        if (previousReading.NumericValue.HasValue)
        {
            candidate.AddPreviousTariffComponent(previousReading.NumericValue.Value, previousReading.Component, previousReading.ExplicitDate ?? configuration.DefaultPreviousReadingDate);
        }

        if (currentReading.NumericValue.HasValue)
        {
            candidate.AddCurrentTariffComponent(currentReading.NumericValue.Value, currentReading.Component, currentReading.ExplicitDate ?? configuration.DefaultCurrentReadingDate);
        }

        candidate.HasExplicitT1Marker = previousReading.Component == LegacyTariffComponent.T1
            || currentReading.Component == LegacyTariffComponent.T1;

        candidate.HasCombinedTariffSource = previousReading.Component != LegacyTariffComponent.None
            || currentReading.Component != LegacyTariffComponent.None;

        if (candidate.PreviousReadingValue.HasValue && candidate.CurrentReadingValue.HasValue)
        {
            if (candidate.CurrentReadingDate < candidate.PreviousReadingDate)
            {
                issues.Add(CreateIssue(
                    LegacyElectricityImportIssueSeverity.Error,
                    "ReadingDateOrderInvalid",
                    sheetName,
                    row.RowNumber,
                    originalPlotValue,
                    originalMemberValue,
                    previousCellValue,
                    currentCellValue,
                    string.Join(", ", plotNumbers),
                    memberIdentity.NormalizedName,
                    "The current reading date is earlier than the previous reading date."));
            }

            if (candidate.CurrentReadingValue.Value < candidate.PreviousReadingValue.Value)
            {
                issues.Add(CreateIssue(
                    LegacyElectricityImportIssueSeverity.Error,
                    "ReadingDecreaseDetected",
                    sheetName,
                    row.RowNumber,
                    originalPlotValue,
                    originalMemberValue,
                    previousCellValue,
                    currentCellValue,
                    string.Join(", ", plotNumbers),
                    memberIdentity.NormalizedName,
                    "The current cumulative reading is lower than the previous cumulative reading."));
            }
        }

        return candidate;
    }

    private static void AddReadingCellIssues(
        string sheetName,
        int rowNumber,
        string? originalPlotValue,
        string? originalMemberValue,
        string? normalizedPlots,
        string? normalizedMember,
        string? previousCellValue,
        string? currentCellValue,
        LegacyParsedReadingCell reading,
        bool isPrevious,
        List<LegacyElectricityImportIssue> issues)
    {
        if (reading.Severity is null)
        {
            return;
        }

        issues.Add(CreateIssue(
            reading.Severity.Value,
            reading.Code ?? (isPrevious ? "PreviousReadingIssue" : "CurrentReadingIssue"),
            sheetName,
            rowNumber,
            originalPlotValue,
            originalMemberValue,
            previousCellValue,
            currentCellValue,
            normalizedPlots,
            normalizedMember,
            reading.Message ?? string.Empty));
    }

    private void ParseSupplementalSheet(
        LegacyElectricityWorksheetData sheet,
        List<LegacySupplementalOwnerRow> supplementalRows,
        List<LegacyElectricityImportIssue> issues,
        List<LegacyElectricityMemberDiagnostic> memberDiagnostics,
        List<LegacyElectricityPlotDiagnostic> plotDiagnostics,
        List<LegacyElectricitySheetDiagnostics> sheetStatistics)
    {
        var rowLookup = sheet.Rows.ToDictionary(row => row.RowNumber);
        var blankRows = 0;
        var ignoredSupplementalRows = 0;

        for (var rowNumber = 1; rowNumber <= sheet.LastRowNumber; rowNumber++)
        {
            if (!rowLookup.TryGetValue(rowNumber, out var row))
            {
                blankRows++;
                continue;
            }

            var plotValue = row.GetValue("A");
            var memberValue = row.GetValue("B");
            if (IsBlank(plotValue) && IsBlank(memberValue) && IsBlank(row.GetValue("C")) && IsBlank(row.GetValue("D")))
            {
                blankRows++;
                continue;
            }

            ignoredSupplementalRows++;
            if (IsBlank(plotValue) || IsBlank(memberValue))
            {
                continue;
            }

            var plots = ParsePlotNumbers(plotValue);
            var memberIdentity = ParseMemberIdentity(memberValue);
            if (plots.Count == 0 || string.IsNullOrWhiteSpace(memberIdentity.NormalizedName))
            {
                continue;
            }

            supplementalRows.Add(new LegacySupplementalOwnerRow(sheet.Name, row.RowNumber, plots, memberIdentity.NormalizedName, plotValue, memberValue));
            memberDiagnostics.Add(new LegacyElectricityMemberDiagnostic(sheet.Name, row.RowNumber, memberValue, memberIdentity.NormalizedName, memberIdentity.MeterLabel));
            plotDiagnostics.Add(new LegacyElectricityPlotDiagnostic(sheet.Name, row.RowNumber, plotValue, plots, memberIdentity.NormalizedName));
            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Information,
                "SupplementalOwnershipReference",
                sheet.Name,
                row.RowNumber,
                plotValue,
                memberValue,
                row.GetValue("C"),
                row.GetValue("D"),
                string.Join(", ", plots),
                memberIdentity.NormalizedName,
                "The supplemental sheet row was recorded for ownership conflict detection only."));
        }

        sheetStatistics.Add(new LegacyElectricitySheetDiagnostics(
            sheet.Name,
            sheet.LastRowNumber,
            0,
            blankRows,
            0,
            0,
            0,
            ignoredSupplementalRows,
            0));
    }

    private static void DetectWorkbookOwnershipConflicts(
        IReadOnlyList<LegacyElectricityImportCandidate> candidates,
        IReadOnlyList<LegacySupplementalOwnerRow> supplementalRows,
        List<LegacyElectricityImportIssue> issues)
    {
        var observations = new Dictionary<string, List<LegacyOwnershipObservation>>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates.Where(candidate => candidate.CreatesOwnerships))
        {
            foreach (var plot in candidate.PlotNumbers)
            {
                AddObservation(observations, plot, new LegacyOwnershipObservation(candidate.SheetName, candidate.RowNumber, candidate.NormalizedMemberName, candidate.OriginalPlotValue, candidate.OriginalMemberValue));
            }
        }

        foreach (var row in supplementalRows)
        {
            foreach (var plot in row.PlotNumbers)
            {
                AddObservation(observations, plot, new LegacyOwnershipObservation(row.SheetName, row.RowNumber, row.NormalizedMemberName, row.OriginalPlotValue, row.OriginalMemberValue));
            }
        }

        foreach (var pair in observations)
        {
            var distinctMembers = pair.Value.Select(item => item.MemberName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinctMembers.Count <= 1)
            {
                continue;
            }

            foreach (var observation in pair.Value)
            {
                issues.Add(CreateIssue(
                    LegacyElectricityImportIssueSeverity.Error,
                    "ConflictingPlotOwnerInWorkbook",
                    observation.SheetName,
                    observation.RowNumber,
                    observation.OriginalPlotValue,
                    observation.OriginalMemberValue,
                    null,
                    null,
                    pair.Key,
                    observation.MemberName,
                    $"Plot {pair.Key} is associated with multiple members in the workbook: {string.Join(", ", distinctMembers)}.",
                    BuildConflictRowReferences(pair.Value)));
            }
        }
    }

    private static void DetectSupplementalMemberPlotDisagreements(
        IReadOnlyList<LegacyElectricityImportCandidate> candidates,
        IReadOnlyList<LegacySupplementalOwnerRow> supplementalRows,
        List<LegacyElectricityImportIssue> issues)
    {
        var primaryByMember = candidates
            .Where(candidate => candidate.CreatesOwnerships)
            .GroupBy(candidate => candidate.NormalizedMemberName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Plots = group.SelectMany(candidate => candidate.PlotNumbers).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(plot => plot, StringComparer.OrdinalIgnoreCase).ToList(),
                    Sources = group.Select(candidate => $"{candidate.SheetName}:{candidate.RowNumber}").Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var row in supplementalRows)
        {
            if (!primaryByMember.TryGetValue(row.NormalizedMemberName, out var primary))
            {
                continue;
            }

            var missingPlots = row.PlotNumbers
                .Where(plot => !primary.Plots.Contains(plot, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missingPlots.Count == 0)
            {
                continue;
            }

            issues.Add(CreateIssue(
                LegacyElectricityImportIssueSeverity.Error,
                "SupplementalMemberPlotDisagreement",
                row.SheetName,
                row.RowNumber,
                row.OriginalPlotValue,
                row.OriginalMemberValue,
                null,
                null,
                string.Join(", ", row.PlotNumbers),
                row.NormalizedMemberName,
                $"Supplemental ownership evidence associates member '{row.NormalizedMemberName}' with plot(s) {string.Join(", ", missingPlots)}, while the primary sheet associates the member with plot(s) {string.Join(", ", primary.Plots)}.",
                string.Join(", ", primary.Sources.Append($"{row.SheetName}:{row.RowNumber}"))));
        }
    }

    private static void AddObservation(Dictionary<string, List<LegacyOwnershipObservation>> map, string plot, LegacyOwnershipObservation observation)
    {
        if (!map.TryGetValue(plot, out var items))
        {
            items = [];
            map[plot] = items;
        }

        items.Add(observation);
    }

    private static IReadOnlyList<LegacySheetUsage> BuildSheetUsage(LegacyElectricityWorksheetData primarySheet, IReadOnlyList<LegacyElectricityWorksheetData> supplementalSheets)
    {
        var usage = new List<LegacySheetUsage>
        {
            new(primarySheet.Name, ["A", "B", "C", "D", "E", "F", "G"], "Primary import sheet for members, plots, meters and readings."),
        };

        usage.AddRange(supplementalSheets.Select(sheet => new LegacySheetUsage(sheet.Name, ["A", "B", "C", "D"], "Supplemental ownership and conflict-detection sheet.")));
        return usage;
    }

    internal static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value.Trim(), "\\s+", " ");
    }

    internal static IReadOnlyList<string> ParsePlotNumbers(string? rawValue)
    {
        var normalized = NormalizeWhitespace(rawValue)
            .Replace('–', '-')
            .Replace('—', '-')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        if (Regex.IsMatch(normalized, "^\\d+([.,]0+)?$"))
        {
            var singlePlot = Regex.Match(normalized, "^(\\d+)").Groups[1].Value.TrimStart('0');
            return [string.IsNullOrWhiteSpace(singlePlot) ? "0" : singlePlot];
        }

        return Regex.Split(normalized, "[\\s,./;]+")
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => Regex.Match(token, "^\\d+$").Success ? token.TrimStart('0') : token)
            .Select(token => string.IsNullOrWhiteSpace(token) ? "0" : token)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static LegacyMemberIdentity ParseMemberIdentity(string? rawMember)
    {
        var normalized = NormalizeWhitespace(rawMember);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new LegacyMemberIdentity(string.Empty, null, normalized);
        }

        var match = MeterLabelPattern.Match(normalized);
        if (match.Success)
        {
            var memberName = NormalizeWhitespace(match.Groups["member"].Value);
            var meterLabel = NormalizeMeterLabel(match.Groups["label"].Value);
            return new LegacyMemberIdentity(memberName, meterLabel, normalized);
        }

        return new LegacyMemberIdentity(normalized, null, normalized);
    }

    private static string NormalizeMeterLabel(string? rawLabel)
    {
        return NormalizeWhitespace(rawLabel)
            .Trim()
            .TrimEnd('.', ',', ';', ':');
    }

    private static bool SharesBaseMember(string previousMember, string currentMember)
    {
        var previousHead = previousMember.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var currentHead = currentMember.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return previousHead.Length > 0 && string.Equals(previousHead, currentHead, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTariffComponentContinuation(string? previousCellValue, string? currentCellValue)
    {
        return ContainsTariffMarker(previousCellValue, LegacyTariffComponent.T2)
            || ContainsTariffMarker(currentCellValue, LegacyTariffComponent.T2);
    }

    private static LegacyParsedReadingCell ParseReadingCell(string? rawValue, DateOnly defaultDate)
    {
        var normalized = NormalizeWhitespace(rawValue);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return LegacyParsedReadingCell.Empty;
        }

        if (DisconnectedMarkers.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return new LegacyParsedReadingCell(null, null, defaultDate, LegacyTariffComponent.None, LegacyElectricityImportIssueSeverity.Warning, "MissingReadingMarker", "The row contains a disconnected or no-data marker, so no reading will be imported.");
        }

        var component = ContainsTariffMarker(normalized, LegacyTariffComponent.T1)
            ? LegacyTariffComponent.T1
            : ContainsTariffMarker(normalized, LegacyTariffComponent.T2)
                ? LegacyTariffComponent.T2
                : LegacyTariffComponent.None;

        var date = TryParseEmbeddedDate(normalized, out var parsedDate)
            ? parsedDate
            : (DateOnly?)null;

        var numericSource = date.HasValue
            ? EmbeddedDatePattern.Replace(normalized, string.Empty)
            : normalized;

        numericSource = Regex.Replace(
            numericSource,
            @"(?i)[tт][ \t]*[12][.:]?",
            string.Empty);

        var numericMatches = Regex.Matches(
            numericSource.Replace(',', '.'),
            @"\d+(?:\.\d+)?");
        decimal? numericValue = null;
        foreach (Match match in numericMatches)
        {
            if (decimal.TryParse(match.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsedNumber))
            {
                numericValue = parsedNumber;
                break;
            }
        }

        if (!numericValue.HasValue)
        {
            return new LegacyParsedReadingCell(null, null, defaultDate, component, LegacyElectricityImportIssueSeverity.Error, "ReadingValueUnparsed", "The source reading cell does not contain a valid numeric reading.");
        }

        if (numericValue.Value < 0m)
        {
            return new LegacyParsedReadingCell(null, null, defaultDate, component, LegacyElectricityImportIssueSeverity.Error, "NegativeReadingValue", "The source reading value is negative.");
        }

        return new LegacyParsedReadingCell(numericValue.Value, date, defaultDate, component, null, null, null);
    }

    private static bool ContainsTariffMarker(string? value, LegacyTariffComponent component)
    {
        if (string.IsNullOrWhiteSpace(value) || component == LegacyTariffComponent.None)
        {
            return false;
        }

        var token = component == LegacyTariffComponent.T1 ? "т1" : "т2";
        return value.Contains(token, StringComparison.OrdinalIgnoreCase)
            || value.Contains(component == LegacyTariffComponent.T1 ? "t1" : "t2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseEmbeddedDate(string text, out DateOnly date)
    {
        var match = EmbeddedDatePattern.Match(text);
        if (match.Success)
        {
            var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            var yearText = match.Groups["year"].Value;
            var year = yearText.Length == 2 ? 2000 + int.Parse(yearText, CultureInfo.InvariantCulture) : int.Parse(yearText, CultureInfo.InvariantCulture);
            date = new DateOnly(year, month, day);
            return true;
        }

        date = default;
        return false;
    }

    private static LegacyElectricityImportIssue CreateIssue(
        LegacyElectricityImportIssueSeverity severity,
        string code,
        string sheetName,
        int rowNumber,
        string? originalPlotValue,
        string? originalMemberValue,
        string? originalPreviousValue,
        string? originalCurrentValue,
        string? normalizedPlots,
        string? normalizedMember,
        string message)
    {
        return CreateIssue(severity, code, sheetName, rowNumber, originalPlotValue, originalMemberValue, originalPreviousValue, originalCurrentValue, normalizedPlots, normalizedMember, message, null);
    }

    private static LegacyElectricityImportIssue CreateIssue(
        LegacyElectricityImportIssueSeverity severity,
        string code,
        string sheetName,
        int rowNumber,
        string? originalPlotValue,
        string? originalMemberValue,
        string? originalPreviousValue,
        string? originalCurrentValue,
        string? normalizedPlots,
        string? normalizedMember,
        string message,
        string? conflictRows)
    {
        return new LegacyElectricityImportIssue(
            severity,
            code,
            sheetName,
            rowNumber,
            new Dictionary<string, string?>
            {
                ["Plot"] = originalPlotValue,
                ["Member"] = originalMemberValue,
                ["PreviousReading"] = originalPreviousValue,
                ["CurrentReading"] = originalCurrentValue
            },
            new Dictionary<string, string?>
            {
                ["PlotNumbers"] = normalizedPlots,
                ["MemberName"] = normalizedMember,
                ["ConflictRows"] = conflictRows
            },
            message);
    }

    private static string BuildConflictRowReferences(IEnumerable<LegacyOwnershipObservation> observations)
    {
        return string.Join(", ", observations
            .Select(observation => $"{observation.SheetName}:{observation.RowNumber}:{observation.MemberName}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);
}

internal sealed record LegacyValidatedWorkbook(
    IReadOnlyList<LegacyElectricityImportCandidate> Candidates,
    IReadOnlyList<LegacySupplementalOwnerRow> SupplementalRows,
    IReadOnlyList<LegacyElectricityImportIssue> Issues,
    IReadOnlyList<string> SheetsInspected,
    IReadOnlyList<LegacySheetUsage> SheetUsage,
    LegacyElectricityImportDiagnostics Diagnostics,
    IReadOnlyList<LegacyOwnershipCandidate> OwnershipCandidates);

internal sealed record LegacySheetUsage(string SheetName, IReadOnlyList<string> ColumnsUsed, string Purpose);

internal sealed record LegacySupplementalOwnerRow(
    string SheetName,
    int RowNumber,
    IReadOnlyList<string> PlotNumbers,
    string NormalizedMemberName,
    string? OriginalPlotValue,
    string? OriginalMemberValue);

internal sealed record LegacyOwnershipCandidate(string SheetName, int RowNumber, string PlotNumber, string MemberName);

internal sealed class LegacyElectricityImportCandidate
{
    private readonly List<LegacyReadingComponent> _previousComponents = [];
    private readonly List<LegacyReadingComponent> _currentComponents = [];

    public LegacyElectricityImportCandidate(
        string sheetName,
        int rowNumber,
        List<int> sourceRows,
        string? originalPlotValue,
        string? originalMemberValue,
        List<string> plotNumbers,
        string normalizedMemberName,
        string? meterLabel,
        string? manualDifferenceValue,
        string? paymentValue,
        string? noteValue,
        bool createsOwnerships)
    {
        SheetName = sheetName;
        RowNumber = rowNumber;
        SourceRows = sourceRows;
        OriginalPlotValue = originalPlotValue;
        OriginalMemberValue = originalMemberValue;
        PlotNumbers = plotNumbers;
        NormalizedMemberName = normalizedMemberName;
        MeterLabel = meterLabel;
        ManualDifferenceValue = manualDifferenceValue;
        PaymentValue = paymentValue;
        NoteValue = noteValue;
        CreatesOwnerships = createsOwnerships;
    }

    public string SheetName { get; }

    public int RowNumber { get; }

    public List<int> SourceRows { get; }

    public string? OriginalPlotValue { get; }

    public string? OriginalMemberValue { get; }

    public IReadOnlyList<string> PlotNumbers { get; }

    public string NormalizedMemberName { get; }

    public string? MeterLabel { get; }

    public string? ManualDifferenceValue { get; }

    public string? PaymentValue { get; }

    public string? NoteValue { get; }

    public bool CreatesOwnerships { get; }

    public bool HasCombinedTariffSource { get; set; }

    public bool HasExplicitT1Marker { get; set; }

    public decimal? PreviousReadingValue => _previousComponents.Count == 0 ? null : _previousComponents.Sum(item => item.Value);

    public decimal? CurrentReadingValue => _currentComponents.Count == 0 ? null : _currentComponents.Sum(item => item.Value);

    public DateOnly PreviousReadingDate => _previousComponents.Select(item => item.Date).DefaultIfEmpty().Max();

    public DateOnly CurrentReadingDate => _currentComponents.Select(item => item.Date).DefaultIfEmpty().Max();

    public IReadOnlyList<LegacyReadingComponent> PreviousComponents => _previousComponents;

    public IReadOnlyList<LegacyReadingComponent> CurrentComponents => _currentComponents;

    public void AddPreviousTariffComponent(decimal value, LegacyTariffComponent component, DateOnly date)
    {
        _previousComponents.Add(new LegacyReadingComponent(value, component, date));
    }

    public void AddCurrentTariffComponent(decimal value, LegacyTariffComponent component, DateOnly date)
    {
        _currentComponents.Add(new LegacyReadingComponent(value, component, date));
    }
}

internal sealed record LegacyMemberIdentity(string NormalizedName, string? MeterLabel, string OriginalValue);

internal sealed record LegacyReadingComponent(decimal Value, LegacyTariffComponent Component, DateOnly Date);

internal sealed record LegacyParsedReadingCell(
    decimal? NumericValue,
    DateOnly? ExplicitDate,
    DateOnly DefaultDate,
    LegacyTariffComponent Component,
    LegacyElectricityImportIssueSeverity? Severity,
    string? Code,
    string? Message)
{
    public static LegacyParsedReadingCell Empty => new(null, null, default, LegacyTariffComponent.None, null, null, null);
}

internal sealed record LegacyOwnershipObservation(string SheetName, int RowNumber, string MemberName, string? OriginalPlotValue, string? OriginalMemberValue);

internal enum LegacyTariffComponent
{
    None = 0,
    T1 = 1,
    T2 = 2
}
