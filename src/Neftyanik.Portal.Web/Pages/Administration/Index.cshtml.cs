using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly IUserActivityService _userActivityService;

    public IndexModel(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IUserActivityService userActivityService)
    {
        _dbContext = dbContext;
        _environment = environment;
        _userActivityService = userActivityService;
    }

    public bool DatabaseAvailable { get; private set; }

    public int RegisteredUsersCount { get; private set; }

    public int RolesCount { get; private set; }

    public int AppliedMigrationsCount { get; private set; }

    public string EnvironmentName { get; private set; } = string.Empty;

    public bool CanViewUserActivity { get; private set; }

    public UserActivityDashboardSummary? UserActivitySummary { get; private set; }

    public async Task OnGetAsync()
    {
        EnvironmentName = _environment.EnvironmentName;

        try
        {
            DatabaseAvailable = await _dbContext.Database.CanConnectAsync();
        }
        catch
        {
            DatabaseAvailable = false;
        }

        RegisteredUsersCount = await _dbContext.Users.AsNoTracking().CountAsync();
        RolesCount = await _dbContext.Roles.AsNoTracking().CountAsync();
        AppliedMigrationsCount = (await _dbContext.Database.GetAppliedMigrationsAsync()).Count();

        CanViewUserActivity = User.IsInRole(RoleNames.Administrator);
        if (CanViewUserActivity)
        {
            UserActivitySummary = await _userActivityService.GetDashboardSummaryAsync(HttpContext.RequestAborted);
        }
    }
}
