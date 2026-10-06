using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Platform.Associations;

public class CreateModel(IPlatformAssociationCreator creator, IPlatformAssociationWriteAccess access,
    IDataProtectionProvider protection, IAssociationSlugReservations reservations) : PageModel
{
    private readonly ITimeLimitedDataProtector protector = protection.CreateProtector("DachaHub.AssociationCreation.Confirmation.v3").ToTimeLimitedDataProtector();

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public PasswordInputModel Password { get; set; } = new();
    [BindProperty] public string? ConfirmationToken { get; set; }
    [BindProperty] public bool ConfirmAdministrator { get; set; }
    public bool HasPreview { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostPreviewAsync(CancellationToken cancellationToken)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        ClearPassword();
        ConfirmationToken = null;
        ModelState.Remove(nameof(ConfirmationToken));
        if (ModelState.IsValid && reservations.IsReserved(Input.Slug))
            ModelState.AddModelError("Input.Slug", SlugReservedMessage);
        if (!ModelState.IsValid) return Page();
        ConfirmationToken = protector.Protect(JsonSerializer.Serialize(new Confirmation(Input, actor)), TimeSpan.FromMinutes(15));
        HasPreview = true;
        Response.Headers.CacheControl = "no-store";
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        var actor = await access.GetOperatorIdAsync(cancellationToken);
        var temporaryPassword = Password.TemporaryPassword;
        var repeatedPassword = Password.ConfirmPassword;
        ClearPassword();
        Confirmation? confirmation = null;
        try
        {
            if (!string.IsNullOrEmpty(ConfirmationToken))
                confirmation = JsonSerializer.Deserialize<Confirmation>(protector.Unprotect(ConfirmationToken, out _));
        }
        catch (CryptographicException) { }
        catch (JsonException) { }
        if (confirmation is null || confirmation.OperatorId != actor)
        {
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, AppLocalizer.Get("Повторите ввод и подтверждение.", "Повторіть введення й підтвердження.", "Repeat input and confirmation."));
            return Page();
        }

        Input = confirmation.Input;
        // Only non-secret fields come from the protected preview. The password is supplied on this POST.
        ModelState.Clear();
        HasPreview = true;
        if (!TryValidateModel(Input, nameof(Input)))
        {
            HasPreview = false;
            ConfirmationToken = null;
            return Page();
        }
        if (!ConfirmAdministrator)
        {
            HasPreview = true;
            ModelState.AddModelError(nameof(ConfirmAdministrator), AppLocalizer.Get("Подтвердите назначение.", "Підтвердіть призначення.", "Confirm the administrator assignment."));
            return Page();
        }

        if (string.IsNullOrWhiteSpace(temporaryPassword) || temporaryPassword.Length > 128
            || temporaryPassword != repeatedPassword)
        {
            ModelState.AddModelError("Password.ConfirmPassword", AppLocalizer.Get("Введите и подтвердите временный пароль.", "Введіть і підтвердіть тимчасовий пароль.", "Enter and confirm the temporary password."));
            return Page();
        }
        var request = new AssociationCreationRequest(Input.Name, Input.Slug, Input.ContactEmail, Input.ContactPhone,
            Input.PostalAddress, Input.AdministratorEmail, Input.AdministratorUserName, true,
            temporaryPassword, Input.AdministratorDisplayName);
        var result = await creator.CreateAsync(request, cancellationToken);
        if (result.Outcome == AssociationCreationOutcome.Created)
        {
            TempData["SuccessMessage"] = AppLocalizer.Get("Товарищество создано. Администратор назначен.", "Товариство створено. Адміністратора призначено.", "Association created and administrator assigned.");
            TempData["SetupMessage"] = SetupMessage;
            return RedirectToPage("Details", new { id = result.AssociationId });
        }
        ConfirmationToken = null;
        HasPreview = false;
        var field = result.Outcome switch
        {
            AssociationCreationOutcome.SlugExists or AssociationCreationOutcome.InvalidSlug => "Input.Slug",
            AssociationCreationOutcome.UsernameExists => "Input.AdministratorUserName",
            _ => string.Empty
        };
        ModelState.AddModelError(field, result.Outcome switch
        {
            AssociationCreationOutcome.SlugExists => AppLocalizer.Get("Такой slug уже используется.", "Такий slug уже використовується.", "This slug is already in use."),
            AssociationCreationOutcome.InvalidSlug => AppLocalizer.Get("Slug недопустим или зарезервирован.", "Slug неприпустимий або зарезервований.", "The slug is invalid or reserved."),
            AssociationCreationOutcome.UsernameExists => AppLocalizer.Get("Такой логин уже используется. Укажите новый.", "Такий логін уже використовується. Вкажіть новий.", "This username is already in use. Choose a new one."),
            AssociationCreationOutcome.IdentityRejected => AppLocalizer.Get("Identity отклонила данные аккаунта. Проверьте логин и требования к паролю.", "Identity відхилила дані акаунта. Перевірте логін і вимоги до пароля.", "Identity rejected the account details. Check the username and password requirements."),
            AssociationCreationOutcome.InvalidInput => AppLocalizer.Get("Проверьте название и контактные данные.", "Перевірте назву та контактні дані.", "Check the name and contact details."),
            _ => AppLocalizer.Get("Создать товарищество не удалось. Изменения отменены.", "Створити товариство не вдалося. Зміни скасовано.", "Unable to create the association. Changes were rolled back.")
        });
        return Page();
    }

    public static string SetupMessage => AppLocalizer.Get(
        "Перед расчетами настройте тарифы электроэнергии, ставки взносов с датами действия и виды начислений. Остатки и финансовые записи не создавались.",
        "Перед розрахунками налаштуйте тарифи електроенергії, ставки внесків із датами дії та види нарахувань. Залишки й фінансові записи не створювалися.",
        "Before accounting, configure electricity tariffs, membership fee rates with effective dates, and charge types. No balances or financial records were created.");

    private void ClearPassword()
    {
        Password = new();
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith("Password.", StringComparison.OrdinalIgnoreCase)).ToArray())
            ModelState.Remove(key);
        // The first step has no Password fields, so model binding can use an empty prefix.
        ModelState.Remove(nameof(PasswordInputModel.TemporaryPassword));
        ModelState.Remove(nameof(PasswordInputModel.ConfirmPassword));
        Response.Headers.CacheControl = "no-store";
    }

    public sealed record Confirmation(InputModel Input, string OperatorId);

    public static string SlugFormatMessage => AppLocalizer.Get(
        "Используйте только маленькие латинские буквы a–z, цифры и одиночные дефисы между ними. Например: test-garden.",
        "Використовуйте лише малі латинські літери a–z, цифри та одиночні дефіси між ними. Наприклад: test-garden.",
        "Use only lowercase letters a–z, digits and single hyphens between them. For example: test-garden.");
    public static string SlugReservedMessage => AppLocalizer.Get(
        "Этот адрес зарезервирован для страниц сайта. Выберите другой slug.",
        "Цю адресу зарезервовано для сторінок сайту. Виберіть інший slug.",
        "This address is reserved for site pages. Choose another slug.");
    public static string RequiredFieldMessage => AppLocalizer.Get("Заполните это поле.", "Заповніть це поле.", "Fill in this field.");
    public static string SlugLengthMessage => AppLocalizer.Get("Slug должен содержать не более {1} символов.", "Slug має містити не більше {1} символів.", "The slug must contain no more than {1} characters.");
    public static string PasswordMatchMessage => AppLocalizer.Get("Пароли не совпадают.", "Паролі не збігаються.", "Passwords do not match.");

    public sealed class InputModel
    {
        [Required, StringLength(AssociationMetadataLimits.Name)] public string Name { get; set; } = string.Empty;
        [Required(ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(RequiredFieldMessage))]
        [StringLength(100, ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(SlugLengthMessage))]
        [RegularExpression("[a-z0-9]+(?:-[a-z0-9]+)*", ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(SlugFormatMessage))]
        public string Slug { get; set; } = string.Empty;
        [EmailAddress, StringLength(AssociationMetadataLimits.ContactEmail)] public string? ContactEmail { get; set; }
        [StringLength(AssociationMetadataLimits.ContactPhone)] public string? ContactPhone { get; set; }
        [StringLength(AssociationMetadataLimits.PostalAddress)] public string? PostalAddress { get; set; }
        [Required, StringLength(256)] public string AdministratorUserName { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(256)] public string AdministratorEmail { get; set; } = string.Empty;
        [StringLength(200)] public string? AdministratorDisplayName { get; set; }
    }

    public sealed class PasswordInputModel
    {
        [Required(ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(RequiredFieldMessage))]
        [DataType(DataType.Password)] public string TemporaryPassword { get; set; } = string.Empty;
        [Required(ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(RequiredFieldMessage))]
        [Compare(nameof(TemporaryPassword), ErrorMessageResourceType = typeof(CreateModel), ErrorMessageResourceName = nameof(PasswordMatchMessage))]
        [DataType(DataType.Password)] public string ConfirmPassword { get; set; } = string.Empty;
    }
}
