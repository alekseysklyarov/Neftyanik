namespace Neftyanik.Portal.Application.Associations;

public interface IPlatformOverviewReader
{
    Task<PlatformOverview> GetAsync(CancellationToken cancellationToken = default);
    Task<PlatformAssociationSetup?> GetSetupAsync(int associationId, CancellationToken cancellationToken = default);
}

public sealed record PlatformOverview(int TotalCount, int ActiveCount, int WithoutAdministratorCount,
    int AwaitingAdministratorCount, IReadOnlyList<PlatformAttentionItem> Attention);

public sealed record PlatformAttentionItem(int Id, string Name, string Slug, bool HasAvailableAdministrator);

// Only setup facts cross the platform boundary; no member details, tariff amounts or balances.
public sealed record PlatformAssociationSetup(bool HasContact, int AvailableAdministrators, int OnboardedAdministrators,
    int Members, int Plots, int CurrentOwnerships, bool HasChargeTypes, bool HasMemberTariff, bool HasSupplierTariff);
