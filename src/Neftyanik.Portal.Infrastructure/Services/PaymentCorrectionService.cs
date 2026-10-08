using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PaymentCorrectionService(ApplicationDbContext db, IFinancialAuditService audit)
{
    public static Task<long?> LatestActivePaymentIdAsync(ApplicationDbContext db, int memberId, CancellationToken ct = default) =>
        db.Payments.AsNoTracking().Where(p => p.MemberId == memberId && p.CancelledAtUtc == null)
            .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id)
            .Select(p => (long?)p.Id).FirstOrDefaultAsync(ct);

    public static string Version(Payment payment) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Snapshot(payment)))));

    public async Task<string?> CorrectAsync(int memberId, long paymentId, decimal amount, PaymentMethod method,
        string? reference, string? description, string reason, string expectedVersion, CancellationToken ct = default)
    {
        if (amount <= 0 || amount > 9999999999999999.99m || decimal.Round(amount, 2) != amount)
            return "Укажите положительную сумму с точностью до копеек.";
        if (!PaymentMethodRules.IsAllowed(method)) return "Выберите наличные или перевод на карту.";
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) return "Укажите причину исправления (до 500 символов).";
        reference = Normalize(reference); description = Normalize(description);
        if (reference?.Length > 150 || description?.Length > 1000) return "Слишком длинный номер документа или описание.";

        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(ct) : null;
        await AdvancePaymentAllocator.LockAsync(db, ct);
        var payment = await db.Payments.Include(p => p.PaymentAllocations).ThenInclude(a => a.Charge)
            .SingleOrDefaultAsync(p => p.Id == paymentId && p.MemberId == memberId, ct);
        if (payment is null) return "Платёж участника не найден.";
        if (payment.CancelledAtUtc is not null || payment.Id != await LatestActivePaymentIdAsync(db, memberId, ct))
            return "Можно исправить только последний активный платёж участника.";
        if (Version(payment) != expectedVersion) return "Платёж или его распределение изменились. Обновите страницу и повторите исправление.";
        if (payment.Amount == amount && payment.PaymentMethod == method && payment.ReferenceNumber == reference && payment.Description == description)
            return "Данные платежа не изменились.";

        var before = Snapshot(payment);
        var difference = payment.Amount - amount;
        payment.Amount = amount; payment.PaymentMethod = method;
        payment.ReferenceNumber = reference; payment.Description = description;
        if (payment.BalanceBeforePayment.HasValue) payment.BalanceAfterPayment = payment.BalanceBeforePayment.Value - amount;

        if (difference != 0)
        {
            // Retain the original allocation priority; trim the newest allocations first.
            var remaining = amount;
            foreach (var allocation in payment.PaymentAllocations.Where(a => a.Charge?.CancelledAtUtc is null).OrderBy(a => a.Id).ToArray())
            {
                if (remaining == 0)
                {
                    db.PaymentAllocations.Remove(allocation);
                    payment.PaymentAllocations.Remove(allocation);
                    continue;
                }
                allocation.Amount = Math.Min(allocation.Amount, remaining);
                remaining -= allocation.Amount;
            }
            await db.SaveChangesAsync(ct);
            var plotIds = await db.LoadMemberFinancePlotIdsAsync(memberId, ct);
            var outstanding = await db.LoadOutstandingPaymentChargesAsync(plotIds, ct, memberId);
            foreach (var charge in outstanding)
            {
                if (remaining <= 0) break;
                var allocated = Math.Min(remaining, charge.OutstandingAmount);
                db.PaymentAllocations.Add(new PaymentAllocation { PaymentId = payment.Id, ChargeId = charge.Id, Amount = allocated });
                remaining -= allocated;
            }
            await db.SaveChangesAsync(ct);
            await AdvancePaymentAllocator.ApplyAsync(db, audit, outstanding.Select(c => c.Id), ct);

            // Later receipts captured a balance that included the original payment amount.
            var later = await db.Payments.Where(p => p.MemberId == memberId && p.CancelledAtUtc == null
                && (p.CreatedAtUtc > payment.CreatedAtUtc || (p.CreatedAtUtc == payment.CreatedAtUtc && p.Id > payment.Id))).ToListAsync(ct);
            foreach (var item in later)
            {
                var oldBalance = new { item.BalanceBeforePayment, item.BalanceAfterPayment };
                item.BalanceBeforePayment += difference;
                item.BalanceAfterPayment += difference;
                audit.Add(FinancialAuditLogActions.Updated, nameof(Payment), item.Id.ToString(),
                    $"Уточнён баланс в платёжке после исправления платежа #{payment.Id}.", oldBalance,
                    new { item.BalanceBeforePayment, item.BalanceAfterPayment });
            }
        }
        audit.Add(FinancialAuditLogActions.Updated, nameof(Payment), payment.Id.ToString(),
            "Исправление платежа: " + reason.Trim(), before, Snapshot(payment));
        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static object Snapshot(Payment payment) => new
    {
        payment.Id, payment.MemberId, payment.PlotId, payment.PaymentDate, payment.Amount, payment.PaymentMethod,
        payment.ReferenceNumber, payment.Description, payment.CreatedAtUtc, payment.CreatedByUserId,
        payment.BalanceBeforePayment, payment.BalanceAfterPayment, payment.CancelledAtUtc,
        Allocations = payment.PaymentAllocations.OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.ChargeId, a.Amount, ChargeCancelledAtUtc = a.Charge?.CancelledAtUtc }).ToArray()
    };
}
