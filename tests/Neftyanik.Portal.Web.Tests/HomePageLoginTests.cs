using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
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

    private static async Task CreateUserAsync(PortalWebApplicationFactory factory, string email, string password)
    {
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
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
