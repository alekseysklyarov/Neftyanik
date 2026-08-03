using Neftyanik.Portal.Domain.Enums;
namespace Neftyanik.Portal.Application.Payments;

public interface IPaymentNotificationService
{
    Task<PaymentNotificationOperationResult> CreateAsync(int memberId, CreatePaymentNotificationRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentNotificationListItem>> GetRecentForMemberAsync(int memberId, int limit = 5, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentNotificationListItem>> GetForAdministrationAsync(GetPaymentNotificationsForAdministrationRequest request, CancellationToken cancellationToken = default);

    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task<PaymentNotificationOperationResult> ConfirmAsync(ConfirmPaymentNotificationRequest request, CancellationToken cancellationToken = default);

    Task<PaymentNotificationOperationResult> RejectAsync(RejectPaymentNotificationRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreatePaymentNotificationRequest(
    decimal Amount,
    Neftyanik.Portal.Domain.Enums.PaymentMethod PaymentMethod,
    string? Description);

public sealed record GetPaymentNotificationsForAdministrationRequest(
    Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus? Status,
    int Limit = 100);

public sealed record ConfirmPaymentNotificationRequest(
    long NotificationId,
    DateOnly PaymentDate,
    int? PaymentPlotId,
    string ReviewedByUserId);

public sealed record RejectPaymentNotificationRequest(
    long NotificationId,
    string ReviewedByUserId,
    string? AdministratorComment);

public enum PaymentNotificationOperationResultCode
{
    Success = 0,
    NotFound,
    AlreadyProcessed,
    InvalidRequest,
    PaymentCreationFailed
}

public sealed record PaymentNotificationOperationResult(
    PaymentNotificationOperationResultCode Code,
    string? ErrorMessage,
    long? NotificationId = null,
    long? PaymentId = null)
{
    public bool Succeeded => Code == PaymentNotificationOperationResultCode.Success;

    public static PaymentNotificationOperationResult Success(long notificationId, long? paymentId = null)
        => new(PaymentNotificationOperationResultCode.Success, null, notificationId, paymentId);

    public static PaymentNotificationOperationResult Failure(PaymentNotificationOperationResultCode code, string errorMessage)
        => new(code, errorMessage);
}

public sealed record PaymentNotificationListItem(
    long Id,
    int MemberId,
    string MemberFullName,
    decimal Amount,
    Neftyanik.Portal.Domain.Enums.PaymentMethod PaymentMethod,
    string? Description,
    Neftyanik.Portal.Domain.Enums.PaymentNotificationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    string? ReviewedByUserId,
    string? ReviewedByUserDisplayName,
    string? AdministratorComment,
    long? PaymentId,
    IReadOnlyList<string> MemberPlotNumbers);
