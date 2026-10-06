using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class AddAdministratorModel(IPlatformAssociationReader associations,
    IPlatformAssociationAdministratorCreator creator) : PageModel
{
    public PlatformAssociationDetails Association { get; private set; } = null!;
    [BindProperty] public InputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var association = await associations.GetDetailsAsync(id, cancellationToken);
        if (association is null) return NotFound();
        Association = association;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        var password = Input.TemporaryPassword;
        Input.TemporaryPassword = Input.ConfirmPassword = string.Empty;
        ModelState.SetModelValue("Input.TemporaryPassword", null, null);
        ModelState.SetModelValue("Input.ConfirmPassword", null, null);
        var page = await OnGetAsync(id, cancellationToken);
        if (page is NotFoundResult) return page;
        if (!Input.ConfirmAssignment)
            ModelState.AddModelError("Input.ConfirmAssignment", AppLocalizer.Get("Подтвердите назначение администратора.", "Підтвердіть призначення адміністратора.", "Confirm the administrator assignment."));
        if (!ModelState.IsValid) return Page();
        var outcome = await creator.CreateAsync(id, new(Input.UserName, Input.Email, Input.DisplayName, password, Input.ConfirmAssignment), cancellationToken);
        if (outcome == AdministratorCreationOutcome.NotFound) return NotFound();
        if (outcome == AdministratorCreationOutcome.Created)
        {
            TempData["SuccessMessage"] = AppLocalizer.Get("Администратор создан и назначен. Передайте ему адрес входа и временный пароль.", "Адміністратора створено й призначено. Передайте йому адресу входу та тимчасовий пароль.", "Administrator created and assigned. Share the sign-in address and temporary password.");
            return RedirectToPage("Details", new { id });
        }
        ModelState.AddModelError(outcome == AdministratorCreationOutcome.UsernameExists ? "Input.UserName" : string.Empty, outcome switch
        {
            AdministratorCreationOutcome.InactiveAssociation => AppLocalizer.Get("Сначала активируйте товарищество.", "Спочатку активуйте товариство.", "Activate the association first."),
            AdministratorCreationOutcome.UsernameExists => AppLocalizer.Get("Этот логин уже занят. Укажите новый логин.", "Цей логін уже зайнято. Укажіть новий логін.", "This username is already taken. Choose a new username."),
            AdministratorCreationOutcome.IdentityRejected => AppLocalizer.Get("Проверьте логин и пароль: минимум 6 символов, хотя бы одна буква и цифра.", "Перевірте логін і пароль: щонайменше 6 символів, хоча б одна літера й цифра.", "Check the username and password: at least 6 characters, including a letter and a digit."),
            AdministratorCreationOutcome.InvalidInput => AppLocalizer.Get("Проверьте данные администратора.", "Перевірте дані адміністратора.", "Check the administrator details."),
            _ => AppLocalizer.Get("Назначение не сохранено. Повторите попытку.", "Призначення не збережено. Повторіть спробу.", "The assignment was not saved. Please try again.")
        });
        return Page();
    }

    public sealed class InputModel
    {
        [Required, StringLength(256)] public string UserName { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = string.Empty;
        [StringLength(200)] public string? DisplayName { get; set; }
        [Required, StringLength(128), DataType(DataType.Password)] public string TemporaryPassword { get; set; } = string.Empty;
        [Required, Compare(nameof(TemporaryPassword)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
        public bool ConfirmAssignment { get; set; }
    }
}
