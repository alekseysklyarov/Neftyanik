namespace Neftyanik.Portal.Application.Electricity;

public enum MemberReadingDeletionResultCode
{
    Success,
    NotFound,
    NotLatest,
    InvalidReason,
    InvalidActor,
    ChargeCancellationFailed
}

public sealed record MemberReadingDeletionResult(MemberReadingDeletionResultCode Code)
{
    public bool Succeeded => Code == MemberReadingDeletionResultCode.Success;
}
