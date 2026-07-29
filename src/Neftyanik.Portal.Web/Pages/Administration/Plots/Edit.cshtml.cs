using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.Administrator)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public EditModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public PlotInputModel Input { get; set; } = new();

    public int PlotId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var plot = await _dbContext.Plots
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (plot is null)
        {
            return NotFound();
        }

        PlotId = plot.Id;
        Input = new PlotInputModel
        {
            Number = plot.Number,
            Address = plot.Address,
            AreaSquareMeters = plot.AreaSquareMeters,
            CadastralNumber = plot.CadastralNumber,
            Notes = plot.Notes,
            IsActive = plot.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        PlotId = id;

        if (!string.IsNullOrWhiteSpace(Input.Number) && await PlotNumberExistsAsync(id, cancellationToken))
        {
            ModelState.AddModelError("Input.Number", AppLocalizer.Get(
                "Участок с таким номером уже существует.",
                "Ділянка з таким номером уже існує.",
                "A plot with this number already exists."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var plot = await _dbContext.Plots.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (plot is null)
        {
            return NotFound();
        }

        plot.Number = Input.Number.Trim();
        plot.Address = Normalize(Input.Address);
        plot.AreaSquareMeters = Input.AreaSquareMeters;
        plot.CadastralNumber = Normalize(Input.CadastralNumber);
        plot.Notes = Normalize(Input.Notes);
        plot.IsActive = Input.IsActive;
        plot.UpdatedAtUtc = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniquePlotNumberViolation(exception))
        {
            ModelState.AddModelError("Input.Number", AppLocalizer.Get(
                "Участок с таким номером уже существует.",
                "Ділянка з таким номером уже існує.",
                "A plot with this number already exists."));
            return Page();
        }

        TempData["SuccessMessage"] = AppLocalizer.Get(
            "Изменения по участку сохранены.",
            "Зміни щодо ділянки збережено.",
            "Plot changes have been saved.");
        return RedirectToPage("/Administration/Plots/Details", new { id = plot.Id });
    }

    private async Task<bool> PlotNumberExistsAsync(int currentId, CancellationToken cancellationToken)
    {
        var normalizedNumber = Input.Number?.Trim() ?? string.Empty;

        return await _dbContext.Plots
            .AsNoTracking()
            .AnyAsync(plot => plot.Number == normalizedNumber && plot.Id != currentId, cancellationToken);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool IsUniquePlotNumberViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
