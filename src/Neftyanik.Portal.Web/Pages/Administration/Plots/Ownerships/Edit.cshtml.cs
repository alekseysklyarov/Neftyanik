using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class EditModel : OwnershipPageModelBase
{
    public EditModel(ApplicationDbContext dbContext)
        : base(dbContext)
    {
    }

    [BindProperty]
    public OwnershipInputModel Input { get; set; } = new();

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
            PlotNumber = plot.PlotNumber,
            MemberId = ownership.MemberId,
            MemberFullName = ownership.Member?.FullName ?? "—",
            MemberPhoneNumber = ownership.Member?.PhoneNumber,
            MemberEmail = ownership.Member?.Email,
            ValidTo = ownership.ValidTo,
            IsActive = ownership.ValidTo == null
        };

        ValidateDateRange(Input.ValidFrom, ownership.ValidTo);

        if (ownership.ValidTo == null)
        {
            var existingTotalShare = await GetSpecifiedActiveOwnershipShareTotalAsync(plotId, ownership.Id, cancellationToken);
            ValidateTotalShare(existingTotalShare, Input.OwnershipShare);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        await using var transaction = ownership.ValidTo == null && Input.IsPrimaryContact
            ? await DbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        ownership.OwnershipShare = Input.OwnershipShare;
        ownership.IsPrimaryContact = Input.IsPrimaryContact;
        ownership.ValidFrom = Input.ValidFrom;

        await DbContext.SaveChangesAsync(cancellationToken);

        if (ownership.ValidTo == null && ownership.IsPrimaryContact)
        {
            await ClearOtherPrimaryContactsAsync(plotId, ownership.Id, cancellationToken);
            await DbContext.SaveChangesAsync(cancellationToken);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        TempData["SuccessMessage"] = "Изменения по владению сохранены.";
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
                PlotNumber = item.Plot != null ? item.Plot.Number : string.Empty,
                MemberId = item.MemberId,
                MemberFullName = item.Member != null ? item.Member.FullName : "—",
                MemberPhoneNumber = item.Member != null ? item.Member.PhoneNumber : null,
                MemberEmail = item.Member != null ? item.Member.Email : null,
                ValidTo = item.ValidTo,
                IsActive = item.ValidTo == null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (ownership is null)
        {
            return NotFound();
        }

        var input = await DbContext.PlotOwnerships
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new OwnershipInputModel
            {
                MemberId = item.MemberId,
                OwnershipShare = item.OwnershipShare,
                IsPrimaryContact = item.IsPrimaryContact,
                ValidFrom = item.ValidFrom
            })
            .FirstAsync(cancellationToken);

        Plot = plot;
        Ownership = ownership;
        Input = input;
        return Page();
    }

    public sealed class OwnershipContextViewModel
    {
        public int Id { get; init; }

        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public int MemberId { get; init; }

        public string MemberFullName { get; init; } = string.Empty;

        public string? MemberPhoneNumber { get; init; }

        public string? MemberEmail { get; init; }

        public DateOnly? ValidTo { get; init; }

        public bool IsActive { get; init; }
    }
}
