using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Export;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
[RequestSizeLimit(16384)]
public class IndexModel(ApplicationDbContext database, IAssociationContext association) : PageModel
{
    public string AssociationName => association.IsResolved ? association.Association.Name : string.Empty;

    public IActionResult OnGet()
    {
        Response.Headers.CacheControl = "no-store";
        return association.IsResolved ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostDownloadAsync(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        if (!association.IsResolved) return NotFound();
        return await database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Read a consistent set before building the workbook; tenant query filters remain enabled.
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var capturedAt = DateTime.UtcNow;
            var members = await database.Members.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.FullName, x.PhoneNumber, x.Email, x.JoinedAt, x.IsActive }).ToListAsync(cancellationToken);
            var plots = await database.Plots.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.Number, x.Address, x.AreaSquareMeters, x.CadastralNumber, x.IsActive }).ToListAsync(cancellationToken);
            var ownerships = await database.PlotOwnerships.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.MemberId, x.PlotId, x.OwnershipShare, x.IsPrimaryContact, x.ValidFrom, x.ValidTo }).ToListAsync(cancellationToken);
            var charges = await database.Charges.AsNoTracking().Where(x => x.CancelledAtUtc == null)
                .Select(x => new { x.MemberId, x.PlotId, x.Amount }).ToListAsync(cancellationToken);
            var payments = await database.Payments.AsNoTracking().Where(x => x.CancelledAtUtc == null)
                .Select(x => new { x.MemberId, x.Amount }).ToListAsync(cancellationToken);
            var plotPayments = await database.LoadActivePaymentTotalsByPlotAsync(plots.Select(x => x.Id), cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var memberCharges = charges.Where(x => x.MemberId.HasValue).GroupBy(x => x.MemberId!.Value).ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));
            var memberPayments = payments.Where(x => x.MemberId.HasValue).GroupBy(x => x.MemberId!.Value).ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));
            var plotCharges = charges.Where(x => x.PlotId.HasValue).GroupBy(x => x.PlotId!.Value).ToDictionary(x => x.Key, x => x.Sum(v => v.Amount));
            var memberNames = members.ToDictionary(x => x.Id, x => x.FullName);
            var plotNumbers = plots.ToDictionary(x => x.Id, x => x.Number);
            using var workbook = new XLWorkbook();
            var info = Sheet(workbook, L("Описание", "Опис", "About"), L("Параметр", "Параметр", "Field"), L("Значение", "Значення", "Value"));
            Row(info, 2, L("Товарищество", "Товариство", "Association"), AssociationName);
            Row(info, 3, L("Адрес товарищества", "Адреса товариства", "Association slug"), association.Slug);
            Row(info, 4, L("Сформировано, UTC", "Сформовано, UTC", "Captured, UTC"), capturedAt.ToString("yyyy-MM-dd HH:mm:ss"));
            Row(info, 5, L("Состав", "Склад", "Contents"), L("Все участники и участки, включая неактивные; все записи владения, включая завершённые.", "Усі учасники й ділянки, включно з неактивними; усі записи володіння, включно із завершеними.", "All members and plots, including inactive records; all ownership records, including ended ownerships."));
            Row(info, 6, L("Финансовый охват", "Фінансове охоплення", "Financial scope"), L("Все сохранённые неотменённые начисления и платежи, включая записи с будущей датой. Это остатки по текущему состоянию базы, без отбора периода.", "Усі збережені нескасовані нарахування й платежі, включно із записами з майбутньою датою. Це залишки за поточним станом бази, без відбору періоду.", "All saved non-cancelled charges and payments, including future-dated entries. Balances reflect the current database state with no period filter."));
            Row(info, 7, L("Знак остатка", "Знак залишку", "Balance sign"), L("Начислено минус оплачено. Плюс — долг; минус — переплата. Суммы в гривнах.", "Нараховано мінус сплачено. Плюс — борг; мінус — переплата. Суми в гривнях.", "Charges minus payments. Positive means debt; negative means credit. Amounts are in UAH."));
            Row(info, 8, L("Два представления", "Два подання", "Two views"), L("Остатки участников и участков — разные представления одних операций, складывать их нельзя. Для участков учтены распределения платежей и нераспределённый остаток.", "Залишки учасників і ділянок — різні подання тих самих операцій, їх не можна додавати. Для ділянок враховано розподіли платежів і нерозподілений залишок.", "Member and plot balances are different views of the same transactions; do not add them together. Plot totals account for payment allocations and unallocated remainders."));
            Row(info, 9, L("Без привязки", "Без прив’язки", "Unassigned"), L("Последняя строка остатков содержит суммы без привязки к участнику или участку. Долг сохраняется за плательщиком начисления, а не текущим владельцем участка.", "Останній рядок залишків містить суми без прив’язки до учасника або ділянки. Борг залишається за платником нарахування, а не поточним власником ділянки.", "The final balance row includes amounts not assigned to a member or plot. Debt remains with the charge’s debtor, not the plot’s current owner."));
            Row(info, 10, L("Назначение файла", "Призначення файлу", "File purpose"), L("Таблицы для сверки и передачи данных. Файл не содержит полной истории операций, документов, паролей или резервной копии системы.", "Таблиці для звірки й передавання даних. Файл не містить повної історії операцій, документів, паролів або резервної копії системи.", "Tables for reconciliation and data handover. The file does not contain the full transaction history, documents, passwords or a system backup."));
            var memberSheet = Sheet(workbook, L("Участники", "Учасники", "Members"), "ID", L("Имя", "Ім’я", "Name"), L("Телефон", "Телефон", "Phone"), "Email", L("Дата вступления", "Дата вступу", "Joined"), L("Активен", "Активний", "Active"));
            var row = 2;
            foreach (var m in members) { cancellationToken.ThrowIfCancellationRequested(); Row(memberSheet, row++, m.Id, m.FullName, m.PhoneNumber, m.Email, Date(m.JoinedAt), Yes(m.IsActive)); }
            var plotSheet = Sheet(workbook, L("Участки", "Ділянки", "Plots"), "ID", L("Номер", "Номер", "Number"), L("Адрес", "Адреса", "Address"), L("Площадь, м²", "Площа, м²", "Area, m²"), L("Кадастровый номер", "Кадастровий номер", "Cadastral number"), L("Активен", "Активна", "Active"));
            row = 2;
            foreach (var p in plots) Row(plotSheet, row++, p.Id, p.Number, p.Address, p.AreaSquareMeters, p.CadastralNumber, Yes(p.IsActive));
            var ownerSheet = Sheet(workbook, L("Владение", "Володіння", "Ownerships"), "ID", L("ID участника", "ID учасника", "Member ID"), L("Имя", "Ім’я", "Name"), L("ID участка", "ID ділянки", "Plot ID"), L("Номер участка", "Номер ділянки", "Plot number"), L("Доля, %", "Частка, %", "Share, %"), L("Основной контакт", "Основний контакт", "Primary contact"), L("С даты", "З дати", "From"), L("По дату", "До дати", "Until"));
            row = 2;
            foreach (var o in ownerships) Row(ownerSheet, row++, o.Id, o.MemberId, memberNames.GetValueOrDefault(o.MemberId), o.PlotId, plotNumbers.GetValueOrDefault(o.PlotId), o.OwnershipShare, Yes(o.IsPrimaryContact), Date(o.ValidFrom), Date(o.ValidTo));
            var memberBalanceSheet = Sheet(workbook, L("Остатки участников", "Залишки учасників", "Member balances"), "ID", L("Имя", "Ім’я", "Name"), L("Начислено, грн", "Нараховано, грн", "Charges, UAH"), L("Оплачено, грн", "Сплачено, грн", "Payments, UAH"), L("Остаток, грн", "Залишок, грн", "Balance, UAH"));
            row = 2;
            foreach (var m in members) BalanceRow(memberBalanceSheet, row++, m.Id, m.FullName, memberCharges.GetValueOrDefault(m.Id), memberPayments.GetValueOrDefault(m.Id));
            BalanceRow(memberBalanceSheet, row, null, L("Без участника", "Без учасника", "No member"), charges.Where(x => x.MemberId == null).Sum(x => x.Amount), payments.Where(x => x.MemberId == null).Sum(x => x.Amount));
            var plotBalanceSheet = Sheet(workbook, L("Остатки участков", "Залишки ділянок", "Plot balances"), "ID", L("Номер участка", "Номер ділянки", "Plot number"), L("Начислено, грн", "Нараховано, грн", "Charges, UAH"), L("Оплачено, грн", "Сплачено, грн", "Payments, UAH"), L("Остаток, грн", "Залишок, грн", "Balance, UAH"));
            row = 2;
            foreach (var p in plots) BalanceRow(plotBalanceSheet, row++, p.Id, p.Number, plotCharges.GetValueOrDefault(p.Id), plotPayments.GetValueOrDefault(p.Id));
            BalanceRow(plotBalanceSheet, row, null, L("Без участка", "Без ділянки", "No plot"), charges.Where(x => x.PlotId == null).Sum(x => x.Amount), payments.Sum(x => x.Amount) - plotPayments.Values.Sum());
            foreach (var sheet in workbook.Worksheets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sheet.SheetView.FreezeRows(1);
                sheet.RangeUsed()!.SetAutoFilter();
                sheet.ColumnsUsed().Width = 22;
                sheet.Column(2).Width = sheet == info ? 90 : 36;
                sheet.RangeUsed()!.Style.Alignment.WrapText = true;
                sheet.RowsUsed().Height = 32;
            }
            info.Rows(5, 10).Height = 60;
            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return (IActionResult)File(output.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DachaHub-{association.Slug}-{capturedAt:yyyyMMdd-HHmmss}.xlsx");
        });
    }

    private static IXLWorksheet Sheet(XLWorkbook workbook, string name, params string[] headers)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8EFFA");
        return sheet;
    }

    private static void Row(IXLWorksheet sheet, int row, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            var cell = sheet.Cell(row, i + 1);
            switch (values[i])
            {
                case null: break;
                case decimal amount: cell.Value = amount; cell.Style.NumberFormat.Format = "#,##0.00"; break;
                case int number: cell.Value = number; break;
                // Explicit string values are never interpreted as workbook formulas.
                default: cell.Value = values[i]!.ToString() ?? string.Empty; break;
            }
        }
    }

    private static void BalanceRow(IXLWorksheet sheet, int row, int? id, string name, decimal charges, decimal payments) =>
        Row(sheet, row, id, name, charges, payments, charges - payments);
    private static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? string.Empty;
    private static string Yes(bool value) => value ? L("Да", "Так", "Yes") : L("Нет", "Ні", "No");
    private static string L(string ru, string uk, string en) => AppLocalizer.Get(ru, uk, en);
}
