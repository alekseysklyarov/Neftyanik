using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;

namespace Neftyanik.Portal.Infrastructure.LegacyImport;

internal sealed class LegacyElectricityWorkbookReader
{
    public async Task<LegacyElectricityWorkbookData> ReadAsync(string workbookPath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(workbookPath);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        var workbookHash = Convert.ToHexString(hashBytes);
        stream.Position = 0;

        using var workbook = new XLWorkbook(stream);
        var sheets = new List<LegacyElectricityWorksheetData>();
        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();
            var lastRow = usedRange?.LastRow().RowNumber() ?? 0;
            var lastColumn = usedRange?.LastColumn().ColumnNumber() ?? 0;
            var rows = new List<LegacyElectricityWorksheetRow>();
            var columnMetadata = new Dictionary<string, LegacyElectricityColumnMetadata>(StringComparer.OrdinalIgnoreCase);
            var mergedRanges = worksheet.MergedRanges.Select(range => range.RangeAddress.ToString()).ToList();
            var formulaCells = new List<string>();
            var commentCells = new List<string>();

            for (var rowNumber = 1; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = worksheet.Row(rowNumber);
                var cells = new Dictionary<string, LegacyElectricityCellData>(StringComparer.OrdinalIgnoreCase);
                var hasAnyValue = false;

                for (var columnNumber = 1; columnNumber <= lastColumn; columnNumber++)
                {
                    var cell = row.Cell(columnNumber);
                    var columnLetter = XLHelper.GetColumnLetterFromNumber(columnNumber);
                    var originalText = GetCellDisplayValue(cell);
                    var formula = string.IsNullOrWhiteSpace(cell.FormulaA1) ? null : cell.FormulaA1;
                    var comment = cell.HasComment ? cell.GetComment().Text : null;
                    var dataType = cell.DataType.ToString();

                    if (!columnMetadata.TryGetValue(columnLetter, out var metadata))
                    {
                        metadata = new LegacyElectricityColumnMetadata(columnLetter);
                        columnMetadata[columnLetter] = metadata;
                    }

                    if (!string.IsNullOrWhiteSpace(originalText) || formula is not null || comment is not null)
                    {
                        hasAnyValue = true;
                        metadata.NonEmptyCellCount++;
                    }

                    if (formula is not null)
                    {
                        formulaCells.Add($"{columnLetter}{rowNumber}");
                    }

                    if (comment is not null)
                    {
                        commentCells.Add($"{columnLetter}{rowNumber}");
                    }

                    if (rowNumber == 1 && !string.IsNullOrWhiteSpace(originalText))
                    {
                        metadata.HeaderValue = originalText;
                    }

                    cells[columnLetter] = new LegacyElectricityCellData(
                        columnLetter,
                        rowNumber,
                        originalText,
                        dataType,
                        formula,
                        comment);
                }

                if (hasAnyValue)
                {
                    rows.Add(new LegacyElectricityWorksheetRow(rowNumber, cells));
                }
            }

            sheets.Add(new LegacyElectricityWorksheetData(
                worksheet.Name,
                worksheet.Visibility.ToString(),
                lastRow,
                lastColumn,
                rows,
                columnMetadata.Values.OrderBy(item => item.ColumnLetter, StringComparer.Ordinal).ToList(),
                mergedRanges,
                formulaCells,
                commentCells));
        }

        return new LegacyElectricityWorkbookData(workbookPath, workbookHash, sheets);
    }

    private static string? GetCellDisplayValue(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        var value = cell.GetFormattedString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        var text = cell.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

internal sealed record LegacyElectricityWorkbookData(
    string WorkbookPath,
    string WorkbookHash,
    IReadOnlyList<LegacyElectricityWorksheetData> Sheets);

internal sealed record LegacyElectricityWorksheetData(
    string Name,
    string Visibility,
    int LastRowNumber,
    int LastColumnNumber,
    IReadOnlyList<LegacyElectricityWorksheetRow> Rows,
    IReadOnlyList<LegacyElectricityColumnMetadata> Columns,
    IReadOnlyList<string> MergedRanges,
    IReadOnlyList<string> FormulaCells,
    IReadOnlyList<string> CommentCells);

internal sealed record LegacyElectricityWorksheetRow(int RowNumber, IReadOnlyDictionary<string, LegacyElectricityCellData> Cells)
{
    public string? GetValue(string columnLetter)
    {
        return Cells.TryGetValue(columnLetter, out var cell) ? cell.Value : null;
    }
}

internal sealed record LegacyElectricityCellData(
    string ColumnLetter,
    int RowNumber,
    string? Value,
    string CellType,
    string? Formula,
    string? Comment);

internal sealed class LegacyElectricityColumnMetadata
{
    public LegacyElectricityColumnMetadata(string columnLetter)
    {
        ColumnLetter = columnLetter;
    }

    public string ColumnLetter { get; }

    public string? HeaderValue { get; set; }

    public int NonEmptyCellCount { get; set; }
}
