using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Finance.ChargeTypes;

[Authorize(Roles = RoleNames.Administrator)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;

    public DetailsModel(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ChargeTypeDetailsViewModel ChargeType { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var chargeType = await _dbContext.ChargeTypes
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new ChargeTypeDetailsViewModel
            {
                Id = item.Id,
                Name = item.Name,
                Description = item.Description,
                DefaultAmount = item.DefaultAmount,
                IsDefault = item.IsDefault,
                IsYearly = item.IsYearly,
                OnlyOnOwnerChange = item.OnlyOnOwnerChange,
                IsActive = item.IsActive,
                CreatedAtUtc = item.CreatedAtUtc,
                UpdatedAtUtc = item.UpdatedAtUtc,
                ChargesCount = item.Charges.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (chargeType is null)
        {
            return NotFound();
        }

        ChargeType = chargeType;
        return Page();
    }

    public sealed class ChargeTypeDetailsViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public decimal? DefaultAmount { get; init; }

        public bool IsDefault { get; init; }

        public bool IsYearly { get; init; }

        public bool OnlyOnOwnerChange { get; init; }

        public bool IsActive { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime? UpdatedAtUtc { get; init; }

        public int ChargesCount { get; init; }
    }
}
