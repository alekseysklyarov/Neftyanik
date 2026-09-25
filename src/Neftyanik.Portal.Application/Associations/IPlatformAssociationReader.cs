namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformAssociationReader
{
    Task<PlatformAssociationPage> GetPageAsync(string? search = null, bool? isActive = null,
        int pageNumber = 1, CancellationToken cancellationToken = default);

    Task<PlatformAssociationDetails?> GetDetailsAsync(int associationId, CancellationToken cancellationToken = default);
}

public sealed record PlatformAssociationSummary(
    int Id, string Name, string Slug, bool IsActive, int MemberCount, int AdministratorCount);

public sealed record PlatformAssociationPage(
    IReadOnlyList<PlatformAssociationSummary> Items, int TotalCount, int PageNumber, int TotalPages);

public sealed record PlatformAssociationAdministrator(
    string? UserName, bool IsAccountActive, bool IsAccountLocked, bool IsAssignmentActive);

public sealed record PlatformAssociationDetails(
    int Id, string Name, string Slug, bool IsActive, DateTimeOffset CreatedAtUtc, int MemberCount,
    IReadOnlyList<PlatformAssociationAdministrator> Administrators,
    string? ContactEmail = null, string? ContactPhone = null, string? PostalAddress = null);
