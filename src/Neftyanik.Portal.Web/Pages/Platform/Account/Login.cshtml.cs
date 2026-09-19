using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Localization;
using Neftyanik.Portal.Web.Pages.Account;
using Neftyanik.Portal.Web.Security;

namespace Neftyanik.Portal.Web.Pages.Platform.Account;

public class LoginModel(
    SignInManager<ApplicationUser> signInManager,
    PlatformAdministratorAccess access,
    IAuthorizationService authorization) : PageModel
{
    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if ((await authorization.AuthorizeAsync(User, PlatformAuthorization.PolicyName)).Succeeded)
        {
            return RedirectToPage("/Platform/Index");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid && !string.IsNullOrWhiteSpace(Input.Login) && !string.IsNullOrWhiteSpace(Input.Password))
        {
            var login = Input.Login.Trim();
            var user = await signInManager.UserManager.FindByNameAsync(login)
                ?? await signInManager.UserManager.FindByEmailAsync(login);
            if (await access.IsAllowedAsync(user))
            {
                var result = await signInManager.PasswordSignInAsync(user!, Input.Password, Input.RememberMe, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    return RedirectToPage("/Platform/Index");
                }
            }
        }

        ModelState.AddModelError(string.Empty, AppLocalizer.Get(
            "Вход в управление платформой недоступен. Проверьте данные входа и права доступа.",
            "Вхід до керування платформою недоступний. Перевірте дані входу та права доступу.",
            "Platform sign-in is unavailable. Check your credentials and access permissions."));
        return Page();
    }
}
