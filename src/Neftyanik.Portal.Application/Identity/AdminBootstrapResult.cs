namespace Neftyanik.Portal.Application.Identity;

public sealed record AdminBootstrapResult(AdminBootstrapOutcome Outcome, string Message);

public enum AdminBootstrapOutcome
{
    Created,
    AlreadyAdministrator,
    RoleAssignedToExistingUser
}
