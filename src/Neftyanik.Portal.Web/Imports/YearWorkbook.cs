using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Imports;

public sealed record YearTransferRow(int Row, string Key, DateOnly Date, string Scope, string Member, string Plot,
    string Meter, bool Initial, decimal Value, decimal? Night, decimal? Second, decimal? SecondNight,
    PaymentMethod Method, string Reference, string Description, bool Cancelled, string CancellationReason);
public sealed record YearTransferIssue(int Row, string Message);
public sealed record YearTransferLine(int Row, string Key, string Detail, bool Skipped, decimal? Amount);
public sealed class YearTransferResult
{
    public List<YearTransferRow> Rows { get; set; } = [];
    public List<YearTransferIssue> Issues { get; set; } = [];
    public List<YearTransferLine> Lines { get; set; } = [];
    public int Added => Lines.Count(x => !x.Skipped);
    public int Skipped => Lines.Count(x => x.Skipped);
}

public static class YearWorkbook
{
    public const string Payments = "payments";
    public const string Readings = "readings";
    public const int MaxRows = 10000;
    public const int MaxBytes = 10 * 1024 * 1024;
    public static bool ValidKind(string kind) => kind is Payments or Readings;
    public static bool ValidYear(int year) => year is >= 2000 and <= 2100;
    public static string L(string ru, string uk, string en) => AppLocalizer.Get(ru, uk, en);
    public static string Title(string kind) => kind == Payments ? L("Платежи", "Платежі", "Payments") : L("Показания электроэнергии", "Показання електроенергії", "Electricity readings");
    public static string[] Headers(string kind) => kind == Payments
        ? ["Key", "Date", "Member", "Plot", "Amount", "Method", "Reference", "Description", "Cancelled", "CancellationReason"]
        : ["Key", "Date", "Scope", "Member", "Plot", "Meter", "Initial", "Day", "Night", "SecondDay", "SecondNight"];

    public static byte[] Write(string kind, int year, IEnumerable<YearTransferRow> rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Data");
        var headers = Headers(kind);
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EFFA");
        sheet.Columns(1, headers.Length).Width = 25;
        sheet.Columns(1, headers.Length).Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);
        var rowNumber = 2;
        foreach (var r in rows)
        {
            if (rowNumber > 1048576) throw new InvalidDataException(L("Превышен лимит строк одного листа Excel.", "Перевищено ліміт рядків одного аркуша Excel.", "The Excel worksheet row limit was exceeded."));
            object?[] values = kind == Payments
                ? [r.Key, r.Date.ToString("yyyy-MM-dd"), r.Member, r.Plot, r.Value, (int)r.Method, r.Reference, r.Description, r.Cancelled ? "1" : "0", r.CancellationReason]
                : [r.Key, r.Date.ToString("yyyy-MM-dd"), r.Scope, r.Member, r.Plot, r.Meter, r.Initial ? "1" : "0", r.Value, r.Night, r.Second, r.SecondNight];
            for (var c = 0; c < values.Length; c++)
            {
                var cell = sheet.Cell(rowNumber, c + 1);
                if (values[c] is decimal number) { cell.Value = number; cell.Style.NumberFormat.Format = kind == Payments ? "0.00" : "0.000"; }
                else if (values[c] is int integer) { cell.Value = integer; cell.Style.NumberFormat.Format = "0"; }
                else cell.Value = values[c]?.ToString() ?? "";
            }
            rowNumber++;
        }
        var info = book.AddWorksheet("Info");
        info.Cell(1, 1).Value = "DachaHub YearTransfer v1";
        info.Cell(2, 1).Value = "Kind"; info.Cell(2, 2).Value = kind;
        info.Cell(3, 1).Value = "Year"; info.Cell(3, 2).Value = year;
        info.Cell(5, 1).Value = L("Заполняйте Data, сохраняя заголовки. Key — уникальный код записи; не меняйте его при повторной загрузке. Date — YYYY-MM-DD. Member — точные ФИО, Plot — номер существующего участка. Все даты должны относиться к выбранному году. Формулы замените значениями.", "Заповнюйте Data, зберігаючи заголовки. Key — унікальний код запису; не змінюйте його під час повторного завантаження. Date — YYYY-MM-DD. Member — точні ПІБ, Plot — номер наявної ділянки. Усі дати мають належати обраному року. Замініть формули значеннями.", "Fill in Data and keep the headers. Key is a unique record identifier: preserve it on reimport. Date uses YYYY-MM-DD. Member is the exact name and Plot is an existing plot number. All dates must be in the selected year. Replace formulas with values.");
        info.Cell(6, 1).Value = kind == Payments
            ? L("Amount — положительная сумма в грн (2 знака). Method: 1 — наличные, 2 — банковский перевод, 3 — карта, 4 — другое, 5 — перенос переплаты (без поступления денег). Cancelled: 0 или 1; при 1 заполните CancellationReason. Reference — номер документа. Импорт распределяет оплату по начислениям по правилам портала; прежние распределения файла не переносятся.", "Amount — додатна сума у грн (2 знаки). Method: 1 — готівка, 2 — банківський переказ, 3 — картка, 4 — інше, 5 — перенесення переплати (без надходження коштів). Cancelled: 0 або 1; для 1 заповніть CancellationReason. Reference — номер документа. Імпорт розподіляє оплату за правилами порталу; попередні розподіли файлу не переносяться.", "Amount is positive UAH (2 decimals). Method: 1 cash, 2 bank transfer, 3 card, 4 other, 5 opening credit (no cash receipt). Cancelled is 0 or 1; provide CancellationReason for 1. Reference is the document number. Allocation follows the target portal's rules; previous allocations are not transferred.")
            : L("Scope: member — счётчик участника, association — общий счётчик. Для member заполните ФИО, участок и Meter (номер счётчика, если задан). Initial: 1 — начальное показание, 0 — очередное. Day/Night — накопительные показания (3 знака); для двух физических счётчиков заполните также SecondDay/SecondNight. Для общего счётчика нужны Day и Night. Начисления рассчитываются по тарифам портала; история добавляется после последней сохранённой даты, по порядку.", "Scope: member — лічильник учасника, association — загальний лічильник. Для member заповніть ПІБ, ділянку й Meter (номер лічильника, якщо заданий). Initial: 1 — початкове показання, 0 — чергове. Day/Night — накопичувальні показання (3 знаки); для двох фізичних лічильників заповніть також SecondDay/SecondNight. Для загального лічильника потрібні Day та Night. Нарахування обчислюються за тарифами порталу; історія додається після останньої збереженої дати, за порядком.", "Scope is member or association (main meter). For member readings enter the name, billing plot and Meter number if available. Initial is 1 for an opening reading, otherwise 0. Day/Night are cumulative values (3 decimals); use SecondDay/SecondNight for two physical meters. The main meter requires Day and Night. Charges use portal tariffs; readings are appended chronologically after the last saved date.");
        info.Cell(7, 1).Value = L("Одинаковые записи пропускаются. Изменение уже загруженной записи блокируется: используйте штатное исправление в портале. Карточки участников, участков, счётчики и тарифы должны существовать до импорта. Лимит импорта: 10000 строк, 10 МБ. Большую выгрузку разделите на несколько файлов с тем же листом Info.", "Однакові записи пропускаються. Зміна вже завантаженого запису блокується: використовуйте звичайне виправлення в порталі. Картки учасників, ділянок, лічильники й тарифи мають існувати до імпорту. Ліміт імпорту: 10000 рядків, 10 МБ. Велике вивантаження розділіть на кілька файлів із тим самим аркушем Info.", "Matching records are skipped. Changes to previously imported records are blocked: use the portal's correction workflow. Members, plots, meters and tariffs must already exist. Import limit: 10000 rows and 10 MB. Split large exports into several files, preserving Info.");
        info.Column(1).Width = 125; info.Column(2).Width = 25;
        info.Rows(5, 7).Height = 100; info.RangeUsed()!.Style.Alignment.WrapText = true;
        using var output = new MemoryStream(); book.SaveAs(output); return output.ToArray();
    }

    public static YearTransferResult Read(byte[] bytes, string kind, int year)
    {
        var result = new YearTransferResult();
        void Issue(int row, string message) => result.Issues.Add(new(row, message));
        if (!ValidKind(kind) || !ValidYear(year)) { Issue(0, L("Выберите операцию и год от 2000 до 2100.", "Оберіть операцію та рік від 2000 до 2100.", "Select an operation and a year from 2000 through 2100.")); return result; }
        try
        {
            if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException();
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 80L * 1024 * 1024) throw new InvalidDataException();
            using var book = new XLWorkbook(new MemoryStream(bytes));
            if (!book.TryGetWorksheet("Info", out var info) || info.Cell(1, 1).GetString() != "DachaHub YearTransfer v1" ||
                info.Cell(2, 2).GetString() != kind || !info.Cell(3, 2).TryGetValue<int>(out var fileYear) || fileYear != year)
            { Issue(0, L("Тип данных или год файла не совпадает с выбранным. Используйте соответствующий шаблон.", "Тип даних або рік файлу не збігається з обраним. Використайте відповідний шаблон.", "The file type or year does not match your selection. Use the matching template.")); return result; }
            if (!book.TryGetWorksheet("Data", out var sheet)) throw new InvalidDataException();
            var headers = Headers(kind);
            if (headers.Where((h, i) => sheet.Cell(1, i + 1).GetString().Trim() != h).Any() ||
                (sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0) > headers.Length) throw new InvalidDataException();
            var last = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
            if (last > MaxRows + 1) throw new InvalidDataException();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var n = 2; n <= last; n++)
            {
                var cells = Enumerable.Range(1, headers.Length).Select(c => sheet.Cell(n, c)).ToArray();
                if (cells.All(c => c.IsEmpty())) continue;
                var before = result.Issues.Count;
                if (cells.Any(c => c.HasFormula)) { Issue(n, L("Замените формулы значениями.", "Замініть формули значеннями.", "Replace formulas with values.")); continue; }
                var v = cells.Select(c => c.GetString().Trim()).ToArray();
                decimal? Number(int index, int decimals, bool required = false)
                {
                    if (v[index].Length == 0 && !required) return null;
                    if (decimal.TryParse(v[index].Replace("\u00a0", "").Replace(" ", "").Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var value) && value >= 0 && value <= 999999999999m && decimal.Round(value, decimals) == value) return value;
                    Issue(n, headers[index] + ": " + L("проверьте число и точность.", "перевірте число й точність.", "check the number and decimal places.")); return null;
                }
                bool Flag(int index)
                {
                    if (v[index] is "" or "0" or "false") return false;
                    if (v[index] is "1" or "true") return true;
                    Issue(n, headers[index] + ": " + L("допускается 0 или 1.", "дозволено 0 або 1.", "use 0 or 1.")); return false;
                }
                DateOnly date;
                if (cells[1].DataType == XLDataType.DateTime) date = DateOnly.FromDateTime(cells[1].GetDateTime());
                else if (!DateOnly.TryParseExact(v[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    Issue(n, L("Дата должна иметь формат YYYY-MM-DD.", "Дата має бути у форматі YYYY-MM-DD.", "Date must use YYYY-MM-DD."));
                if (date.Year != year) Issue(n, L("Дата не относится к выбранному году.", "Дата не належить обраному року.", "The date is outside the selected year."));
                if (v[0].Length is 0 or > 150 || !keys.Add(v[0])) Issue(n, L("Код Key обязателен, до 150 знаков, без повторов в файле.", "Код Key обов’язковий, до 150 знаків, без повторів у файлі.", "Key is required, up to 150 characters, and must be unique in the file."));
                YearTransferRow row;
                if (kind == Payments)
                {
                    var method = int.TryParse(v[5], out var m) ? (PaymentMethod)m : 0;
                    var amount = Number(4, 2, true) ?? 0; var cancelled = Flag(8);
                    if (amount <= 0 || !Enum.IsDefined(method)) Issue(n, L("Сумма должна быть больше нуля; Method — число от 1 до 5.", "Сума має бути більшою за нуль; Method — число від 1 до 5.", "Amount must be positive; Method must be 1 through 5."));
                    if (v[2].Length is 0 or > 200 || v[3].Length is 0 or > 50 || v[6].Length > 150 || v[7].Length > 1000 || v[9].Length > 500 || (cancelled && v[9].Length == 0))
                        Issue(n, L("Проверьте ФИО, участок, длину текста и причину отмены.", "Перевірте ПІБ, ділянку, довжину тексту й причину скасування.", "Check the member, plot, text lengths and cancellation reason."));
                    row = new(n, v[0], date, "", v[2], v[3], "", false, amount, null, null, null, method, v[6], v[7], cancelled, cancelled ? v[9] : "");
                }
                else
                {
                    var initial = Flag(6); var day = Number(7, 3, true) ?? 0; var night = Number(8, 3); var second = Number(9, 3); var secondNight = Number(10, 3);
                    if (v[2] is not ("member" or "association") || v[3].Length > 200 || v[4].Length > 50 || v[5].Length > 100 ||
                        (v[2] == "member" && (v[3].Length == 0 || v[4].Length == 0)) ||
                        (v[2] == "association" && (v[3].Length > 0 || v[4].Length > 0 || v[5].Length > 0 || !night.HasValue || second.HasValue || secondNight.HasValue)) ||
                        (secondNight.HasValue && !second.HasValue) || (initial && second.HasValue))
                        Issue(n, L("Проверьте Scope, владельца, участок и поля счётчиков. Начальное показание вводится суммарно.", "Перевірте Scope, власника, ділянку й поля лічильників. Початкове показання вводиться сумарно.", "Check Scope, owner, plot and meter fields. Opening readings use combined values."));
                    row = new(n, v[0], date, v[2], v[3], v[4], v[5], initial, day, night, second, secondNight, 0, "", "", false, "");
                }
                if (result.Issues.Count == before) result.Rows.Add(row);
            }
            if (result.Rows.Count == 0 && result.Issues.Count == 0) Issue(0, L("Файл не содержит записей.", "Файл не містить записів.", "The file contains no records."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        { Issue(0, L("Не удалось прочитать .xlsx. Используйте шаблон: до 10 МБ и 10000 строк, без пароля.", "Не вдалося прочитати .xlsx. Використайте шаблон: до 10 МБ і 10000 рядків, без пароля.", "Cannot read .xlsx. Use an unencrypted template up to 10 MB and 10000 rows.")); }
        return result;
    }
}
