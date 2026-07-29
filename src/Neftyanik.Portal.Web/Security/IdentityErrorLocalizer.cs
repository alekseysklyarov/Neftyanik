using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Neftyanik.Portal.Web.Localization;

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
            nameof(IdentityErrorDescriber.DuplicateEmail) => (emailKey, AppLocalizer.Get(
                "Пользователь с таким адресом электронной почты уже существует.",
                "Користувач із такою адресою електронної пошти вже існує.",
                "A user with this email address already exists.")),
            nameof(IdentityErrorDescriber.DuplicateUserName) => (emailKey, AppLocalizer.Get(
                "Пользователь с таким логином уже существует.",
                "Користувач із таким логіном уже існує.",
                "A user with this login already exists.")),
            nameof(IdentityErrorDescriber.InvalidUserName) => (emailKey, AppLocalizer.Get(
                "Укажите корректный логин.",
                "Вкажіть коректний логін.",
                "Enter a valid login.")),
            nameof(IdentityErrorDescriber.PasswordTooShort) => (passwordKey, AppLocalizer.Get(
                "Пароль слишком короткий.",
                "Пароль занадто короткий.",
                "The password is too short.")),
            nameof(IdentityErrorDescriber.PasswordRequiresDigit) => (passwordKey, AppLocalizer.Get(
                "Пароль должен содержать хотя бы одну цифру.",
                "Пароль має містити принаймні одну цифру.",
                "The password must contain at least one digit.")),
            nameof(IdentityErrorDescriber.PasswordRequiresLower) => (passwordKey, AppLocalizer.Get(
                "Пароль должен содержать хотя бы одну строчную букву.",
                "Пароль має містити принаймні одну малу літеру.",
                "The password must contain at least one lowercase letter.")),
            nameof(IdentityErrorDescriber.PasswordRequiresUpper) => (passwordKey, AppLocalizer.Get(
                "Пароль должен содержать хотя бы одну заглавную букву.",
                "Пароль має містити принаймні одну велику літеру.",
                "The password must contain at least one uppercase letter.")),
            nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric) => (passwordKey, AppLocalizer.Get(
                "Пароль должен содержать хотя бы один специальный символ.",
                "Пароль має містити принаймні один спеціальний символ.",
                "The password must contain at least one special character.")),
            nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars) => (passwordKey, AppLocalizer.Get(
                "Пароль должен содержать достаточное количество уникальных символов.",
                "Пароль має містити достатню кількість унікальних символів.",
                "The password must contain enough unique characters.")),
            nameof(IdentityErrorDescriber.PasswordMismatch) when !string.IsNullOrWhiteSpace(currentPasswordKey) => (currentPasswordKey!, AppLocalizer.Get(
                "Текущий пароль указан неверно.",
                "Поточний пароль указано неправильно.",
                "The current password is incorrect.")),
            _ => (string.Empty, string.IsNullOrWhiteSpace(error.Description)
                ? AppLocalizer.Get(
                    "Не удалось выполнить операцию с учетной записью. Проверьте введенные данные и повторите попытку.",
                    "Не вдалося виконати операцію з обліковим записом. Перевірте введені дані та повторіть спробу.",
                    "The account operation could not be completed. Check the entered data and try again.")
                : error.Description)
        };
    }
}
