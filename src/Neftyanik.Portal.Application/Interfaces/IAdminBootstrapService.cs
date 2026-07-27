namespace Neftyanik.Portal.Application.Interfaces;

using Neftyanik.Portal.Application.Identity;

public interface IAdminBootstrapService
{
    Task<AdminBootstrapResult> CreateAdministratorAsync(AdminBootstrapRequest request, CancellationToken cancellationToken = default);
}
