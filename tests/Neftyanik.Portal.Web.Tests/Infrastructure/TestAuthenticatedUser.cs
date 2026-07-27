namespace Neftyanik.Portal.Web.Tests.Infrastructure;

public sealed record TestAuthenticatedUser(string UserId, params string[] Roles);
