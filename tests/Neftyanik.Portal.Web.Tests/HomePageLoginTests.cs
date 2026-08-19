using System.Net;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class HomePageLoginTests
{
    [Fact]
    public async Task GetHome_Anonymous_ShowsLoginFormAndNoHeaderLoginLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.Login\"", html);
        Assert.Contains("name=\"Input.Password\"", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("Что доступно в портале", html);
        Assert.DoesNotContain("href=\"/Account/Login\"", html);
        Assert.Contains("portal-feature-card", html);
    }

    [Fact]
    public async Task PostHome_WithInvalidCredentials_ShowsSameValidationMessage()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        using var response = await client.PostAsync("/", CreateLoginContent(token, "unknown", "wrong-password"));
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.Login\"", html);
        Assert.True(
            html.Contains("Неверный логин или пароль.", StringComparison.Ordinal)
            || html.Contains("Неправильний логін або пароль.", StringComparison.Ordinal)
            || html.Contains("Invalid login or password.", StringComparison.Ordinal),
            "Expected an invalid login message in one of the supported UI languages.");
    }

    [Fact]
    public async Task PostHome_WithValidCredentials_LogsInAndRedirectsToReturnUrl()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "member@example.com", "Pass123!");
        using var client = factory.CreateAnonymousClient();
        const string returnUrl = "/Member/Finance";
        var token = await GetAntiforgeryTokenAsync(client, $"/?ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        using var response = await client.PostAsync("/", CreateLoginContent(token, "member@example.com", "Pass123!", returnUrl));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostHome_WithAdministratorCredentials_WithoutReturnUrl_RedirectsToAdministration()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "admin@example.com", "Pass123!", RoleNames.Administrator);
        using var client = factory.CreateAnonymousClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        using var response = await client.PostAsync("/", CreateLoginContent(token, "admin@example.com", "Pass123!"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Administration", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostHome_WithMemberCredentials_WithoutReturnUrl_RedirectsToMember()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "member-default@example.com", "Pass123!");
        using var client = factory.CreateAnonymousClient();
        var token = await GetAntiforgeryTokenAsync(client, "/");

        using var response = await client.PostAsync("/", CreateLoginContent(token, "member-default@example.com", "Pass123!"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Member", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetAccountLogin_StillDisplaysSharedLoginForm()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Account/Login");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"Input.Login\"", html);
        Assert.Contains("name=\"Input.Password\"", html);
        Assert.Contains("name=\"Input.RememberMe\"", html);
        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task PostAccountLogin_WithValidCredentials_StillRedirectsToReturnUrl()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "admin@example.com", "Pass123!");
        using var client = factory.CreateAnonymousClient();
        const string returnUrl = "/Member/Index";
        var token = await GetAntiforgeryTokenAsync(client, $"/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        using var response = await client.PostAsync("/Account/Login", CreateLoginContent(token, "admin@example.com", "Pass123!", returnUrl, rememberMe: true));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetHome_WhenAdministratorIsAuthenticated_RedirectsToAdministration()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "admin-get@example.com", "Pass123!", RoleNames.Administrator, "admin-user");
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("admin-user", RoleNames.Administrator));

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Administration", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetHome_WhenMemberIsAuthenticated_RedirectsToMember()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "member-get@example.com", "Pass123!", RoleNames.Member, "member-user");
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("member-user", RoleNames.Member));

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Member", response.Headers.Location?.OriginalString);
    }

    private static async Task CreateUserAsync(PortalWebApplicationFactory factory, string email, string password, string? role = null, string? userId = null)
    {
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = new ApplicationUser
            {
                Id = userId ?? Guid.NewGuid().ToString("N"),
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                EmailConfirmed = true,
                SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                FirstName = "Test",
                LastName = "User",
                MustChangePassword = false,
                IsActive = true
            };

            user.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(user, password);
            dbContext.Users.Add(user);

            if (!string.IsNullOrWhiteSpace(role))
            {
                var normalizedRoleName = role.ToUpperInvariant();
                var existingRole = await dbContext.Roles.FirstOrDefaultAsync(r => r.NormalizedName == normalizedRoleName);

                if (existingRole is null)
                {
                    existingRole = new IdentityRole(role)
                    {
                        NormalizedName = normalizedRoleName
                    };

                    dbContext.Roles.Add(existingRole);
                }

                dbContext.UserRoles.Add(new IdentityUserRole<string>
                {
                    UserId = user.Id,
                    RoleId = existingRole.Id
                });
            }

            await dbContext.SaveChangesAsync();
        });
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<value>[^\"]+)\"");

        Assert.True(match.Success, $"Antiforgery token not found in response for '{url}'.");
        return match.Groups["value"].Value;
    }

    private static FormUrlEncodedContent CreateLoginContent(
        string antiforgeryToken,
        string login,
        string password,
        string? returnUrl = null,
        bool rememberMe = false)
    {
        var formValues = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", antiforgeryToken),
            new("Input.Login", login),
            new("Input.Password", password),
            new("ReturnUrl", returnUrl ?? string.Empty)
        };

        if (rememberMe)
        {
            formValues.Add(new("Input.RememberMe", "true"));
        }

        return new FormUrlEncodedContent(formValues);
    }
}
