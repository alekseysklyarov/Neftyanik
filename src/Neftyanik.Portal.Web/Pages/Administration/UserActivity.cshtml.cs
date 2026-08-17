using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration;

[Authorize(Roles = RoleNames.Administrator)]
public class UserActivityModel : PageModel
{
    private readonly IUserActivityService _userActivityService;

    public UserActivityModel(IUserActivityService userActivityService)
    {
        _userActivityService = userActivityService;
    }

    public UserActivityDashboardSummary Summary { get; private set; } = new(0, 0, 0, 0, 0);

    public IReadOnlyList<UserActivityListItem> Users { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Summary = await _userActivityService.GetDashboardSummaryAsync(cancellationToken);
        Users = await _userActivityService.GetUserActivityAsync(cancellationToken);
    }

    public string GetActivityStatusText(UserActivityStatus status)
    {
        return status switch
        {
            UserActivityStatus.Today => AppLocalizer.Get("Сегодня", "Сьогодні", "Today"),
            UserActivityStatus.Last7Days => AppLocalizer.Get("Последние 7 дней", "Останні 7 днів", "Last 7 days"),
            UserActivityStatus.Last30Days => AppLocalizer.Get("Последние 30 дней", "Останні 30 днів", "Last 30 days"),
            UserActivityStatus.MoreThan30DaysAgo => AppLocalizer.Get("Более 30 дней назад", "Понад 30 днів тому", "More than 30 days ago"),
            _ => AppLocalizer.Get("Никогда не входил", "Ніколи не входив", "Never logged in")
        };
    }

    public string GetActivityStatusBadgeClass(UserActivityStatus status)
    {
        return status switch
        {
            UserActivityStatus.Today => "text-bg-success",
            UserActivityStatus.Last7Days => "text-bg-primary",
            UserActivityStatus.Last30Days => "text-bg-info",
            UserActivityStatus.MoreThan30DaysAgo => "text-bg-secondary",
            _ => "text-bg-warning"
        };
    }
}
