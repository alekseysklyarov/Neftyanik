namespace Neftyanik.Portal.Application.Identity;

public enum PlatformBootstrapResult
{
    Created,
    AlreadyProvisioned,
    AccountExists,
    InvalidInput,
    Failed
}

public enum PlatformPasswordChangeResult
{
    Succeeded,
    Denied,
    InvalidPassword,
    Failed
}

public interface IPlatformAdministratorOnboarding
{
    Task<PlatformBootstrapResult> BootstrapAsync(string login, string email, string temporaryPassword, CancellationToken cancellationToken = default);
    Task<bool> CanChangePasswordAsync(string userId, string securityStamp, CancellationToken cancellationToken = default);
    Task<PlatformPasswordChangeResult> ChangePasswordAsync(string userId, string securityStamp, string currentPassword, string newPassword, CancellationToken cancellationToken = default);
}
