using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    private readonly ApplicationDbContext _dbContext;

    public PaymentService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

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

    private sealed record OutstandingChargeViewModel
    {
        public long Id { get; init; }

        public decimal Amount { get; init; }

        public decimal AllocatedAmount { get; init; }
    }
}
