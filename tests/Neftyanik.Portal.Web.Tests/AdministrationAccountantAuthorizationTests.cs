#if WEB_TESTS
using System.Net;
using Neftyanik.Portal.Domain.Constants;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationAccountantAuthorizationTests
{
    [Fact]
    public async Task GetAdministrationMembersIndex_ForAccountant_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationMemberCreate_ForAccountant_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Members/Create");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationPlotOwnerships_ForAccountant_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Plots/Ownerships?plotId=1");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }
}
#endif
