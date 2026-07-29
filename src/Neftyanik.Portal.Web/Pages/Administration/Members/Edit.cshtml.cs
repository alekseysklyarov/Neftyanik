using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;

namespace Neftyanik.Portal.Web.Pages.Administration.Members;

[Authorize(Roles = RoleNames.Administrator)]
public class EditModel : PageModel
{
    private readonly ApplicationDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public EditModel(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    [BindProperty]
    public MemberInputModel Input { get; set; } = new();

    public int MemberId { get; private set; }

    public string? LinkedAccount { get; private set; }

    public bool HasLinkedAccount { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        var member = await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.FullName,
                item.PhoneNumber,
                item.Email,
                item.JoinedAt,
                item.Notes,
                item.IsActive,
                Login = item.ApplicationUser != null ? item.ApplicationUser.UserName : null,
                LinkedAccount = item.ApplicationUserId == null
                    ? null
                    : item.ApplicationUser != null
                        ? item.ApplicationUser.DisplayName ?? item.ApplicationUser.Email ?? item.ApplicationUser.UserName
                        : item.ApplicationUserId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (member is null)
        {
            return NotFound();
        }

        MemberId = member.Id;
        LinkedAccount = member.LinkedAccount;
        HasLinkedAccount = !string.IsNullOrWhiteSpace(member.Login);
        Input = new MemberInputModel
        {
            Login = member.Login,
            FullName = member.FullName,
            PhoneNumber = member.PhoneNumber,
            Email = member.Email,
            JoinedAt = member.JoinedAt,
            Notes = member.Notes,
            IsActive = member.IsActive
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken)
    {
        MemberId = id;
        var linkedAccountContext = await GetLinkedAccountContextAsync(id, cancellationToken);
        LinkedAccount = linkedAccountContext.DisplayName;
        HasLinkedAccount = linkedAccountContext.Exists;

        Input.FullName = Input.FullName.Trim();
        Input.PhoneNumber = Normalize(Input.PhoneNumber);
        Input.Email = Normalize(Input.Email);
        Input.Notes = Normalize(Input.Notes);
        Input.Login = Normalize(Input.Login);

        if (HasLinkedAccount && string.IsNullOrWhiteSpace(Input.Login))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(MemberInputModel.Login)}", AppLocalizer.Get(
                "Укажите логин для входа.",
                "Вкажіть логін для входу.",
                "Enter a login."));
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var member = await _dbContext.Members.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        ApplicationUser? user = null;
        if (!string.IsNullOrWhiteSpace(member.ApplicationUserId))
        {
            user = await _dbContext.Users.FirstOrDefaultAsync(item => item.Id == member.ApplicationUserId, cancellationToken);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, AppLocalizer.Get(
                    "Связанная учетная запись не найдена.",
                    "Пов'язаний обліковий запис не знайдено.",
                    "The linked account was not found."));
                return Page();
            }

            var normalizedLogin = _userManager.NormalizeName(Input.Login!);
            var duplicateLoginExists = await _dbContext.Users
                .AsNoTracking()
                .AnyAsync(item => item.Id != user.Id && item.NormalizedUserName == normalizedLogin, cancellationToken);

            if (duplicateLoginExists)
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(MemberInputModel.Login)}", AppLocalizer.Get(
                    "Пользователь с таким логином уже существует.",
                    "Користувач із таким логіном уже існує.",
                    "A user with this login already exists."));
            }
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        member.FullName = Input.FullName;
        member.PhoneNumber = Input.PhoneNumber;
        member.Email = Input.Email;
        member.JoinedAt = Input.JoinedAt;
        member.Notes = Input.Notes;
        member.IsActive = Input.IsActive;
        member.UpdatedAtUtc = DateTime.UtcNow;

        if (user is not null)
        {
            var name = ParseFullName(Input.FullName);
            user.UserName = Input.Login;
            user.NormalizedUserName = _userManager.NormalizeName(Input.Login);
            user.Email = Input.Email;
            user.NormalizedEmail = Input.Email is null ? null : _userManager.NormalizeEmail(Input.Email);
            user.PhoneNumber = Input.PhoneNumber;
            user.FirstName = name.FirstName;
            user.LastName = name.LastName;
            user.DisplayName = name.DisplayName;
            user.IsActive = Input.IsActive;

            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                AddIdentityErrors(updateResult);
                return Page();
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["SuccessMessage"] = AppLocalizer.Get(
            "Изменения по члену товарищества сохранены.",
            "Зміни щодо члена товариства збережено.",
            "Member changes have been saved.");
        return RedirectToPage("/Administration/Members/Details", new { id = member.Id });
    }

    private async Task<LinkedAccountContext> GetLinkedAccountContextAsync(int id, CancellationToken cancellationToken)
    {
        return await _dbContext.Members
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new LinkedAccountContext(
                item.ApplicationUserId != null,
                item.ApplicationUserId == null
                    ? null
                    : item.ApplicationUser != null
                        ? item.ApplicationUser.DisplayName ?? item.ApplicationUser.Email ?? item.ApplicationUser.UserName
                        : item.ApplicationUserId))
            .FirstOrDefaultAsync(cancellationToken) ?? new LinkedAccountContext(false, null);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            var key = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateUserName) => $"{nameof(Input)}.{nameof(MemberInputModel.Login)}",
                nameof(IdentityErrorDescriber.InvalidUserName) => $"{nameof(Input)}.{nameof(MemberInputModel.Login)}",
                nameof(IdentityErrorDescriber.DuplicateEmail) => $"{nameof(Input)}.{nameof(MemberInputModel.Email)}",
                _ => string.Empty
            };

            var message = error.Code switch
            {
                nameof(IdentityErrorDescriber.DuplicateUserName) => AppLocalizer.Get("Пользователь с таким логином уже существует.", "Користувач із таким логіном уже існує.", "A user with this login already exists."),
                nameof(IdentityErrorDescriber.InvalidUserName) => AppLocalizer.Get("Укажите корректный логин.", "Вкажіть коректний логін.", "Enter a valid login."),
                nameof(IdentityErrorDescriber.DuplicateEmail) => AppLocalizer.Get("Пользователь с таким адресом электронной почты уже существует.", "Користувач із такою адресою електронної пошти вже існує.", "A user with this email address already exists."),
                _ => string.IsNullOrWhiteSpace(error.Description)
                    ? AppLocalizer.Get("Не удалось сохранить данные учетной записи.", "Не вдалося зберегти дані облікового запису.", "Could not save the account data.")
                    : error.Description
            };

            ModelState.AddModelError(key, message);
        }
    }

    private static (string FirstName, string LastName, string DisplayName) ParseFullName(string fullName)
    {
        var trimmedFullName = fullName.Trim();
        var nameParts = trimmedFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (nameParts.Length == 0)
        {
            return ("Член", "Товарищества", "Член товарищества");
        }

        if (nameParts.Length == 1)
        {
            return (TrimToLength(nameParts[0], 100), "Товарищества", trimmedFullName);
        }

        var firstName = TrimToLength(string.Join(' ', nameParts[..^1]), 100);
        var lastName = TrimToLength(nameParts[^1], 100);

        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = "Член";
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            lastName = "Товарищества";
        }

        return (firstName, lastName, trimmedFullName);
    }

    private static string TrimToLength(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    private sealed record LinkedAccountContext(bool Exists, string? DisplayName);
}
