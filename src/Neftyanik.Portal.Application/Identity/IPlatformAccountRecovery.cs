namespace Neftyanik.Portal.Application.Identity;

public interface IPlatformAccountRecovery
{
    Task RequestAsync(string email, bool confirmEmail, CancellationToken cancellationToken = default);
    Task<bool> ConfirmEmailAsync(string userId, string token, CancellationToken cancellationToken = default);
    Task<bool> ResetPasswordAsync(string userId, string token, string newPassword, CancellationToken cancellationToken = default);
}

public interface IPlatformEmailSender
{
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
