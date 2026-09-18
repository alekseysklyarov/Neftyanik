namespace Neftyanik.Portal.Application.Associations;

public interface IAssociationMembershipService
{
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken = default);
}
