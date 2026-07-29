using Microsoft.AspNetCore.Identity;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Web.Security;

public sealed class SimplePasswordValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (!string.IsNullOrEmpty(password) && password.Any(char.IsLetter))
        {
            return Task.FromResult(IdentityResult.Success);
        }

        return Task.FromResult(IdentityResult.Failed(new IdentityError
        {
            Code = "PasswordRequiresLetter",
            Description = "Пароль должен содержать хотя бы одну букву."
        }));
    }
}
