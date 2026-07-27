using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LoginModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
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

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var signInResult = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: false);
        if (signInResult.Succeeded)
        {
            return LocalRedirect(returnUrl ?? Url.Content("~/"));
        }

        if (signInResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "??????? ?????? ???????? ?????????????.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "???????? ????? ??????????? ????? ??? ??????.");
        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "??????? ????? ??????????? ?????.")]
        [EmailAddress(ErrorMessage = "??????? ?????????? ????? ??????????? ?????.")]
        [Display(Name = "??????????? ?????")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "??????? ??????.")]
        [DataType(DataType.Password)]
        [Display(Name = "??????")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "????????? ????")]
        public bool RememberMe { get; set; }
    }
}
