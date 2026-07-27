using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Neftyanik.Portal.Web.Security;

internal static class IdentityErrorLocalizer
{
    public static void AddErrors(
        ModelStateDictionary modelState,
        IdentityResult result,
        string emailKey,
        string passwordKey,
        string? currentPasswordKey = null)
    {
        foreach (var error in result.Errors)
        {
            var (key, message) = Translate(error, emailKey, passwordKey, currentPasswordKey);
            modelState.AddModelError(key, message);
        }
    }

    private static (string Key, string Message) Translate(
        IdentityError error,
        string emailKey,
        string passwordKey,
        string? currentPasswordKey)
    {
        return error.Code switch
        {
            nameof(IdentityErrorDescriber.DuplicateEmail) => (emailKey, "Пользователь с таким адресом электронной почты уже существует."),
            nameof(IdentityErrorDescriber.DuplicateUserName) => (emailKey, "Пользователь с таким логином уже существует."),
            nameof(IdentityErrorDescriber.InvalidEmail) => (emailKey, "Введите корректный адрес электронной почты."),
            nameof(IdentityErrorDescriber.InvalidUserName) => (emailKey, "Укажите корректный логин."),
            nameof(IdentityErrorDescriber.PasswordTooShort) => (passwordKey, "Пароль слишком короткий."),
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => (passwordKey, "Пароль должен содержать хотя бы одну цифру."),
            nameof(IdentityErrorDescriber.PasswordRequiresLower) => (passwordKey, "Пароль должен содержать хотя бы одну строчную букву."),
            nameof(IdentityErrorDescriber.PasswordRequiresUpper) => (passwordKey, "Пароль должен содержать хотя бы одну заглавную букву."),
            nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => (passwordKey, "Пароль должен содержать хотя бы один специальный символ."),
            nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => (passwordKey, "Пароль должен содержать достаточное количество уникальных символов."),
            nameof(IdentityErrorDescriber.PasswordMismatch) when !string.IsNullOrWhiteSpace(currentPasswordKey) => (currentPasswordKey!, "Текущий пароль указан неверно."),
            _ => (string.Empty, "Не удалось выполнить операцию с учетной записью. Проверьте введенные данные и повторите попытку.")
        };
    }
}
