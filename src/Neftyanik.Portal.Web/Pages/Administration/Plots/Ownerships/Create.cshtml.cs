using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots.Ownerships;

public class CreateModel : OwnershipPageModelBase
{
    public CreateModel(ApplicationDbContext dbContext)
        : base(dbContext)
    {
    }

    [BindProperty]
    public OwnershipInputModel Input { get; set; } = new();

    public PlotContextViewModel Plot { get; private set; } = new();

    public IReadOnlyList<SelectListItem> MemberOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int plotId, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(plotId, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;

        if (Input.MemberId is null)
        {
            ModelState.AddModelError("Input.MemberId", "Выберите члена товарищества.");
        }
        else
        {
            if (!await IsActiveMemberAsync(Input.MemberId.Value, cancellationToken))
            {
                ModelState.AddModelError("Input.MemberId", "Можно выбрать только активного члена товарищества.");
            }

            if (await HasDuplicateActiveOwnershipAsync(plotId, Input.MemberId.Value, null, cancellationToken))
            {
                ModelState.AddModelError("Input.MemberId", "Этот член товарищества уже является активным владельцем данного участка.");
            }
        }

        var existingTotalShare = await GetSpecifiedActiveOwnershipShareTotalAsync(plotId, null, cancellationToken);
        ValidateTotalShare(existingTotalShare, Input.OwnershipShare);

        if (!ModelState.IsValid)
        {
            MemberOptions = await GetMemberOptionsAsync(plotId, Input.MemberId, cancellationToken);
            return Page();
        }

        await using var transaction = await DbContext.Database.BeginTransactionAsync(cancellationToken);

        var ownership = new PlotOwnership
        {
            PlotId = plotId,
            MemberId = Input.MemberId!.Value,
            OwnershipShare = Input.OwnershipShare,
            IsPrimaryContact = Input.IsPrimaryContact,
            ValidFrom = Input.ValidFrom,
            CreatedAtUtc = DateTime.UtcNow
        };

        DbContext.PlotOwnerships.Add(ownership);

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
            ModelState.AddModelError("Input.MemberId", "Этот член товарищества уже является активным владельцем данного участка.");
            MemberOptions = await GetMemberOptionsAsync(plotId, Input.MemberId, cancellationToken);
            return Page();
        }

        TempData["SuccessMessage"] = "Владелец успешно добавлен к участку.";
        return RedirectToPage("/Administration/Plots/Ownerships/Index", new { plotId });
    }

    private async Task<IActionResult> LoadPageAsync(int plotId, CancellationToken cancellationToken)
    {
        var plot = await GetPlotContextAsync(plotId, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        Plot = plot;
        MemberOptions = await GetMemberOptionsAsync(plotId, Input.MemberId, cancellationToken);
        return Page();
    }

    private static bool IsDuplicateActiveOwnershipViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
