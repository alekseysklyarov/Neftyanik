using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Pages.Account;

namespace Neftyanik.Portal.Web.Pages;

[AllowAnonymous]
public class IndexModel : LoginPageModelBase
{
    public IndexModel(
        SignInManager<ApplicationUser> signInManager,
        IUserActivityService userActivityService,
        ILogger<IndexModel> logger)
        : base(signInManager, userActivityService, logger)
    {
    }

    public void OnGet(string? returnUrl = null)
    {
        InitializeReturnUrl(returnUrl);
    }

    public Task<IActionResult> OnPostAsync(string? returnUrl = null) => SignInAsync(returnUrl);
}
