using Neftyanik.Portal.Domain.Enums;
namespace Neftyanik.Portal.Application.Payments;

public interface IPaymentService
{
    Task<CreateMemberPaymentResult> CreateMemberPaymentAsync(CreateMemberPaymentRequest request, CancellationToken cancellationToken = default);

    Task<CancelPaymentResult> CancelPaymentAsync(CancelPaymentRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreateMemberPaymentRequest(
    int MemberId,
    int? PaymentPlotId,
    DateOnly PaymentDate,
    decimal Amount,
    Neftyanik.Portal.Domain.Enums.PaymentMethod PaymentMethod,
    string? ReferenceNumber,
    string? Description,
    string? CreatedByUserId,
    long? SourcePaymentNotificationId = null);

public enum CreateMemberPaymentResultCode
{
    Success = 0,
    InvalidAmount,
    InvalidPaymentMethod,
    NoEligiblePlots,
    PaymentPlotNotOwnedByMember
}

public sealed record CreateMemberPaymentResult(
    CreateMemberPaymentResultCode Code,
    long? PaymentId,
    decimal AllocatedAmount,
    decimal AdvanceAmount)
{
    public bool Succeeded => Code == CreateMemberPaymentResultCode.Success;

    public static CreateMemberPaymentResult Success(long paymentId, decimal allocatedAmount, decimal advanceAmount)
        => new(CreateMemberPaymentResultCode.Success, paymentId, allocatedAmount, advanceAmount);

    public static CreateMemberPaymentResult Failure(CreateMemberPaymentResultCode code)
        => new(code, null, 0m, 0m);
}

public sealed record CancelPaymentRequest(
    long PaymentId,
    string? CancellationReason);

public enum CancelPaymentResultCode
{
    Success = 0,
    NotFound,
    AlreadyCancelled,
    InvalidCancellationReason
}

public sealed record CancelPaymentResult(
    CancelPaymentResultCode Code)
{
    public bool Succeeded => Code == CancelPaymentResultCode.Success;

    public static CancelPaymentResult Success()
        => new(CancelPaymentResultCode.Success);

    public static CancelPaymentResult Failure(CancelPaymentResultCode code)
        => new(code);
}
