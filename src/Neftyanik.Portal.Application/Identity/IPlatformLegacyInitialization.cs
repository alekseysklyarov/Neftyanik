namespace Neftyanik.Portal.Application.Identity;

public interface IPlatformLegacyInitialization
{
    Task<PlatformBootstrapResult> InitializeAsync(string login, string email, string temporaryPassword,
        string operatorIdentity, string approvalReference, CancellationToken cancellationToken = default);
}
