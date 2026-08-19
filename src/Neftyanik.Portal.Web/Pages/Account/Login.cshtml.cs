using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : LoginPageModelBase
{
    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IUserActivityService userActivityService,
        ILogger<LoginModel> logger)
        : base(signInManager, userActivityService, logger)
    {
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return await RedirectAuthenticatedUserAsync(returnUrl);
        }

        InitializeReturnUrl(returnUrl);
        return Page();
    }

    public Task<IActionResult> OnPostAsync(string? returnUrl = null) => SignInAsync(returnUrl);
}
