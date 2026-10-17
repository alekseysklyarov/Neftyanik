using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Pages.Account;

namespace Neftyanik.Portal.Web.Pages;

[AllowAnonymous]
public class IndexModel : LoginPageModelBase
{
    private readonly IAssociationContext _associationContext;
    public IndexModel(
        SignInManager<ApplicationUser> signInManager,
        IUserActivityService userActivityService,
        ILogger<IndexModel> logger,
        IAssociationMembershipService memberships,
        IAssociationContext associationContext)
        : base(signInManager, userActivityService, logger, memberships)
    {
        _associationContext = associationContext;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        if (!_associationContext.IsResolved) return Page();
        if (User.Identity?.IsAuthenticated == true)
        {
            return await RedirectAuthenticatedUserAsync();
        }

        InitializeReturnUrl(returnUrl);
        return Page();
    }

    public Task<IActionResult> OnPostAsync(string? returnUrl = null) => _associationContext.IsResolved
        ? SignInAsync(returnUrl) : Task.FromResult<IActionResult>(BadRequest());
}
