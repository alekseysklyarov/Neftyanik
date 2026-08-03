using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Neftyanik.Portal.Application.Payments;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.Services;

public sealed class PaymentNotificationService : IPaymentNotificationService
{
    private const int DefaultRecentLimit = 5;
    private const int MaxRecentLimit = 20;
    private const int DefaultAdministrationLimit = 100;
    private const int MaxAdministrationLimit = 200;

    private readonly ApplicationDbContext _dbContext;
    private readonly IPaymentService _paymentService;

    public PaymentNotificationService(ApplicationDbContext dbContext, IPaymentService paymentService)
    {
        _dbContext = dbContext;
        _paymentService = paymentService;
    }

    public async Task<PaymentNotificationOperationResult> CreateAsync(int memberId, CreatePaymentNotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (memberId <= 0)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.InvalidRequest, "Member id is invalid.");
        }

        var validationError = ValidateCreateRequest(request);
        if (validationError is not null)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.InvalidRequest, validationError);
        }

        var memberExists = await _dbContext.Members
            .AsNoTracking()
            .AnyAsync(member => member.Id == memberId, cancellationToken);

        if (!memberExists)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.NotFound, "Member was not found.");
        }

        var notification = new PaymentNotification
        {
            MemberId = memberId,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Description = Normalize(request.Description),
            Status = PaymentNotificationStatus.Pending,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.PaymentNotifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return PaymentNotificationOperationResult.Success(notification.Id);
    }

    public async Task<IReadOnlyList<PaymentNotificationListItem>> GetRecentForMemberAsync(int memberId, int limit = 5, CancellationToken cancellationToken = default)
    {
        if (memberId <= 0)
        {
            return [];
        }

        var boundedLimit = NormalizeRecentLimit(limit);

        var items = await _dbContext.PaymentNotifications
            .AsNoTracking()
            .Where(notification => notification.MemberId == memberId)
            .Select(notification => new PaymentNotificationListProjection(
                notification.Id,
                notification.MemberId,
                notification.Member != null ? notification.Member.FullName : string.Empty,
                notification.Amount,
                notification.PaymentMethod,
                notification.Description,
                notification.Status,
                notification.CreatedAtUtc,
                notification.ReviewedAtUtc,
                notification.ReviewedByUserId,
                notification.ReviewedByUser != null
                    ? (!string.IsNullOrWhiteSpace(notification.ReviewedByUser.DisplayName)
                        ? notification.ReviewedByUser.DisplayName
                        : notification.ReviewedByUser.Email ?? notification.ReviewedByUser.UserName ?? notification.ReviewedByUser.Id)
                    : null,
                notification.AdministratorComment,
                notification.PaymentId))
            .ToListAsync(cancellationToken);

        return items
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Take(boundedLimit)
            .Select(static notification => notification.ToListItem(Array.Empty<string>()))
            .ToList();
    }

    public async Task<IReadOnlyList<PaymentNotificationListItem>> GetForAdministrationAsync(GetPaymentNotificationsForAdministrationRequest request, CancellationToken cancellationToken = default)
    {
        var boundedLimit = NormalizeAdministrationLimit(request.Limit);

        var query = _dbContext.PaymentNotifications
            .AsNoTracking()
            .AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(notification => notification.Status == request.Status.Value);
        }

        var items = await query
            .Select(notification => new PaymentNotificationListProjection(
                notification.Id,
                notification.MemberId,
                notification.Member != null ? notification.Member.FullName : string.Empty,
                notification.Amount,
                notification.PaymentMethod,
                notification.Description,
                notification.Status,
                notification.CreatedAtUtc,
                notification.ReviewedAtUtc,
                notification.ReviewedByUserId,
                notification.ReviewedByUser != null
                    ? (!string.IsNullOrWhiteSpace(notification.ReviewedByUser.DisplayName)
                        ? notification.ReviewedByUser.DisplayName
                        : notification.ReviewedByUser.Email ?? notification.ReviewedByUser.UserName ?? notification.ReviewedByUser.Id)
                    : null,
                notification.AdministratorComment,
                notification.PaymentId))
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            return [];
        }

        var orderedItems = request.Status == PaymentNotificationStatus.Pending
            ? items.OrderBy(notification => notification.CreatedAtUtc).ThenBy(notification => notification.Id)
            : items.OrderByDescending(notification => notification.ReviewedAtUtc ?? notification.CreatedAtUtc).ThenByDescending(notification => notification.Id);

        var selectedItems = orderedItems
            .Take(boundedLimit)
            .ToList();

        var currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var memberIds = selectedItems.Select(item => item.MemberId).Distinct().ToArray();
        var plotNumbersByMemberId = (await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => memberIds.Contains(ownership.MemberId)
                && (!ownership.ValidFrom.HasValue || ownership.ValidFrom.Value <= currentDate)
                && (!ownership.ValidTo.HasValue || ownership.ValidTo.Value >= currentDate))
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new
            {
                ownership.MemberId,
                PlotNumber = ownership.Plot != null ? ownership.Plot.Number : null
            })
            .ToListAsync(cancellationToken))
            .Where(item => !string.IsNullOrWhiteSpace(item.PlotNumber))
            .GroupBy(item => item.MemberId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(item => item.PlotNumber!).Distinct(StringComparer.Ordinal).ToList());

        return selectedItems
            .Select(item => item.ToListItem(plotNumbersByMemberId.GetValueOrDefault(item.MemberId, Array.Empty<string>())))
            .ToList();
    }

    public Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.PaymentNotifications
            .AsNoTracking()
            .CountAsync(notification => notification.Status == PaymentNotificationStatus.Pending, cancellationToken);
    }

    public async Task<PaymentNotificationOperationResult> ConfirmAsync(ConfirmPaymentNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateConfirmRequest(request);
        if (validationError is not null)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.InvalidRequest, validationError);
        }

        IDbContextTransaction? transaction = null;
        if (_dbContext.Database.IsRelational() && _dbContext.Database.CurrentTransaction is null)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var notification = await _dbContext.PaymentNotifications
                .FirstOrDefaultAsync(item => item.Id == request.NotificationId, cancellationToken);

            if (notification is null)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.NotFound, "Payment notification was not found.");
            }

            if (notification.Status != PaymentNotificationStatus.Pending)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.AlreadyProcessed, "Payment notification has already been processed.");
            }

            var paymentResult = await _paymentService.CreateMemberPaymentAsync(
                new CreateMemberPaymentRequest(
                    notification.MemberId,
                    request.PaymentPlotId,
                    request.PaymentDate,
                    notification.Amount,
                    notification.PaymentMethod,
                    null,
                    notification.Description,
                    Normalize(request.ReviewedByUserId)),
                cancellationToken);

            if (!paymentResult.Succeeded || !paymentResult.PaymentId.HasValue)
            {
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return CreatePaymentFailureResult(paymentResult);
            }

            notification.Status = PaymentNotificationStatus.Confirmed;
            notification.PaymentId = paymentResult.PaymentId.Value;
            notification.ReviewedAtUtc = DateTimeOffset.UtcNow;
            notification.ReviewedByUserId = Normalize(request.ReviewedByUserId);
            notification.AdministratorComment = null;
            notification.ReviewVersion++;

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return PaymentNotificationOperationResult.Success(notification.Id, paymentResult.PaymentId.Value);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.AlreadyProcessed, "Payment notification has already been processed.");
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

    public async Task<PaymentNotificationOperationResult> RejectAsync(RejectPaymentNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var validationError = ValidateRejectRequest(request);
        if (validationError is not null)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.InvalidRequest, validationError);
        }

        try
        {
            var notification = await _dbContext.PaymentNotifications
                .FirstOrDefaultAsync(item => item.Id == request.NotificationId, cancellationToken);

            if (notification is null)
            {
                return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.NotFound, "Payment notification was not found.");
            }

            if (notification.Status != PaymentNotificationStatus.Pending)
            {
                return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.AlreadyProcessed, "Payment notification has already been processed.");
            }

            notification.Status = PaymentNotificationStatus.Rejected;
            notification.ReviewedAtUtc = DateTimeOffset.UtcNow;
            notification.ReviewedByUserId = Normalize(request.ReviewedByUserId);
            notification.AdministratorComment = Normalize(request.AdministratorComment);
            notification.ReviewVersion++;

            await _dbContext.SaveChangesAsync(cancellationToken);
            return PaymentNotificationOperationResult.Success(notification.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.AlreadyProcessed, "Payment notification has already been processed.");
        }
    }

    private static string? ValidateCreateRequest(CreatePaymentNotificationRequest request)
    {
        if (request.Amount <= 0m)
        {
            return "Payment notification amount must be greater than zero.";
        }

        if (!Enum.IsDefined(request.PaymentMethod) || !PaymentMethodRules.IsAllowed(request.PaymentMethod))
        {
            return "Payment notification method is invalid.";
        }

        if (!ValidateOptionalLength(request.Description, PaymentNotification.DescriptionMaxLength))
        {
            return $"Payment notification description must not exceed {PaymentNotification.DescriptionMaxLength} characters.";
        }

        return null;
    }

    private static string? ValidateConfirmRequest(ConfirmPaymentNotificationRequest request)
    {
        if (request.NotificationId <= 0)
        {
            return "Payment notification id is invalid.";
        }

        if (request.PaymentDate == default)
        {
            return "Payment date is required.";
        }

        if (string.IsNullOrWhiteSpace(request.ReviewedByUserId))
        {
            return "Reviewing user is required.";
        }

        return null;
    }

    private static string? ValidateRejectRequest(RejectPaymentNotificationRequest request)
    {
        if (request.NotificationId <= 0)
        {
            return "Payment notification id is invalid.";
        }

        if (string.IsNullOrWhiteSpace(request.ReviewedByUserId))
        {
            return "Reviewing user is required.";
        }

        if (!ValidateOptionalLength(request.AdministratorComment, PaymentNotification.AdministratorCommentMaxLength))
        {
            return $"Administrator comment must not exceed {PaymentNotification.AdministratorCommentMaxLength} characters.";
        }

        return null;
    }

    private static PaymentNotificationOperationResult CreatePaymentFailureResult(CreateMemberPaymentResult paymentResult)
    {
        return paymentResult.Code switch
        {
            CreateMemberPaymentResultCode.NoEligiblePlots => PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.PaymentCreationFailed, "The member has no eligible plots for payment registration."),
            CreateMemberPaymentResultCode.PaymentPlotNotOwnedByMember => PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.InvalidRequest, "The selected plot does not belong to the member on the payment date."),
            CreateMemberPaymentResultCode.InvalidAmount => PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.PaymentCreationFailed, "The payment amount is invalid."),
            CreateMemberPaymentResultCode.InvalidPaymentMethod => PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.PaymentCreationFailed, "The payment method is invalid."),
            _ => PaymentNotificationOperationResult.Failure(PaymentNotificationOperationResultCode.PaymentCreationFailed, "Failed to create a payment from the notification.")
        };
    }

    private static bool ValidateOptionalLength(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) || value.Trim().Length <= maxLength;
    }

    private static int NormalizeRecentLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultRecentLimit;
        }

        return Math.Min(limit, MaxRecentLimit);
    }

    private static int NormalizeAdministrationLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultAdministrationLimit;
        }

        return Math.Min(limit, MaxAdministrationLimit);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record PaymentNotificationListProjection(
        long Id,
        int MemberId,
        string MemberFullName,
        decimal Amount,
        PaymentMethod PaymentMethod,
        string? Description,
        PaymentNotificationStatus Status,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ReviewedAtUtc,
        string? ReviewedByUserId,
        string? ReviewedByUserDisplayName,
        string? AdministratorComment,
        long? PaymentId)
    {
        public PaymentNotificationListItem ToListItem(IReadOnlyList<string> memberPlotNumbers)
        {
            return new PaymentNotificationListItem(
                Id,
                MemberId,
                MemberFullName,
                Amount,
                PaymentMethod,
                Description,
                Status,
                CreatedAtUtc,
                ReviewedAtUtc,
                ReviewedByUserId,
                ReviewedByUserDisplayName,
                AdministratorComment,
                PaymentId,
                memberPlotNumbers);
        }
    }
}
