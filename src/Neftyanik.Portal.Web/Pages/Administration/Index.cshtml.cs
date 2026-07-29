using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Pages.Administration;

[Authorize(Roles = RoleNames.AdministratorOrAccountant)]
public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;

    public IndexModel(ApplicationDbContext dbContext, IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _environment = environment;
    }

    public bool DatabaseAvailable { get; private set; }

    public int RegisteredUsersCount { get; private set; }

    public int RolesCount { get; private set; }

    public int AppliedMigrationsCount { get; private set; }

    public string EnvironmentName { get; private set; } = string.Empty;

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
    }
}
