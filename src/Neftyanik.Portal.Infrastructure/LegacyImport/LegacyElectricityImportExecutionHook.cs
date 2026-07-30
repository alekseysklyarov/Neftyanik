using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Infrastructure.LegacyImport;

internal interface ILegacyElectricityImportExecutionHook
{
    Task OnBeforeCommitAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken);
}

internal sealed class NoOpLegacyElectricityImportExecutionHook : ILegacyElectricityImportExecutionHook
{
    public Task OnBeforeCommitAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
