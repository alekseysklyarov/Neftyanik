namespace Neftyanik.Portal.Application.Identity;

public enum PlatformOperatorPasswordResetResult
{
    Succeeded,
    Denied,
    Conflict,
    InvalidInput,
    InvalidPassword,
    Failed
}

public interface IPlatformAdministratorPasswordRecovery
{
    Task<string?> GetSecurityStampAsync(string exactLogin, CancellationToken cancellationToken = default);
    Task<PlatformOperatorPasswordResetResult> ResetAsync(string exactLogin, string expectedSecurityStamp,
        string temporaryPassword, string operatorIdentity, string approvalReference, CancellationToken cancellationToken = default);
}
