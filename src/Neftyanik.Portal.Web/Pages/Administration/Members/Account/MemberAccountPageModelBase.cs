using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Security;

namespace Neftyanik.Portal.Web.Pages.Administration.Members.Account;

[Authorize(Roles = RoleNames.Administrator)]
public abstract class MemberAccountPageModelBase : PageModel
{
    protected MemberAccountPageModelBase(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        DbContext = dbContext;
        UserManager = userManager;
    }

    protected ApplicationDbContext DbContext { get; }

    protected UserManager<ApplicationUser> UserManager { get; }

    protected async Task<MemberContextViewModel?> GetMemberContextAsync(int memberId, CancellationToken cancellationToken)
    {
        return await DbContext.Members
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => new MemberContextViewModel
            {
                Id = member.Id,
                FullName = member.FullName,
                Email = member.Email,
                PhoneNumber = member.PhoneNumber,
                IsActive = member.IsActive,
                ApplicationUserId = member.ApplicationUserId
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    protected static (string FirstName, string LastName, string DisplayName) ParseFullName(string fullName)
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

    protected static string TrimToLength(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd();
    }

    protected void AddIdentityErrors(IdentityResult result, string emailKey, string passwordKey, string? currentPasswordKey = null)
    {
        IdentityErrorLocalizer.AddErrors(ModelState, result, emailKey, passwordKey, currentPasswordKey);
    }

    public sealed class MemberContextViewModel
    {
        public int Id { get; init; }

        public string FullName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? PhoneNumber { get; init; }

        public bool IsActive { get; init; }

        public string? ApplicationUserId { get; init; }
    }
}
