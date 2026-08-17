using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IUserActivityService _userActivityService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<ApplicationUser> signInManager,
        IUserActivityService userActivityService,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userActivityService = userActivityService;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        ValidateInput();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var login = Input.Login.Trim();
        var user = await _signInManager.UserManager.FindByNameAsync(login)
            ?? await _signInManager.UserManager.FindByEmailAsync(login);
        var userName = user?.UserName ?? login;

        var signInResult = await _signInManager.PasswordSignInAsync(userName, Input.Password, Input.RememberMe, lockoutOnFailure: false);
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

            return LocalRedirect(returnUrl ?? Url.Content("~/"));
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

    private void ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Input.Login))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.Login)}", AppLocalizer.Get(
                "Введите логин.",
                "Введіть логін.",
                "Enter a login."));
        }

        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(InputModel.Password)}", AppLocalizer.Get(
                "Введите пароль.",
                "Введіть пароль.",
                "Enter a password."));
        }
    }

    public class InputModel
    {
        public string Login { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
