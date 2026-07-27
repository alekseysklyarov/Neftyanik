using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class RestoreModel : OwnershipPageModelBase
{
    public RestoreModel(ApplicationDbContext dbContext)
        : base(dbContext)
    {
    }

    public PlotContextViewModel Plot { get; private set; } = new();

    public OwnershipContextViewModel Ownership { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(plotId, id, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        var ownership = await DbContext.PlotOwnerships
            .Include(item => item.Member)
            .FirstOrDefaultAsync(item => item.Id == id && item.PlotId == plotId, cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        Ownership = new OwnershipContextViewModel
        {
            Id = ownership.Id,
            PlotId = ownership.PlotId,
            MemberId = ownership.MemberId,
            MemberFullName = ownership.Member?.FullName ?? "—",
            OwnershipShare = ownership.OwnershipShare,
            IsPrimaryContact = ownership.IsPrimaryContact,
            ValidFrom = ownership.ValidFrom,
            ValidTo = ownership.ValidTo
        };

        if (!ownership.ValidTo.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Эта запись владения уже является активной.");
            return Page();
        }

        if (await HasDuplicateActiveOwnershipAsync(plotId, ownership.MemberId, ownership.Id, cancellationToken))
        {
            ModelState.AddModelError(string.Empty, "Нельзя возобновить владение, потому что у этого члена товарищества уже есть активная запись по данному участку.");
        }

        var existingTotalShare = await GetSpecifiedActiveOwnershipShareTotalAsync(plotId, ownership.Id, cancellationToken);
        ValidateTotalShare(existingTotalShare, ownership.OwnershipShare);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);

        ownership.ValidTo = null;

        try
        {
            await DbContext.SaveChangesAsync(cancellationToken);

            if (ownership.IsPrimaryContact)
            {
                await ClearOtherPrimaryContactsAsync(plotId, ownership.Id, cancellationToken);
                await DbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateActiveOwnershipViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            ModelState.AddModelError(string.Empty, "Нельзя возобновить владение, потому что у этого члена товарищества уже есть активная запись по данному участку.");
            return Page();
        }

        TempData["SuccessMessage"] = "Владение участком успешно возобновлено.";
        return RedirectToPage("/Administration/Plots/Ownerships/Index", new { plotId });
    }

    private async Task<IActionResult> LoadPageAsync(int plotId, int id, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        var ownership = await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(item => item.Id == id && item.PlotId == plotId)
            .Select(item => new OwnershipContextViewModel
            {
                Id = item.Id,
                PlotId = item.PlotId,
                MemberId = item.MemberId,
                MemberFullName = item.Member != null ? item.Member.FullName : "—",
                OwnershipShare = item.OwnershipShare,
                IsPrimaryContact = item.IsPrimaryContact,
                ValidFrom = item.ValidFrom,
                ValidTo = item.ValidTo
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        Plot = plot;
        Ownership = ownership;
        return Page();
    }

    private static bool IsDuplicateActiveOwnershipViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }

    public sealed class OwnershipContextViewModel
    {
        public int Id { get; init; }

        public int PlotId { get; init; }

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public decimal? OwnershipShare { get; init; }

        public bool IsPrimaryContact { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }
    }
}
