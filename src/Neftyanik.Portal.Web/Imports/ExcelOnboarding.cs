using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Imports;

public sealed record OnboardingRow(int Row, string MemberKey, string FullName, string Phone, string Email,
    string PlotNumber, string Address, decimal? Area, string CadastralNumber, decimal Balance);
public sealed record OnboardingIssue(int Row, string Message);
public sealed class OnboardingPreview
{
    public List<OnboardingRow> Rows { get; set; } = [];
    public List<OnboardingIssue> Issues { get; set; } = [];
    public int Members => Rows.Where(r => r.MemberKey.Length > 0).Select(r => r.MemberKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public int Plots => Rows.Count(r => r.PlotNumber.Length > 0);
    public decimal Debt => Rows.Sum(r => Math.Max(r.Balance, 0));
    public decimal Credit => Rows.Sum(r => Math.Max(-r.Balance, 0));
}

public static class ExcelOnboarding
{
    public const int MaxRows = 1000;
    public const int MaxBytes = 5 * 1024 * 1024;
    private const string OpeningCode = "EXCEL_OPENING_BALANCE";
    private static readonly string[][] Headers =
    [
        ["Код участника", "Код учасника", "Member key"], ["ФИО", "ПІБ", "Full name"],
        ["Телефон", "Телефон", "Phone"], ["Email", "Email", "Email"],
        ["Номер участка", "Номер ділянки", "Plot number"], ["Адрес", "Адреса", "Address"],
        ["Площадь, м²", "Площа, м²", "Area, m²"], ["Кадастровый номер", "Кадастровий номер", "Cadastral number"],
        ["Остаток, грн", "Залишок, грн", "Balance, UAH"]
    ];
    public static string L(string ru, string uk, string en) => AppLocalizer.Get(ru, uk, en);
    public static string Header(int index) => L(Headers[index][0], Headers[index][1], Headers[index][2]);

    public static byte[] Template()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Import");
        for (var i = 0; i < Headers.Length; i++) sheet.Cell(1, i + 1).Value = Header(i);
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EFFA");
        sheet.Columns(1, 9).Width = 26;
        sheet.Columns(1, 6).Style.NumberFormat.Format = "@";
        sheet.Column(8).Style.NumberFormat.Format = "@";
        sheet.Column(7).Style.NumberFormat.Format = "0.00";
        sheet.Column(9).Style.NumberFormat.Format = "0.00";
        sheet.SheetView.FreezeRows(1);
        var help = workbook.AddWorksheet("Readme");
        help.Cell(1, 1).Value = L("Заполняйте лист Import с 2-й строки. Максимум 1000 строк. Столбцы не переименовывайте.", "Заповнюйте аркуш Import з 2-го рядка. Максимум 1000 рядків. Не перейменовуйте стовпці.", "Fill in Import starting at row 2. Maximum 1000 rows. Keep the column headers.");
        help.Cell(2, 1).Value = L("Код участника — ваш код в этом файле (например, M001). Для нескольких участков одного человека повторите код, ФИО и контакты.", "Код учасника — ваш код у цьому файлі (наприклад, M001). Для кількох ділянок однієї людини повторіть код, ПІБ і контакти.", "Member key is your identifier in this file (e.g. M001). Repeat the key, name and contacts for a member with multiple plots.");
        help.Cell(3, 1).Value = L("Одна строка — один участок и его владелец. Можно добавить участника без участка или участок без владельца.", "Один рядок — одна ділянка та її власник. Можна додати учасника без ділянки або ділянку без власника.", "One row per plot and its owner. A member without a plot or a plot without an owner is also allowed.");
        help.Cell(4, 1).Value = L("Остаток: плюс — долг, минус — переплата, пусто — 0. Для остатка нужны участок и владелец. Указывайте остаток каждого участка, не повторяйте общий остаток владельца.", "Залишок: плюс — борг, мінус — переплата, порожньо — 0. Для залишку потрібні ділянка та власник. Вказуйте залишок кожної ділянки, не повторюйте загальну суму власника.", "Balance: positive = debt, negative = credit, blank = 0. A balance requires both a plot and an owner. Enter each plot's balance; do not repeat the member's total.");
        help.Cell(5, 1).Value = L("Создаются только новые записи. Совпадения с участниками или участками портала блокируют импорт. Формулы не поддерживаются — вставьте значения. Дата переноса выбирается в портале.", "Створюються лише нові записи. Збіги з учасниками або ділянками порталу блокують імпорт. Формули не підтримуються — вставте значення. Дата перенесення обирається в порталі.", "Only new records are created. Existing member or plot matches block import. Paste values instead of formulas. Select the transfer date in the portal.");
        help.Cell(6, 1).Value = L("Переплата переносится на лицевой счёт и не увеличивает кассу или банк. Остатки денежных средств вводятся отдельно в финансовых настройках.", "Переплата переноситься на особовий рахунок і не збільшує касу чи банк. Залишки коштів вводяться окремо у фінансових налаштуваннях.", "Credit is transferred to the member account without increasing cash or bank funds. Enter opening cash and bank balances separately in finance settings.");
        help.Column(1).Width = 110;
        help.Rows(1, 6).Height = 65;
        help.RangeUsed()!.Style.Alignment.WrapText = true;
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    public static OnboardingPreview Parse(byte[] bytes)
    {
        var result = new OnboardingPreview();
        void Error(int row, string message) => result.Issues.Add(new(row, message));
        try
        {
            if (bytes.Length == 0 || bytes.Length > MaxBytes) throw new InvalidDataException();
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                if (zip.Entries.Count > 1000 || zip.Entries.Sum(e => e.Length) > 40L * 1024 * 1024)
                    throw new InvalidDataException();
            }
            using var workbook = new XLWorkbook(new MemoryStream(bytes));
            if (!workbook.TryGetWorksheet("Import", out var sheet))
            {
                Error(0, L("Не найден лист Import. Используйте шаблон.", "Не знайдено аркуш Import. Використайте шаблон.", "Import sheet not found. Use the template."));
                return result;
            }
            for (var i = 0; i < Headers.Length; i++)
                if (!Headers[i].Contains(sheet.Cell(1, i + 1).GetString().Trim(), StringComparer.OrdinalIgnoreCase))
                    Error(1, L("Неверный заголовок столбца: ", "Невірний заголовок стовпця: ", "Invalid column header: ") + Header(i));
            var last = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
            if (last > MaxRows + 1 || (sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0) > Headers.Length)
                Error(0, L("Допускается до 1000 строк и только столбцы шаблона.", "Дозволено до 1000 рядків і лише стовпці шаблону.", "Use at most 1000 rows and only the template columns."));
            if (result.Issues.Count > 0) return result;
            for (var row = 2; row <= last; row++)
            {
                var cells = Enumerable.Range(1, Headers.Length).Select(c => sheet.Cell(row, c)).ToArray();
                if (cells.All(c => c.IsEmpty())) continue;
                if (cells.Any(c => c.HasFormula))
                {
                    Error(row, L("Замените формулы готовыми значениями.", "Замініть формули готовими значеннями.", "Replace formulas with values."));
                    continue;
                }
                var values = cells.Select(c => c.GetString().Trim()).ToArray();
                decimal? Number(int index)
                {
                    if (values[index].Length == 0) return null;
                    var normalized = values[index].Replace("\u00a0", "").Replace(" ", "").Replace(',', '.');
                    if (decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var number) && Math.Abs(number) <= 999999999999m && decimal.Round(number, 2) == number)
                        return number;
                    Error(row, Header(index) + ": " + L("нужно число, максимум 2 знака после запятой.", "потрібне число, максимум 2 знаки після коми.", "enter a number with at most 2 decimal places."));
                    return null;
                }
                result.Rows.Add(new(row, values[0], values[1], values[2], values[3], values[4], values[5], Number(6), values[7], Number(8) ?? 0));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException)
        {
            Error(0, L("Не удалось прочитать файл. Нужен незашифрованный .xlsx до 5 МБ по шаблону.", "Не вдалося прочитати файл. Потрібен незашифрований .xlsx до 5 МБ за шаблоном.", "Cannot read the file. Use an unencrypted template .xlsx up to 5 MB."));
        }
        return result;
    }

    public static async Task ValidateAsync(ApplicationDbContext db, OnboardingPreview preview, DateOnly date, CancellationToken ct)
    {
        if (!db.IsAssociationResolved) throw new InvalidOperationException("Association required.");
        void Error(int row, string ru, string uk, string en) => preview.Issues.Add(new(row, L(ru, uk, en)));
        if (date < new DateOnly(2000, 1, 1) || date > DateOnly.FromDateTime(DateTime.Today))
            Error(0, "Укажите дату переноса с 2000 года, не позже сегодняшнего дня.", "Вкажіть дату перенесення з 2000 року, не пізніше сьогодні.", "Select a transfer date from 2000 through today.");
        if (preview.Rows.Count == 0 || preview.Rows.Count > MaxRows)
            Error(0, "Файл должен содержать от 1 до 1000 строк данных.", "Файл має містити від 1 до 1000 рядків даних.", "The file must contain 1–1000 data rows.");
        var members = new Dictionary<string, OnboardingRow>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memberOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in preview.Rows)
        {
            var hasMember = r.MemberKey.Length > 0;
            if (r.MemberKey.Length > 50 || r.FullName.Length > 200 || r.Phone.Length > 50 || r.Email.Length > 256 ||
                r.PlotNumber.Length > 50 || r.Address.Length > 250 || r.CadastralNumber.Length > 100)
                Error(r.Row, "Превышена длина одного из полей.", "Перевищено довжину одного з полів.", "A field exceeds its maximum length.");
            if (hasMember != (r.FullName.Length > 0) || (!hasMember && (r.Phone.Length > 0 || r.Email.Length > 0 || r.Balance != 0)))
                Error(r.Row, "Для участника и остатка нужны код участника и ФИО.", "Для учасника та залишку потрібні код учасника й ПІБ.", "Member key and full name are required for member details and balances.");
            if (!hasMember && r.PlotNumber.Length == 0)
                Error(r.Row, "Укажите участника или номер участка.", "Вкажіть учасника або номер ділянки.", "Enter a member or a plot number.");
            if (r.Balance != 0 && r.PlotNumber.Length == 0)
                Error(r.Row, "Для начального остатка укажите участок и его владельца.", "Для початкового залишку вкажіть ділянку та її власника.", "An opening balance requires a plot and its owner.");
            if (r.PlotNumber.Length == 0 && (r.Address.Length > 0 || r.Area.HasValue || r.CadastralNumber.Length > 0))
                Error(r.Row, "Для данных участка нужен его номер.", "Для даних ділянки потрібен її номер.", "Plot details require a plot number.");
            if (r.Area is <= 0 || r.Area > 999999999999m || Math.Abs(r.Balance) > 999999999999m)
                Error(r.Row, "Проверьте площадь и сумму остатка.", "Перевірте площу й суму залишку.", "Check the area and balance amount.");
            if ((r.Email.Length > 0 && !new EmailAddressAttribute().IsValid(r.Email)) ||
                (r.Phone.Length > 0 && !new PhoneAttribute().IsValid(r.Phone)))
                Error(r.Row, "Проверьте телефон и email.", "Перевірте телефон та email.", "Check the phone number and email.");
            if (r.PlotNumber.Length > 0 && !plots.Add(r.PlotNumber))
                Error(r.Row, "Номер участка повторяется в файле.", "Номер ділянки повторюється у файлі.", "Duplicate plot number in the file.");
            if (hasMember)
            {
                if (members.TryGetValue(r.MemberKey, out var previous) && (previous.FullName != r.FullName || previous.Phone != r.Phone || previous.Email != r.Email))
                    Error(r.Row, "У одинакового кода участника отличаются ФИО или контакты.", "Для однакового коду учасника відрізняються ПІБ або контакти.", "The same member key has different names or contacts.");
                members.TryAdd(r.MemberKey, r);
                if (names.TryGetValue(r.FullName, out var key) && !key.Equals(r.MemberKey, StringComparison.OrdinalIgnoreCase))
                    Error(r.Row, "Одинаковые ФИО указаны с разными кодами. Уточните данные, чтобы исключить дубль.", "Однакові ПІБ вказано з різними кодами. Уточніть дані, щоб виключити дубль.", "The same name has different member keys. Clarify the data to avoid duplicates.");
                names.TryAdd(r.FullName, r.MemberKey);
                if (r.PlotNumber.Length == 0 && !memberOnly.Add(r.MemberKey))
                    Error(r.Row, "Повторная строка участника без участка.", "Повторний рядок учасника без ділянки.", "Duplicate member-only row.");
            }
        }
        var storedNames = (await db.Members.AsNoTracking().Select(m => m.FullName).ToListAsync(ct)).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var storedPlots = (await db.Plots.AsNoTracking().Select(p => p.Number).ToListAsync(ct)).Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var r in members.Values.Where(r => storedNames.Contains(r.FullName)))
            Error(r.Row, "Участник с такими ФИО уже есть в портале. Изменяйте его карточку отдельно.", "Учасник із такими ПІБ уже є в порталі. Змінюйте його картку окремо.", "A member with this name already exists. Edit the existing member separately.");
        foreach (var r in preview.Rows.Where(r => storedPlots.Contains(r.PlotNumber)))
            Error(r.Row, "Участок с таким номером уже есть в портале.", "Ділянка з таким номером уже є в порталі.", "A plot with this number already exists.");
    }

    public static string BatchKey(IReadOnlyList<OnboardingRow> rows, DateOnly date)
    {
        var normalized = rows.Select(r => r with { Row = 0, MemberKey = r.MemberKey.ToUpperInvariant() })
            .OrderBy(r => r.MemberKey, StringComparer.Ordinal).ThenBy(r => r.PlotNumber, StringComparer.Ordinal).ToArray();
        return "ExcelImport:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { date, rows = normalized }))));
    }

    public static async Task<OnboardingPreview> SaveAsync(ApplicationDbContext db, List<OnboardingRow> rows, DateOnly date, string userId, CancellationToken ct)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = new OnboardingPreview { Rows = rows };
            var batchKey = BatchKey(rows, date);
            if (await db.SystemSettings.AnyAsync(s => s.Key == batchKey, ct))
            {
                result.Issues.Add(new(0, L("Эта загрузка уже сохранена.", "Це завантаження вже збережено.", "This import was already saved.")));
                return result;
            }
            await ValidateAsync(db, result, date, ct);
            if (result.Issues.Count > 0) return result;
            var members = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
            ChargeType? openingType = null;
            if (rows.Any(r => r.Balance > 0))
            {
                openingType = await db.ChargeTypes.SingleOrDefaultAsync(t => t.Code == OpeningCode, ct);
                if (openingType is null)
                {
                    openingType = new ChargeType { Code = OpeningCode, Name = L("Начальный остаток", "Початковий залишок", "Opening balance") };
                    db.ChargeTypes.Add(openingType);
                }
            }
            foreach (var r in rows)
            {
                Member? member = null;
                if (r.MemberKey.Length > 0 && !members.TryGetValue(r.MemberKey, out member))
                {
                    member = new Member { FullName = r.FullName, PhoneNumber = EmptyToNull(r.Phone), Email = EmptyToNull(r.Email) };
                    members.Add(r.MemberKey, member);
                    db.Members.Add(member);
                }
                Plot? plot = null;
                if (r.PlotNumber.Length > 0)
                {
                    plot = new Plot { Number = r.PlotNumber, Address = EmptyToNull(r.Address), AreaSquareMeters = r.Area, CadastralNumber = EmptyToNull(r.CadastralNumber) };
                    db.Plots.Add(plot);
                    if (member is not null) db.PlotOwnerships.Add(new PlotOwnership { Member = member, Plot = plot, IsPrimaryContact = true, ValidFrom = date });
                }
                var description = L("Перенос начального остатка из Excel", "Перенесення початкового залишку з Excel", "Opening balance transferred from Excel") + $" ({date:yyyy-MM-dd})";
                if (r.Balance > 0) db.Charges.Add(new Charge { Member = member, Plot = plot, ChargeType = openingType, Amount = r.Balance, ChargeDate = date, Description = description, CreatedByUserId = userId });
                if (r.Balance < 0) db.Payments.Add(new Payment { Member = member, Plot = plot, Amount = -r.Balance, PaymentDate = date, PaymentMethod = PaymentMethod.OpeningBalance, Description = description, ReferenceNumber = batchKey, CreatedByUserId = userId });
            }
            db.SystemSettings.Add(new SystemSetting { Key = batchKey, Value = $"{date:yyyy-MM-dd}; members={result.Members}; plots={result.Plots}", UpdatedByUserId = userId });
            db.FinancialAuditLogs.Add(new FinancialAuditLog { Action = "ExcelImport", EntityType = "Onboarding", EntityId = batchKey,
                UserId = userId, Description = $"Excel: {result.Members} members; {result.Plots} plots; {date:yyyy-MM-dd}",
                NewValuesJson = JsonSerializer.Serialize(new { date, result.Members, result.Plots, result.Debt, result.Credit }) });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
