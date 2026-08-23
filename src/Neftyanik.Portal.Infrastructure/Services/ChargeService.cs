using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class ChargeService : IChargeService
{
    private const int CancellationReasonMaxLength = 500;
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;

    public ChargeService(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
    }

    public async Task<CancelChargeResult> CancelChargeAsync(CancelChargeRequest request, CancellationToken cancellationToken = default)
    {
        var cancellationReason = Normalize(request.CancellationReason);
        if (string.IsNullOrWhiteSpace(cancellationReason) || cancellationReason.Length > CancellationReasonMaxLength)
        {
            return CancelChargeResult.Failure(CancelChargeResultCode.InvalidCancellationReason);
        }

        var charge = await _dbContext.Charges
            .Include(item => item.PaymentAllocations)
            .Include(item => item.MemberElectricityReading)
            .FirstOrDefaultAsync(item => item.Id == request.ChargeId, cancellationToken);

        if (charge is null)
        {
            return CancelChargeResult.Failure(CancelChargeResultCode.NotFound);
        }

        if (charge.CancelledAtUtc.HasValue)
        {
            return CancelChargeResult.Failure(CancelChargeResultCode.AlreadyCancelled);
        }

        var oldValues = CreateAuditValues(charge);

        charge.CancelledAtUtc = DateTime.UtcNow;
        charge.CancellationReason = cancellationReason;

        var newValues = CreateAuditValues(charge);

        _financialAuditService.Add(
            FinancialAuditLogActions.Cancelled,
            nameof(Charge),
            charge.Id.ToString(),
            $"Отменено начисление #{charge.Id}.",
            oldValues,
            newValues);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CancelChargeResult.Success();
    }

    private static object CreateAuditValues(Charge charge)
    {
        return new
        {
            ChargeId = charge.Id,
            charge.PlotId,
            charge.ChargeTypeId,
            charge.Amount,
            charge.ChargeDate,
            charge.DueDate,
            charge.Description,
            charge.CreatedByUserId,
            charge.CancelledAtUtc,
            charge.CancellationReason,
            MemberElectricityReadingId = charge.MemberElectricityReading?.Id,
            Allocations = charge.PaymentAllocations
                .OrderBy(allocation => allocation.PaymentId)
                .ThenBy(allocation => allocation.Id)
                .Select(allocation => new
                {
                    allocation.PaymentId,
                    allocation.Amount
                })
                .ToArray()
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
