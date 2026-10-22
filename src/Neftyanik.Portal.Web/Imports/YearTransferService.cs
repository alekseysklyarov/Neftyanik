using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Services;
using static Neftyanik.Portal.Web.Imports.YearWorkbook;

namespace Neftyanik.Portal.Web.Imports;

public sealed class YearTransferService(ApplicationDbContext db, IFinancialAuditService audit,
    IMemberElectricityService memberElectricity, IAssociationElectricityService associationElectricity)
{
    private sealed record Marker(string Key, string Hash, long EntityId);
    private sealed class RowFailure(int row, string message) : Exception(message) { public int Row { get; } = row; }
    private static decimal Normalize(decimal value) => decimal.Parse(value.ToString("G29", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    private static string Hash(YearTransferRow row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(row with
    { Row = 0, Key = "", Value = Normalize(row.Value), Night = row.Night.HasValue ? Normalize(row.Night.Value) : null,
        Second = row.Second.HasValue ? Normalize(row.Second.Value) : null, SecondNight = row.SecondNight.HasValue ? Normalize(row.SecondNight.Value) : null }))));
    private static string MarkerKey(string kind, string key) => "YearTransfer:" + kind + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    private static string LocalKey(string slug, string scope, long id) => $"portal:{slug}:{scope}:{id}";
    private static string Conflict => L("Запись уже существует с другими данными. Используйте исправление в портале.", "Запис уже існує з іншими даними. Використайте виправлення в порталі.", "The record already exists with different values. Use the portal's correction workflow.");

    public async Task<byte[]> ExportAsync(string kind, int year, CancellationToken ct)
    {
        if (!db.IsAssociationResolved || !ValidKind(kind) || !ValidYear(year)) throw new ArgumentException("Invalid transfer selection.");
        var rows = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var slug = await db.Associations.Where(a => a.Id == db.CurrentAssociationId).Select(a => a.Slug).SingleAsync(ct);
            var settings = await db.SystemSettings.AsNoTracking().Where(s => s.Key.StartsWith("YearTransfer:" + kind + ":")).Select(s => s.Value).ToListAsync(ct);
            var markers = settings.Select(s => JsonSerializer.Deserialize<Marker>(s)!).ToArray();
            string Key(string scope, long id) => markers.FirstOrDefault(m => m.EntityId == id)?.Key ?? LocalKey(slug, scope, id);
            var start = new DateOnly(year, 1, 1); var end = start.AddYears(1);
            var output = new List<YearTransferRow>();
            if (kind == Payments)
            {
                var payments = await db.Payments.AsNoTracking().Include(p => p.Member).Include(p => p.Plot)
                    .Where(p => p.PaymentDate >= start && p.PaymentDate < end).OrderBy(p => p.PaymentDate).ThenBy(p => p.Id).ToListAsync(ct);
                output.AddRange(payments.Select(p => PaymentRow(p, Key("payment", p.Id))));
            }
            else
            {
                var readings = await db.MemberElectricityReadings.AsNoTracking().Include(r => r.MemberElectricityMeter)!.ThenInclude(m => m!.Member)
                    .Include(r => r.MemberElectricityMeter)!.ThenInclude(m => m!.BillingPlot)
                    .Where(r => r.ReadingDate >= start && r.ReadingDate < end).OrderBy(r => r.ReadingDate).ThenBy(r => r.Id).ToListAsync(ct);
                // Scope prefixes disambiguate member/main readings with the same numeric database ID.
                string ReadingKey(string scope, long id) => markers.FirstOrDefault(m => m.EntityId == id && m.Hash.StartsWith(scope + ":", StringComparison.Ordinal))?.Key ?? LocalKey(slug, scope, id);
                output.AddRange(readings.Select(r => ReadingRow(r, ReadingKey("member", r.Id))));
                var common = await db.AssociationElectricityReadings.AsNoTracking().Where(r => r.ReadingDate >= start && r.ReadingDate < end).ToListAsync(ct);
                output.AddRange(common.Select(r => AssociationRow(r, ReadingKey("association", r.Id))));
                output = output.OrderBy(r => r.Date).ThenBy(r => r.Scope).ThenBy(r => r.Key).ToList();
            }
            await transaction.CommitAsync(ct); return output;
        });
        return Write(kind, year, rows);
    }

    public async Task<YearTransferResult> ExecuteAsync(string kind, int year, List<YearTransferRow> rows, string userId, bool save, CancellationToken ct)
    {
        if (!db.IsAssociationResolved || !ValidKind(kind) || !ValidYear(year)) throw new ArgumentException("Invalid transfer selection.");
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var result = new YearTransferResult { Rows = rows };
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                await AdvancePaymentAllocator.LockAsync(db, ct);
                var slug = await db.Associations.Where(a => a.Id == db.CurrentAssociationId).Select(a => a.Slug).SingleAsync(ct);
                if (rows.Count is 0 or > MaxRows || rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Count)
                    throw new RowFailure(0, L("Проверьте количество строк и уникальность Key.", "Перевірте кількість рядків та унікальність Key.", "Check the row count and unique keys."));
                var payments = new PaymentService(db, audit);
                foreach (var inputRow in rows.OrderBy(r => r.Date).ThenBy(r => r.Scope).ThenBy(r => r.Row))
                {
                    var row = await CanonicalizeAsync(inputRow, kind, ct);
                    if (row.Date.Year != year) throw new RowFailure(row.Row, L("Дата вне выбранного года.", "Дата поза обраним роком.", "Date is outside the selected year."));
                    var scope = kind == Payments ? "payment" : row.Scope;
                    var contentHash = Hash(row); var storedHash = kind == Payments ? contentHash : scope + ":" + contentHash;
                    var markerKey = MarkerKey(kind, row.Key);
                    var setting = await db.SystemSettings.SingleOrDefaultAsync(s => s.Key == markerKey, ct);
                    var marker = setting is null ? null : JsonSerializer.Deserialize<Marker>(setting.Value);
                    if (marker is not null && marker.Hash != storedHash) throw new RowFailure(row.Row, Conflict);
                    long? existingId = marker?.EntityId;
                    var ownPrefix = $"portal:{slug}:{scope}:";
                    if (!existingId.HasValue && row.Key.StartsWith(ownPrefix, StringComparison.Ordinal) && long.TryParse(row.Key[ownPrefix.Length..], out var localId)) existingId = localId;
                    long entityId;
                    decimal? amount = null;
                    var skipped = false;
                    if (kind == Payments)
                    {
                        var memberIds = await db.Members.AsNoTracking().Where(m => m.FullName == row.Member).Select(m => m.Id).ToArrayAsync(ct);
                        var plotIds = await db.Plots.AsNoTracking().Where(p => p.Number == row.Plot).Select(p => p.Id).ToArrayAsync(ct);
                        if (memberIds.Length != 1 || plotIds.Length != 1) throw new RowFailure(row.Row, L("Не найден однозначно участник или участок. Проверьте точные ФИО и номер.", "Не знайдено однозначно учасника або ділянку. Перевірте точні ПІБ та номер.", "Member or plot not uniquely found. Check the exact name and plot number."));
                        Payment? existing = null;
                        if (existingId.HasValue) existing = await db.Payments.AsNoTracking().Include(p => p.Member).Include(p => p.Plot).SingleOrDefaultAsync(p => p.Id == existingId, ct);
                        if (existingId.HasValue && (existing is null || Hash(PaymentRow(existing, row.Key)) != contentHash)) throw new RowFailure(row.Row, Conflict);
                        if (existing is null)
                        {
                            var candidates = await db.Payments.AsNoTracking().Include(p => p.Member).Include(p => p.Plot)
                                .Where(p => p.MemberId == memberIds[0] && p.PlotId == plotIds[0] && p.PaymentDate == row.Date).ToListAsync(ct);
                            existing = candidates.FirstOrDefault(p => Hash(PaymentRow(p, row.Key)) == contentHash);
                        }
                        if (existing is not null) { entityId = existing.Id; skipped = true; }
                        else
                        {
                            var created = await payments.CreateImportedMemberPaymentAsync(new CreateMemberPaymentRequest(memberIds[0], plotIds[0], row.Date, row.Value, row.Method,
                                row.Reference, row.Description, userId), ct);
                            if (!created.Succeeded) throw new RowFailure(row.Row, L("Платёж не принят: ", "Платіж не прийнято: ", "Payment rejected: ") + PaymentError(created.Code));
                            entityId = created.PaymentId!.Value;
                            if (row.Cancelled)
                            {
                                var cancelled = await payments.CancelPaymentAsync(new(entityId, row.CancellationReason), ct);
                                if (!cancelled.Succeeded) throw new RowFailure(row.Row, L("Не удалось сохранить отмену платежа.", "Не вдалося зберегти скасування платежу.", "Cannot preserve payment cancellation."));
                            }
                        }
                        amount = row.Cancelled || row.Method == Neftyanik.Portal.Domain.Enums.PaymentMethod.OpeningBalance ? 0 : row.Value;
                    }
                    else if (row.Scope == "association")
                    {
                        var existing = await db.AssociationElectricityReadings.AsNoTracking().SingleOrDefaultAsync(r => r.ReadingDate == row.Date, ct);
                        if (existingId.HasValue && (existing is null || existing.Id != existingId)) throw new RowFailure(row.Row, Conflict);
                        if (existing is not null)
                        {
                            if (Hash(AssociationRow(existing, row.Key)) != contentHash) throw new RowFailure(row.Row, Conflict);
                            entityId = existing.Id; skipped = true;
                        }
                        else
                        {
                            var created = row.Initial
                                ? await associationElectricity.CreateInitialReadingAsync(new(row.Date, row.Value, row.Night!.Value, userId), ct)
                                : await associationElectricity.CreateReadingAsync(new(row.Date, row.Value, row.Night!.Value, userId), ct);
                            if (!created.Succeeded) throw new RowFailure(row.Row, created.ErrorMessage ?? Conflict);
                            entityId = created.ReadingId!.Value; amount = created.TotalAmount;
                        }
                    }
                    else
                    {
                        var meters = await db.MemberElectricityMeters.AsNoTracking().Include(m => m.Member).Include(m => m.BillingPlot)
                            .Where(m => m.Member!.FullName == row.Member && m.BillingPlot!.Number == row.Plot && (row.Meter == "" || m.MeterNumber == row.Meter)).ToListAsync(ct);
                        if (meters.Count != 1) throw new RowFailure(row.Row, L("Счётчик не найден однозначно. Проверьте владельца, расчётный участок и номер счётчика.", "Лічильник не знайдено однозначно. Перевірте власника, розрахункову ділянку й номер лічильника.", "Meter not uniquely found. Check the member, billing plot and meter number."));
                        var meter = meters[0];
                        // Preserve an omitted meter number in manual files when comparing existing values.
                        var existing = await db.MemberElectricityReadings.AsNoTracking().Include(r => r.MemberElectricityMeter)!.ThenInclude(m => m!.Member)
                            .Include(r => r.MemberElectricityMeter)!.ThenInclude(m => m!.BillingPlot)
                            .SingleOrDefaultAsync(r => r.MemberElectricityMeterId == meter.Id && r.ReadingDate == row.Date, ct);
                        if (existingId.HasValue && (existing is null || existing.Id != existingId)) throw new RowFailure(row.Row, Conflict);
                        if (existing is not null)
                        {
                            if (Hash(ReadingRow(existing, row.Key) with { Meter = row.Meter }) != contentHash) throw new RowFailure(row.Row, Conflict);
                            entityId = existing.Id; skipped = true;
                        }
                        else
                        {
                            var created = row.Initial
                                ? await memberElectricity.CreateInitialReadingAsync(new(meter.Id, row.Date, row.Value, row.Night, userId), ct)
                                : await memberElectricity.CreateReadingAsync(new(meter.Id, row.Date, row.Value, row.Night, userId, SecondMeterReading: row.Second, SecondMeterNightReading: row.SecondNight), ct);
                            if (!created.Succeeded) throw new RowFailure(row.Row, created.ErrorMessage ?? Conflict);
                            entityId = created.ReadingId!.Value; amount = created.TotalAmount;
                        }
                    }
                    if (setting is null)
                        db.SystemSettings.Add(new SystemSetting { Key = markerKey, Value = JsonSerializer.Serialize(new Marker(row.Key, storedHash, entityId)), UpdatedByUserId = userId });
                    result.Lines.Add(new(row.Row, row.Key, $"{row.Date:yyyy-MM-dd} · {row.Member} · {row.Plot}", skipped, amount));
                    await db.SaveChangesAsync(ct);
                }
                if (save)
                {
                    audit.Add("ExcelImport", kind == Payments ? "YearPayments" : "YearReadings", year.ToString(),
                        $"Excel {year}: {result.Added} added; {result.Skipped} skipped.");
                    await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
                }
                else await transaction.RollbackAsync(ct);
            }
            catch (RowFailure ex)
            {
                await transaction.RollbackAsync(ct);
                result.Issues.Add(new(ex.Row, ex.Message));
            }
            finally { db.ChangeTracker.Clear(); }
            return result;
        });
    }

    private async Task<YearTransferRow> CanonicalizeAsync(YearTransferRow row, string kind, CancellationToken ct)
    {
        if (kind == Readings && row.Scope == "association") return row;
        var members = await db.Members.AsNoTracking().Where(m => m.FullName == row.Member).Select(m => m.FullName).ToListAsync(ct);
        var plots = await db.Plots.AsNoTracking().Where(p => p.Number == row.Plot).Select(p => p.Number).ToListAsync(ct);
        if (members.Count != 1 || plots.Count != 1) throw new RowFailure(row.Row, L("Не найден однозначно участник или участок. Проверьте точные ФИО и номер.", "Не знайдено однозначно учасника або ділянку. Перевірте точні ПІБ та номер.", "Member or plot not uniquely found. Check the exact name and plot number."));
        row = row with { Member = members[0], Plot = plots[0] };
        if (kind == Readings)
        {
            var singleRate = await db.Members.AsNoTracking().Where(m => m.FullName == row.Member)
                .Select(m => m.ElectricityMeterType == Neftyanik.Portal.Domain.Enums.MemberElectricityMeterType.SingleRate).SingleAsync(ct);
            if (singleRate && (row.Night.HasValue || row.SecondNight.HasValue))
                throw new RowFailure(row.Row, L("Для однотарифного счётчика оставьте ночные показания пустыми.", "Для однотарифного лічильника залиште нічні показання порожніми.", "Leave night readings blank for a single-rate meter."));
            var meters = await db.MemberElectricityMeters.AsNoTracking().Where(m => m.Member!.FullName == row.Member && m.BillingPlot!.Number == row.Plot && (row.Meter == "" || m.MeterNumber == row.Meter))
                .Select(m => m.MeterNumber).ToListAsync(ct);
            if (meters.Count != 1) throw new RowFailure(row.Row, L("Уточните номер счётчика для владельца и участка.", "Уточніть номер лічильника для власника й ділянки.", "Specify a unique meter for the member and plot."));
            row = row with { Meter = meters[0] ?? "" };
        }
        return row;
    }

    private static string PaymentError(CreateMemberPaymentResultCode code) => code switch
    {
        CreateMemberPaymentResultCode.NoEligiblePlots or CreateMemberPaymentResultCode.PaymentPlotNotOwnedByMember => L("на дату платежа нет владения или долга по указанному участку.", "на дату платежу немає володіння або боргу за вказаною ділянкою.", "no ownership or debt for the plot on the payment date."),
        CreateMemberPaymentResultCode.InvalidAmount => L("неверная сумма.", "невірна сума.", "invalid amount."),
        _ => L("проверьте способ оплаты и данные.", "перевірте спосіб оплати й дані.", "check payment method and data.")
    };
    private static YearTransferRow PaymentRow(Payment p, string key) => new(0, key, p.PaymentDate, "", p.Member?.FullName ?? "", p.Plot?.Number ?? "", "", false,
        p.Amount, null, null, null, p.PaymentMethod, p.ReferenceNumber?.Trim() ?? "", p.Description?.Trim() ?? "", p.CancelledAtUtc.HasValue,
        p.CancelledAtUtc.HasValue ? p.CancellationReason?.Trim() ?? "Imported cancellation" : "");
    private static YearTransferRow ReadingRow(MemberElectricityReading r, string key)
    {
        var physical = r.IsInitialReading ? null : PhysicalMeterReadings.FromJson(r.PhysicalMeterReadingsJson);
        return new(0, key, r.ReadingDate, "member", r.MemberElectricityMeter?.Member?.FullName ?? "", r.MemberElectricityMeter?.BillingPlot?.Number ?? "",
            r.MemberElectricityMeter?.MeterNumber ?? "", r.IsInitialReading, physical?.First ?? r.CurrentReading,
            physical?.FirstNight ?? r.CurrentNightReading, physical?.Second, physical?.SecondNight, 0, "", "", false, "");
    }
    private static YearTransferRow AssociationRow(AssociationElectricityReading r, string key) => new(0, key, r.ReadingDate, "association", "", "", "", r.IsInitialReading,
        r.CurrentDayReading, r.CurrentNightReading, null, null, 0, "", "", false, "");
}
