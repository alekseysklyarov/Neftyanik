namespace Neftyanik.Portal.Application.LegacyImport;

public interface ILegacyElectricityImportService
{
    Task<LegacyElectricityImportResult> ExecuteAsync(LegacyElectricityImportRequest request, CancellationToken cancellationToken = default);
}

public sealed record LegacyElectricityImportRequest(
    bool Commit,
    bool Force = false,
    DateOnly? DefaultPreviousReadingDate = null,
    DateOnly? DefaultCurrentReadingDate = null,
    DateOnly? OwnershipEffectiveFrom = null,
    string? WorkbookRelativePath = null,
    string? ReportsRelativePath = null);

public sealed record LegacyElectricityImportResult(
    bool Succeeded,
    bool CommitRequested,
    bool ForceRequested,
    bool Committed,
    bool CommittedWithBlockingIssues,
    int BlockingIssueCount,
    string WorkbookPath,
    string WorkbookHash,
    IReadOnlyList<string> SheetsInspected,
    LegacyElectricityImportConfigurationSnapshot Configuration,
    LegacyElectricityImportStatistics Statistics,
    LegacyElectricityConflictSkipCount SkippedBecauseOfConflict,
    LegacyElectricityImportDiagnostics Diagnostics,
    IReadOnlyList<LegacyElectricityImportIssue> Issues,
    string MarkdownReportPath,
    string JsonReportPath,
    string SummaryMessage);

public sealed record LegacyElectricityImportConfigurationSnapshot(
    DateOnly DefaultPreviousReadingDate,
    DateOnly DefaultCurrentReadingDate,
    DateOnly OwnershipEffectiveFrom,
    string WorkbookRelativePath,
    string ReportsRelativePath);

public sealed record LegacyElectricityImportStatistics(
    int SourceRowCount,
    int RowsExcluded,
    LegacyElectricityEntityImportCount Members,
    LegacyElectricityEntityImportCount Plots,
    LegacyElectricityEntityImportCount Ownerships,
    LegacyElectricityEntityImportCount Meters,
    LegacyElectricityEntityImportCount Readings);

public sealed record LegacyElectricityImportDiagnostics(
    int PhysicalSourceRowsInspected,
    int EligibleEntityImportRows,
    int UniqueNormalizedMemberCount,
    int UniqueNormalizedPlotCount,
    int MemberToPlotRelationshipCount,
    int OwnershipCandidatesBeforeDeduplication,
    int OwnershipsAfterDeduplication,
    int DuplicateOwnershipsPrevented,
    IReadOnlyList<LegacyElectricitySheetDiagnostics> Sheets,
    IReadOnlyList<LegacyElectricityMemberDiagnostic> Members,
    IReadOnlyList<LegacyElectricityPlotDiagnostic> Plots)
{
    public static LegacyElectricityImportDiagnostics Empty => new(0, 0, 0, 0, 0, 0, 0, 0, [], [], []);
}

public sealed record LegacyElectricitySheetDiagnostics(
    string SheetName,
    int PhysicalRowsInspected,
    int HeaderRows,
    int BlankRows,
    int PrimaryRows,
    int ContinuationMeterRows,
    int ContinuationTariffRows,
    int IgnoredSupplementalRows,
    int EligibleEntityImportRows);

public sealed record LegacyElectricityMemberDiagnostic(
    string SheetName,
    int RowNumber,
    string? OriginalMemberText,
    string NormalizedMemberName,
    string? ExtractedMeterLabel);

public sealed record LegacyElectricityPlotDiagnostic(
    string SheetName,
    int RowNumber,
    string? OriginalPlotText,
    IReadOnlyList<string> ParsedPlotNumbers,
    string NormalizedMemberName);

public sealed record LegacyElectricityEntityImportCount(int Created, int Matched, int Skipped)
{
    public static LegacyElectricityEntityImportCount Empty => new(0, 0, 0);
}

public sealed record LegacyElectricityConflictSkipCount(
    int Members,
    int Plots,
    int Ownerships,
    int Meters,
    int Readings)
{
    public static LegacyElectricityConflictSkipCount Empty => new(0, 0, 0, 0, 0);
}

public sealed record LegacyElectricityImportIssue(
    LegacyElectricityImportIssueSeverity Severity,
    string Code,
    string SheetName,
    int RowNumber,
    IReadOnlyDictionary<string, string?> OriginalValues,
    IReadOnlyDictionary<string, string?> NormalizedValues,
    string Message);

public enum LegacyElectricityImportIssueSeverity
{
    Information = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}
