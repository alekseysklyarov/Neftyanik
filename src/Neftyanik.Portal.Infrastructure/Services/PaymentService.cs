using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    private const int PaymentCancellationReasonMaxLength = 500;
    private readonly ApplicationDbContext _dbContext;
    private readonly IFinancialAuditService _financialAuditService;

    public PaymentService(ApplicationDbContext dbContext, IFinancialAuditService financialAuditService)
    {
        _dbContext = dbContext;
        _financialAuditService = financialAuditService;
    }

    public async Task<CreateMemberPaymentResult> CreateMemberPaymentAsync(CreateMemberPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0m)
        {
            return CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.InvalidAmount);
        }

        if (!Enum.IsDefined(request.PaymentMethod) || !PaymentMethodRules.IsAllowed(request.PaymentMethod))
        {
            return CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.InvalidPaymentMethod);
        }

        var paymentDate = request.PaymentDate;
        var memberPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.MemberId == request.MemberId
                && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= paymentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= paymentDate))
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (memberPlotIds.Length == 0)
        {
            return CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.NoEligiblePlots);
        }

        if (request.PaymentPlotId.HasValue && !memberPlotIds.Contains(request.PaymentPlotId.Value))
        {
            return CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.PaymentPlotNotOwnedByMember);
        }

        var effectivePlotId = request.PaymentPlotId ?? memberPlotIds.OrderBy(plotId => plotId).First();

        var payment = new Payment
        {
            PlotId = effectivePlotId,
            PaymentDate = paymentDate,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            ReferenceNumber = Normalize(request.ReferenceNumber),
            Description = Normalize(request.Description),
            CreatedByUserId = Normalize(request.CreatedByUserId),
            CreatedAtUtc = DateTime.UtcNow
        };

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _dbContext.Payments.Add(payment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var outstandingCharges = await LoadOutstandingChargesAsync(memberPlotIds, cancellationToken);
            var remainingPaymentAmount = payment.Amount;
            var allocations = new List<PaymentAllocation>();

            foreach (var charge in outstandingCharges)
            {
                if (remainingPaymentAmount <= 0m)
                {
                    break;
                }

                var remainingChargeAmount = charge.Amount - charge.AllocatedAmount;
                if (remainingChargeAmount <= 0m)
                {
                    continue;
                }

                var allocationAmount = Math.Min(remainingPaymentAmount, remainingChargeAmount);
                allocations.Add(new PaymentAllocation
                {
                    PaymentId = payment.Id,
                    ChargeId = charge.Id,
                    Amount = allocationAmount
                });

                remainingPaymentAmount -= allocationAmount;
            }

            if (allocations.Count > 0)
            {
                _dbContext.PaymentAllocations.AddRange(allocations);
            }

            _financialAuditService.Add(
                FinancialAuditLogActions.Created,
                nameof(Payment),
                payment.Id.ToString(),
                request.SourcePaymentNotificationId.HasValue
                    ? $"Платеж #{payment.Id} создан из уведомления о платеже #{request.SourcePaymentNotificationId.Value}."
                    : $"Создан платеж #{payment.Id}.",
                newValues: new
                {
                    PaymentId = payment.Id,
                    request.MemberId,
                    payment.PlotId,
                    payment.PaymentDate,
                    payment.Amount,
                    PaymentMethod = payment.PaymentMethod.ToString(),
                    payment.ReferenceNumber,
                    payment.Description,
                    request.SourcePaymentNotificationId,
                    Allocations = allocations.Select(allocation => new
                    {
                        allocation.ChargeId,
                        allocation.Amount
                    }).ToArray()
                });

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            var allocatedAmount = payment.Amount - remainingPaymentAmount;
            return CreateMemberPaymentResult.Success(payment.Id, allocatedAmount, remainingPaymentAmount);
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

    public async Task<CancelPaymentResult> CancelPaymentAsync(CancelPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var cancellationReason = Normalize(request.CancellationReason);
        if (string.IsNullOrWhiteSpace(cancellationReason) || cancellationReason.Length > PaymentCancellationReasonMaxLength)
        {
            return CancelPaymentResult.Failure(CancelPaymentResultCode.InvalidCancellationReason);
        }

        var payment = await _dbContext.Payments
            .Include(item => item.PaymentAllocations)
            .Include(item => item.PaymentNotification)
            .FirstOrDefaultAsync(item => item.Id == request.PaymentId, cancellationToken);

        if (payment is null)
        {
            return CancelPaymentResult.Failure(CancelPaymentResultCode.NotFound);
        }

        if (payment.CancelledAtUtc.HasValue)
        {
            return CancelPaymentResult.Failure(CancelPaymentResultCode.AlreadyCancelled);
        }

        var oldValues = CreateAuditValues(payment);

        payment.CancelledAtUtc = DateTime.UtcNow;
        payment.CancellationReason = cancellationReason;

        var newValues = CreateAuditValues(payment);

        _financialAuditService.Add(
            FinancialAuditLogActions.Cancelled,
            nameof(Payment),
            payment.Id.ToString(),
            $"Отменен платеж #{payment.Id}.",
            oldValues,
            newValues);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return CancelPaymentResult.Success();
    }

    private async Task<IReadOnlyList<OutstandingChargeViewModel>> LoadOutstandingChargesAsync(int[] plotIds, CancellationToken cancellationToken)
    {
        if (plotIds.Length == 0)
        {
            return [];
        }

        var charges = await _dbContext.Charges
            .AsNoTracking()
            .Where(charge => charge.CancelledAtUtc == null
                && charge.PlotId.HasValue
                && plotIds.Contains(charge.PlotId.Value))
            .OrderBy(charge => charge.ChargeDate)
            .ThenBy(charge => charge.Id)
            .Select(charge => new OutstandingChargeViewModel
            {
                Id = charge.Id,
                Amount = charge.Amount
            })
            .ToListAsync(cancellationToken);

        if (charges.Count == 0)
        {
            return charges;
        }

        var chargeIds = charges.Select(charge => charge.Id).ToArray();
        var allocatedAmountsByCharge = (await _dbContext.PaymentAllocations
            .AsNoTracking()
            .Where(allocation => chargeIds.Contains(allocation.ChargeId)
                && allocation.Payment != null
                && allocation.Payment.CancelledAtUtc == null)
            .Select(allocation => new
            {
                allocation.ChargeId,
                allocation.Amount
            })
            .ToListAsync(cancellationToken))
            .GroupBy(allocation => allocation.ChargeId)
            .ToDictionary(group => group.Key, group => group.Sum(allocation => allocation.Amount));

        return charges
            .Select(charge => charge with
            {
                AllocatedAmount = allocatedAmountsByCharge.GetValueOrDefault(charge.Id)
            })
            .ToList();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static object CreateAuditValues(Payment payment)
    {
        return new
        {
            PaymentId = payment.Id,
            payment.PlotId,
            payment.PaymentDate,
            payment.Amount,
            PaymentMethod = payment.PaymentMethod.ToString(),
            payment.ReferenceNumber,
            payment.Description,
            payment.CreatedByUserId,
            payment.CancelledAtUtc,
            payment.CancellationReason,
            SourcePaymentNotificationId = payment.PaymentNotification?.Id,
            Allocations = payment.PaymentAllocations
                .OrderBy(allocation => allocation.ChargeId)
                .ThenBy(allocation => allocation.Id)
                .Select(allocation => new
                {
                    allocation.ChargeId,
                    allocation.Amount
                })
                .ToArray()
        };
    }

    private sealed record OutstandingChargeViewModel
    {
        public long Id { get; init; }

        public decimal Amount { get; init; }

        public decimal AllocatedAmount { get; init; }
    }
}
