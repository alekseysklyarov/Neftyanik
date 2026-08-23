namespace Neftyanik.Portal.Application.Finance
{
    public interface IChargeService
    {
        Task<CancelChargeResult> CancelChargeAsync(CancelChargeRequest request, CancellationToken cancellationToken = default);
    }

    public sealed record CancelChargeRequest(
        long ChargeId,
        string? CancellationReason);

    public enum CancelChargeResultCode
    {
        Success = 0,
        NotFound,
        AlreadyCancelled,
        InvalidCancellationReason
    }

    public sealed record CancelChargeResult(
        CancelChargeResultCode Code)
    {
        public bool Succeeded => Code == CancelChargeResultCode.Success;

        public static CancelChargeResult Success()
            => new(CancelChargeResultCode.Success);

        public static CancelChargeResult Failure(CancelChargeResultCode code)
            => new(code);
    }
}
