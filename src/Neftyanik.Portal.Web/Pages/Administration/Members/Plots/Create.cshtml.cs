using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Plots;

[Authorize(Roles = RoleNames.Administrator)]
public class CreateModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public CreateModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public MemberContextViewModel Member { get; private set; } = new();

    public IReadOnlyList<SelectListItem> PlotOptions { get; private set; } = [];

    public bool HasAvailablePlots => PlotOptions.Count > 0;

    public async Task<IActionResult> OnGetAsync(int memberId, CancellationToken cancellationToken)
    {
        return await LoadPageAsync(memberId, cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberContextAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        Member = member;

        if (!member.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Нельзя добавить участок архивному члену товарищества.");
        }

        PlotOptions = await GetPlotOptionsAsync(Input.PlotId, cancellationToken);
        if (PlotOptions.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Нет свободных участков, доступных для добавления.");
        }

        if (Input.PlotId is null)
        {
            ModelState.AddModelError("Input.PlotId", "Выберите участок.");
        }
        else
        {
            if (await HasOpenOwnershipAsync(Input.PlotId.Value, cancellationToken))
            {
                ModelState.AddModelError("Input.PlotId", "Выберите свободный участок из списка доступных.");
            }

            var validPlotIds = PlotOptions.Select(option => option.Value).ToHashSet(StringComparer.Ordinal);
            if (!validPlotIds.Contains(Input.PlotId.Value.ToString()))
            {
                ModelState.AddModelError("Input.PlotId", "Выберите свободный участок из списка доступных.");
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        _dbContext.PlotOwnerships.Add(new PlotOwnership
        {
            PlotId = Input.PlotId!.Value,
            MemberId = memberId,
            OwnershipShare = Input.OwnershipShare,
            ValidFrom = Input.ValidFrom,
            CreatedAtUtc = DateTime.UtcNow,
            IsPrimaryContact = member.ActiveOwnershipsCount == 0
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (TempData is not null)
        {
            TempData["SuccessMessage"] = "Участок успешно добавлен члену товарищества.";
        }

        return RedirectToPage("/Administration/Members/Details", new { id = memberId });
    }

    private async Task<IActionResult> LoadPageAsync(int memberId, CancellationToken cancellationToken)
    {
        var member = await GetMemberContextAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        Member = member;
        PlotOptions = await GetPlotOptionsAsync(Input.PlotId, cancellationToken);
        Input.ValidFrom ??= DateOnly.FromDateTime(DateTime.Today);
        return Page();
    }

    private async Task<MemberContextViewModel?> GetMemberContextAsync(int memberId, CancellationToken cancellationToken)
    {
        return await _dbContext.Members
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => new MemberContextViewModel
            {
                Id = member.Id,
                FullName = member.FullName,
                Email = member.Email,
                PhoneNumber = member.PhoneNumber,
                IsActive = member.IsActive,
                ActiveOwnershipsCount = member.PlotOwnerships.Count(ownership => ownership.ValidTo == null)
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<SelectListItem>> GetPlotOptionsAsync(int? selectedPlotId, CancellationToken cancellationToken)
    {
        var openOwnershipPlotIds = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.ValidTo == null)
            .Select(ownership => ownership.PlotId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var plots = await _dbContext.Plots
            .AsNoTracking()
            .Where(plot => (plot.IsActive && !openOwnershipPlotIds.Contains(plot.Id)) || (selectedPlotId.HasValue && plot.Id == selectedPlotId.Value))
            .OrderBy(plot => plot.Number)
            .Select(plot => new
            {
                plot.Id,
                plot.Number,
                plot.Address,
                plot.IsActive
            })
            .ToListAsync(cancellationToken);

        return plots
            .Where(plot => plot.IsActive || (selectedPlotId.HasValue && plot.Id == selectedPlotId.Value))
            .Select(plot => new SelectListItem
            {
                Value = plot.Id.ToString(),
                Text = string.IsNullOrWhiteSpace(plot.Address)
                    ? $"Участок {plot.Number}"
                    : $"Участок {plot.Number} — {plot.Address}"
            })
            .ToList();
    }

    private Task<bool> HasOpenOwnershipAsync(int plotId, CancellationToken cancellationToken)
    {
        return _dbContext.PlotOwnerships
            .AsNoTracking()
            .AnyAsync(ownership => ownership.PlotId == plotId && ownership.ValidTo == null, cancellationToken);
    }

    public sealed class MemberContextViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsActive { get; init; }

        public int ActiveOwnershipsCount { get; init; }
    }

    public sealed class InputModel : IValidatableObject
    {
        [Required(ErrorMessage = "Выберите участок.")]
        [Display(Name = "Участок")]
        public int? PlotId { get; set; }

        [Display(Name = "Доля владения, %")]
        public decimal? OwnershipShare { get; set; }

        [DataType(DataType.Date)]
        [Display(Name = "Действует с")]
        public DateOnly? ValidFrom { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (OwnershipShare.HasValue && (OwnershipShare.Value <= 0m || OwnershipShare.Value > 100m))
            {
                yield return new ValidationResult(
                    "Доля владения должна быть больше 0 и не больше 100.",
                    [nameof(OwnershipShare)]);
            }
        }
    }
}
