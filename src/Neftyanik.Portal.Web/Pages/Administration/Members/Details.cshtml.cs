using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class DetailsModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public DetailsModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public MemberDetailsViewModel Member { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new MemberDetailsViewModel
            {
                Id = item.Id,
                FullName = item.FullName,
                PhoneNumber = item.PhoneNumber,
                Email = item.Email,
                JoinedAt = item.JoinedAt,
                Notes = item.Notes,
                IsActive = item.IsActive,
                ApplicationUserId = item.ApplicationUserId,
                CreatedAtUtc = item.CreatedAtUtc,
                UpdatedAtUtc = item.UpdatedAtUtc,
                LinkedAccount = item.ApplicationUserId == null
                    ? null
                    : item.ApplicationUser != null
                        ? item.ApplicationUser.DisplayName ?? item.ApplicationUser.Email ?? item.ApplicationUser.UserName
                        : item.ApplicationUserId,
                ActiveOwnershipsCount = item.PlotOwnerships.Count(ownership => ownership.ValidTo == null)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        member.CurrentPlots = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .Where(ownership => ownership.MemberId == id)
            .OrderByDescending(ownership => ownership.ValidTo == null)
            .ThenBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new MemberPlotViewModel
            {
                PlotId = ownership.PlotId,
                PlotNumber = ownership.Plot != null ? ownership.Plot.Number : "—",
                PlotAddress = ownership.Plot != null ? ownership.Plot.Address : null,
                OwnershipShare = ownership.OwnershipShare,
                ValidFrom = ownership.ValidFrom,
                ValidTo = ownership.ValidTo
            })
            .ToListAsync(cancellationToken);

        member.HistoricalPlots = member.CurrentPlots.Where(plot => plot.ValidTo.HasValue).ToList();
        member.CurrentPlots = member.CurrentPlots.Where(plot => !plot.ValidTo.HasValue).ToList();

        member.Account = await BuildAccountViewModelAsync(member.ApplicationUserId);

        Member = member;
        return Page();
    }

    private async Task<MemberAccountViewModel> BuildAccountViewModelAsync(string? applicationUserId)
    {
        if (string.IsNullOrWhiteSpace(applicationUserId))
        {
            return new MemberAccountViewModel
            {
                StatusText = "Учетная запись не создана"
            };
        }

        var user = await _userManager.FindByIdAsync(applicationUserId);
        if (user is null)
        {
            return new MemberAccountViewModel
            {
                StatusText = "Учетная запись не создана",
                IdentityUserId = applicationUserId
            };
        }

        var roles = await _userManager.GetRolesAsync(user);
        var isLockedOut = user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow;

        return new MemberAccountViewModel
        {
            Exists = true,
            StatusText = isLockedOut ? "Учетная запись заблокирована" : "Учетная запись активна",
                Login = user.UserName,
                Email = user.Email,
            IdentityUserId = user.Id,
            IsLockedOut = isLockedOut,
            LockoutEnd = user.LockoutEnd,
            EmailConfirmed = user.EmailConfirmed,
            MustChangePassword = user.MustChangePassword,
            Roles = roles.ToArray()
        };
    }

    public sealed class MemberDetailsViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? Email { get; init; }

        public DateOnly? JoinedAt { get; init; }

        public string? Notes { get; init; }

        public bool IsActive { get; init; }

        public string? ApplicationUserId { get; init; }

        public DateTime CreatedAtUtc { get; init; }

        public DateTime? UpdatedAtUtc { get; init; }

        public string? LinkedAccount { get; init; }

        public int ActiveOwnershipsCount { get; init; }

        public MemberAccountViewModel Account { get; set; } = new();

        public IReadOnlyList<MemberPlotViewModel> CurrentPlots { get; set; } = [];

        public IReadOnlyList<MemberPlotViewModel> HistoricalPlots { get; set; } = [];
    }

    public sealed class MemberPlotViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? PlotAddress { get; init; }

        public decimal? OwnershipShare { get; init; }

        public DateOnly? ValidFrom { get; init; }

        public DateOnly? ValidTo { get; init; }
    }

    public sealed class MemberAccountViewModel
    {
        public bool Exists { get; init; }

        public string StatusText { get; init; } = string.Empty;

        public string? Login { get; init; }

        public string? Email { get; init; }

        public string? IdentityUserId { get; init; }

        public bool IsLockedOut { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }

        public bool EmailConfirmed { get; init; }

        public bool MustChangePassword { get; init; }

        public IReadOnlyList<string> Roles { get; init; } = [];
    }
}
