namespace Neftyanik.Portal.Application.Identity;

public interface IPlatformLegacyInitialization
{
    Task<PlatformBootstrapResult> InitializeAsync(string login, string email, string temporaryPassword,
        string operatorIdentity, bool ownerConfirmed, CancellationToken cancellationToken = default);
}
