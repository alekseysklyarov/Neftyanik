using System.Net;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ElectricityAuthorizationTests
{
    [Fact]
    public async Task GetAssociationElectricityTariffs_AsAnonymous_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/Administration/Electricity/Association/Tariffs");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAssociationElectricityTariffs_AsMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-tariffs";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-tariffs@example.com"));
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync("/Administration/Electricity/Association/Tariffs");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAssociationElectricityPage_AsMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-association-electricity";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-association-electricity@example.com"));
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync("/Administration/Electricity/Association");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    private static ApplicationUser CreateUser(string id, string email)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Test",
            LastName = "User",
            MustChangePassword = false,
            IsActive = true
        };
    }
}
