using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

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

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            await AdvancePaymentAllocator.LockAsync(_dbContext, cancellationToken);
            var outstandingCharges = await _dbContext.LoadOutstandingPaymentChargesAsync(memberPlotIds, cancellationToken);
            if (request.PriorityChargeTypeId.HasValue
                && !outstandingCharges.Any(c => c.ChargeTypeId == request.PriorityChargeTypeId.Value))
            {
                return CreateMemberPaymentResult.Failure(CreateMemberPaymentResultCode.InvalidPaymentPriority);
            }

            var balanceBeforePayment = await _dbContext.CalculateActiveBalanceAsync(request.MemberId, memberPlotIds, cancellationToken);

            var payment = new Payment
            {
                MemberId = request.MemberId,
                PlotId = effectivePlotId,
                PaymentDate = paymentDate,
                Amount = request.Amount,
                BalanceBeforePayment = balanceBeforePayment,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = Normalize(request.ReferenceNumber),
                Description = Normalize(request.Description),
                CreatedByUserId = Normalize(request.CreatedByUserId),
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Payments.Add(payment);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var remainingPaymentAmount = payment.Amount;
            var allocations = new List<PaymentAllocation>();

            foreach (var charge in outstandingCharges
                .OrderByDescending(c => request.PriorityChargeTypeId.HasValue && c.ChargeTypeId == request.PriorityChargeTypeId.Value)
                .ThenBy(c => c.ChargeDate).ThenBy(c => c.Id))
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

            payment.BalanceAfterPayment = await _dbContext.CalculateActiveBalanceAsync(request.MemberId, memberPlotIds, cancellationToken);

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
                    payment.MemberId,
                    payment.PlotId,
                    payment.PaymentDate,
                    payment.Amount,
                    payment.BalanceBeforePayment,
                    payment.BalanceAfterPayment,
                    PaymentMethod = payment.PaymentMethod.ToString(),
                    payment.ReferenceNumber,
                    payment.Description,
                    request.SourcePaymentNotificationId,
                    request.PriorityChargeTypeId,
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

        await using var transaction = _dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;
        await AdvancePaymentAllocator.LockAsync(_dbContext, cancellationToken);

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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return CancelPaymentResult.Success();
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
            payment.MemberId,
            payment.PlotId,
            payment.PaymentDate,
            payment.Amount,
            payment.BalanceBeforePayment,
            payment.BalanceAfterPayment,
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

}
