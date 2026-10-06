using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Application.Associations;

namespace Neftyanik.Portal.Web.Pages.Platform;

public class IndexModel(SignInManager<ApplicationUser> signInManager, IPlatformOverviewReader overview) : PageModel
{
    public PlatformOverview Overview { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Overview = await overview.GetAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await signInManager.SignOutAsync();
        return RedirectToPage("/Platform/Account/Login");
    }
}
