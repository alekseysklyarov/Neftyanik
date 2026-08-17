using System.Net;
using Neftyanik.Portal.Domain.Constants;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationUserActivityPageTests
{
    [Fact]
    public async Task GetAdministrationUserActivity_ForAdministrator_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("administrator-user", RoleNames.Administrator));

        var response = await client.GetAsync("/Administration/UserActivity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationUserActivity_ForAccountant_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/UserActivity");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationDashboard_ForAdministrator_ContainsUserActivityLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("administrator-user", RoleNames.Administrator));

        var response = await client.GetAsync("/Administration");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Administration/UserActivity", content, StringComparison.Ordinal);
    }
}
