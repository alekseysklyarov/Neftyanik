using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Neftyanik.Portal.Application.Exceptions;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Interfaces;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;

namespace Neftyanik.Portal.Infrastructure.Identity;

public class AdminBootstrapService : IAdminBootstrapService
{
    private const string DefaultFirstName = "Local";
    private const string DefaultLastName = "Administrator";
    private const int FirstNameMaxLength = 100;
    private const int LastNameMaxLength = 100;
    private const int DisplayNameMaxLength = 200;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly ILogger<AdminBootstrapService> _logger;

    public AdminBootstrapService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ILogger<AdminBootstrapService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<AdminBootstrapResult> CreateAdministratorAsync(AdminBootstrapRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateRequest(request);

        var email = request.Email!.Trim();
        var password = request.Password!;

        await EnsureAdministratorRoleAsync();

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            return await HandleExistingUserAsync(existingUser, request.AllowExistingUserRoleAssignment);
        }

        var name = ResolveName(request.Name);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = name.FirstName,
            LastName = name.LastName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            DisplayName = name.DisplayName
        };

        var createResult = await _userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            var recoveredResult = await TryResolveDuplicateUserAsync(email, request.AllowExistingUserRoleAssignment, createResult);
            if (recoveredResult is not null)
            {
                return recoveredResult;
            }

            throw CreateException("Failed to create the administrator user.", createResult);
        }

        var addToRoleResult = await _userManager.AddToRoleAsync(user, RoleNames.Administrator);
        if (!addToRoleResult.Succeeded)
        {
            await TryDeleteUserAsync(user);
            throw CreateException($"Failed to assign the '{RoleNames.Administrator}' role to the administrator user.", addToRoleResult);
        }

        _logger.LogInformation("Administrator account {Email} was created and assigned the {RoleName} role.", email, RoleNames.Administrator);
        return new AdminBootstrapResult(AdminBootstrapOutcome.Created, "Administrator account was created successfully.");
    }

    private async Task<AdminBootstrapResult> HandleExistingUserAsync(ApplicationUser user, bool allowExistingUserRoleAssignment)
    {
        if (await _userManager.IsInRoleAsync(user, RoleNames.Administrator))
        {
            _logger.LogInformation("Administrator creation command found that user {Email} already has the {RoleName} role.", user.Email, RoleNames.Administrator);
            return new AdminBootstrapResult(AdminBootstrapOutcome.AlreadyAdministrator, "Administrator account already exists.");
        }

        if (!allowExistingUserRoleAssignment)
        {
            throw new AdminBootstrapException($"A user with email '{user.Email}' already exists but does not have the '{RoleNames.Administrator}' role. Re-run the command with '--allow-existing-user-role-assignment' to assign that role without changing the password.");
        }

        var addToRoleResult = await _userManager.AddToRoleAsync(user, RoleNames.Administrator);
        if (!addToRoleResult.Succeeded)
        {
            if (await _userManager.IsInRoleAsync(user, RoleNames.Administrator))
            {
                return new AdminBootstrapResult(AdminBootstrapOutcome.AlreadyAdministrator, "Administrator account already exists.");
            }

            throw CreateException($"Failed to assign the '{RoleNames.Administrator}' role to the existing user.", addToRoleResult);
        }

        _logger.LogInformation("Administrator role was assigned to existing user {Email} after explicit confirmation.", user.Email);
        return new AdminBootstrapResult(AdminBootstrapOutcome.RoleAssignedToExistingUser, "Administrator role was assigned to the existing user.");
    }

    private async Task EnsureAdministratorRoleAsync()
    {
        if (await _roleManager.RoleExistsAsync(RoleNames.Administrator))
        {
            return;
        }

        var createRoleResult = await _roleManager.CreateAsync(new IdentityRole(RoleNames.Administrator));
        if (createRoleResult.Succeeded || await _roleManager.RoleExistsAsync(RoleNames.Administrator))
        {
            return;
        }

        throw CreateException($"Failed to create the '{RoleNames.Administrator}' role.", createRoleResult);
    }

    private async Task<AdminBootstrapResult?> TryResolveDuplicateUserAsync(string email, bool allowExistingUserRoleAssignment, IdentityResult createResult)
    {
        if (!ContainsDuplicateUserError(createResult))
        {
            return null;
        }

        var existingUser = await _userManager.FindByEmailAsync(email);
        if (existingUser is null)
        {
            return null;
        }

        return await HandleExistingUserAsync(existingUser, allowExistingUserRoleAssignment);
    }

    private static void ValidateRequest(AdminBootstrapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new AdminBootstrapException("Environment variable 'NEFTYANIK_ADMIN_EMAIL' is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new AdminBootstrapException("Environment variable 'NEFTYANIK_ADMIN_PASSWORD' is required.");
        }

        var email = request.Email.Trim();
        if (!new EmailAddressAttribute().IsValid(email))
        {
            throw new AdminBootstrapException("Environment variable 'NEFTYANIK_ADMIN_EMAIL' must contain a valid email address.");
        }
    }

    private static AdminName ResolveName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new AdminName(DefaultFirstName, DefaultLastName, $"{DefaultFirstName} {DefaultLastName}");
        }

        var displayName = name.Trim();
        if (displayName.Length > DisplayNameMaxLength)
        {
            throw new AdminBootstrapException($"Environment variable 'NEFTYANIK_ADMIN_NAME' must not exceed {DisplayNameMaxLength} characters.");
        }

        var nameParts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (nameParts.Length == 0)
        {
            return new AdminName(DefaultFirstName, DefaultLastName, $"{DefaultFirstName} {DefaultLastName}");
        }

        if (nameParts.Length == 1)
        {
            if (nameParts[0].Length > FirstNameMaxLength)
            {
                throw new AdminBootstrapException($"Environment variable 'NEFTYANIK_ADMIN_NAME' must fit within the user name length limits.");
            }

            return new AdminName(nameParts[0], DefaultLastName, displayName);
        }

        var firstName = string.Join(' ', nameParts[..^1]);
        var lastName = nameParts[^1];

        if (firstName.Length > FirstNameMaxLength || lastName.Length > LastNameMaxLength)
        {
            throw new AdminBootstrapException($"Environment variable 'NEFTYANIK_ADMIN_NAME' must fit within the user name length limits.");
        }

        return new AdminName(firstName, lastName, displayName);
    }

    private static AdminBootstrapException CreateException(string message, IdentityResult result)
    {
        return new AdminBootstrapException($"{message} {string.Join("; ", result.Errors.Select(x => x.Description))}");
    }

    private static bool ContainsDuplicateUserError(IdentityResult result)
    {
        return result.Errors.Any(error =>
            string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateEmail), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(error.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.OrdinalIgnoreCase));
    }

    private async Task TryDeleteUserAsync(ApplicationUser user)
    {
        var deleteResult = await _userManager.DeleteAsync(user);
        if (!deleteResult.Succeeded)
        {
            _logger.LogWarning("Administrator user cleanup failed after role assignment failure for user {Email}.", user.Email);
        }
    }

    private sealed record AdminName(string FirstName, string LastName, string DisplayName);
}