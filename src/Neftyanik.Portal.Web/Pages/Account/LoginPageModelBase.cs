using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Account;

public abstract class LoginPageModelBase : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUserActivityService _userActivityService;
    private readonly ILogger _logger;
    private readonly IAssociationMembershipService _memberships;

    protected LoginPageModelBase(
        SignInManager<ApplicationUser> signInManager,
        IUserActivityService userActivityService,
        ILogger logger,
        IAssociationMembershipService memberships)
    {
        _signInManager = signInManager;
        _userActivityService = userActivityService;
        _logger = logger;
        _memberships = memberships;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    protected void InitializeReturnUrl(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    protected async Task<IActionResult> SignInAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl ?? ReturnUrl;

        ValidateInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var login = Input.Login.Trim();
        var user = await _signInManager.UserManager.FindByNameAsync(login)
            ?? await _signInManager.UserManager.FindByEmailAsync(login);
        var userName = user?.UserName ?? login;

        var signInResult = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await _signInManager.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: false);
        if (signInResult.Succeeded)
        {
            var roles = await _memberships.GetRolesAsync(user!.Id, HttpContext.RequestAborted);
            signInResult = roles.Count == 0 ? Microsoft.AspNetCore.Identity.SignInResult.Failed
                : await _signInManager.PasswordSignInAsync(userName, Input.Password, Input.RememberMe, lockoutOnFailure: false);
        }
        if (signInResult.Succeeded)
        {
            var signedInUser = user ?? await _signInManager.UserManager.FindByNameAsync(userName);
            if (signedInUser is not null)
            {
                try
                {
                    await _userActivityService.RecordSuccessfulLoginAsync(
                        new RecordSuccessfulLoginRequest(
                            signedInUser.Id,
                            HttpContext.Connection.RemoteIpAddress?.ToString(),
                            Request.Headers.UserAgent.ToString()),
                        HttpContext.RequestAborted);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Failed to record successful login activity for user {UserId}.", signedInUser.Id);
                }
            }

            if (signedInUser?.MustChangePassword == true)
            {
                return RedirectToPage("/Account/ChangeInitialPassword");
            }

            return await RedirectAuthenticatedUserAsync(signedInUser, ReturnUrl);
        }

        if (signInResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, AppLocalizer.Get(
                "Учетная запись временно заблокирована.",
                "Обліковий запис тимчасово заблоковано.",
                "The account is temporarily locked."));
            return Page();
        }

        ModelState.AddModelError(string.Empty, AppLocalizer.Get(
            "Неверный логин или пароль.",
            "Неправильний логін або пароль.",
            "Invalid login or password."));
        return Page();
    }

    protected async Task<IActionResult> RedirectAuthenticatedUserAsync(string? returnUrl = null)
    {
        var user = await _signInManager.UserManager.GetUserAsync(User);
        return await RedirectAuthenticatedUserAsync(user, returnUrl);
    }

    protected async Task<IActionResult> RedirectAuthenticatedUserAsync(ApplicationUser? user, string? returnUrl = null)
    {
        var roles = user is null ? Array.Empty<string>()
            : await _memberships.GetRolesAsync(user.Id, HttpContext.RequestAborted);
        if (roles.Count == 0)
        {
            return RedirectToPage("/Account/AccessDenied");
        }
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            && Neftyanik.Portal.Web.Associations.TenantReturnUrls.IsWithinAssociation(Request, returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        if (roles.Contains(RoleNames.Administrator) || roles.Contains(RoleNames.Accountant))
        {
            return RedirectToPage("/Administration/Index");
        }

        return RedirectToPage("/Member/Index");
    }

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Input.Login))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(LoginInputModel.Login)}", AppLocalizer.Get(
                "Введите логин.",
                "Введіть логін.",
                "Enter a login."));
        }

        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(LoginInputModel.Password)}", AppLocalizer.Get(
                "Введите пароль.",
                "Введіть пароль.",
                "Enter a password."));
        }
    }
}

public class LoginInputModel
{
    public string Login { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}
