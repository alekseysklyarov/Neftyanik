namespace Neftyanik.Portal.Application.Identity;

public sealed record AdminBootstrapRequest(
    string? Email,
    string? Password,
    string? Name,
    bool AllowExistingUserRoleAssignment = false);
