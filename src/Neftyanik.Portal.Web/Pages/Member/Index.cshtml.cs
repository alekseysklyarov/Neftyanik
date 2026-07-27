using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;

namespace Neftyanik.Portal.Web.Pages.Member;

[Authorize(Roles = RoleNames.Member + "," + RoleNames.Administrator)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public MemberDashboardViewModel Dashboard { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var currentDate = DateOnly.FromDateTime(DateTime.Now);

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (user.MustChangePassword)
        {
            return RedirectToPage("/Account/ChangeInitialPassword");
        }

        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.ApplicationUserId == user.Id)
            .Select(item => new MemberDashboardQueryModel
            {
                MemberId = item.Id,
                FullName = item.FullName,
                Email = item.Email,
                PhoneNumber = item.PhoneNumber,
                IsLinked = true
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            Dashboard = new MemberDashboardViewModel
            {
                FullName = !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.Email ?? user.UserName ?? "Пользователь",
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                IsLinked = false
            };

            return Page();
        }

        member.Plots = await _dbContext.PlotOwnerships
            .AsNoTracking()
            .WhereCurrentForMember(member.MemberId, currentDate)
            .OrderBy(ownership => ownership.Plot != null ? ownership.Plot.Number : string.Empty)
            .Select(ownership => new PlotViewModel
            {
                PlotId = ownership.PlotId,
                PlotNumber = ownership.Plot != null ? ownership.Plot.Number : "—",
                Address = ownership.Plot != null ? ownership.Plot.Address : null,
                OwnershipShare = ownership.OwnershipShare,
                IsPrimaryContact = ownership.IsPrimaryContact,
                ActiveChargesTotal = ownership.Plot != null
                    ? ownership.Plot.Charges.Where(charge => charge.CancelledAtUtc == null).Sum(charge => (decimal?)charge.Amount) ?? 0m
                    : 0m,
                ActivePaymentsTotal = ownership.Plot != null
                    ? ownership.Plot.Payments.Where(payment => payment.CancelledAtUtc == null).Sum(payment => (decimal?)payment.Amount) ?? 0m
                    : 0m
            })
            .ToListAsync(cancellationToken);

        Dashboard = new MemberDashboardViewModel
        {
            FullName = member.FullName,
            Email = member.Email,
            PhoneNumber = member.PhoneNumber,
            IsLinked = member.IsLinked,
            Plots = member.Plots
        };

        return Page();
    }

    private sealed class MemberDashboardQueryModel
    {
        public int MemberId { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsLinked { get; init; }

        public IReadOnlyList<PlotViewModel> Plots { get; set; } = [];
    }

    public sealed class MemberDashboardViewModel
    {
        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsLinked { get; init; }

        public IReadOnlyList<PlotViewModel> Plots { get; set; } = [];
    }

    public sealed class PlotViewModel
    {
        public int PlotId { get; init; }

        public string PlotNumber { get; init; } = string.Empty;

        public string? Address { get; init; }

        public decimal? OwnershipShare { get; init; }

        public bool IsPrimaryContact { get; init; }

        public decimal ActiveChargesTotal { get; init; }

        public decimal ActivePaymentsTotal { get; init; }

        public decimal Balance => ActiveChargesTotal - ActivePaymentsTotal;
    }
}
