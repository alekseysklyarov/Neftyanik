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

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        InitializeReturnUrl(returnUrl);
        return Page();
    }

    public Task<IActionResult> OnPostAsync(string? returnUrl = null) => SignInAsync(returnUrl);
}
