using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Plots;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public CreateModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public PlotInputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(Input.Number) && await PlotNumberExistsAsync(null, cancellationToken))
        {
            ModelState.AddModelError("Input.Number", "Участок с таким номером уже существует.");
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var plot = new Plot
        {
            Number = Input.Number.Trim(),
            Address = Normalize(Input.Address),
            AreaSquareMeters = Input.AreaSquareMeters,
            CadastralNumber = Normalize(Input.CadastralNumber),
            Notes = Normalize(Input.Notes),
            IsActive = Input.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Plots.Add(plot);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniquePlotNumberViolation(exception))
        {
            ModelState.AddModelError("Input.Number", "Участок с таким номером уже существует.");
            return Page();
        }

        TempData["SuccessMessage"] = "Участок успешно создан.";
        return RedirectToPage("/Administration/Plots/Index");
    }

    private async Task<bool> PlotNumberExistsAsync(int? currentId, CancellationToken cancellationToken)
    {
        var normalizedNumber = Input.Number?.Trim() ?? string.Empty;

        return await _dbContext.Plots
            .AsNoTracking()
            .AnyAsync(plot => plot.Number == normalizedNumber && (!currentId.HasValue || plot.Id != currentId.Value), cancellationToken);
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
