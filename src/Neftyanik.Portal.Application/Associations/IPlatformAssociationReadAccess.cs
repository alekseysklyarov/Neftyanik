namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformAssociationReadAccess
{
    Task EnsureAllowedAsync(CancellationToken cancellationToken = default);
}
