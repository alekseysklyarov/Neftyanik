using System.Net;
using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class HomePageLoginTests
{
    [Theory]
    [InlineData("neftyanik", "ru-RU", "Главная", "Вход")]
    [InlineData("demo", "ru-RU", "Главная", "Вход")]
    [InlineData("neftyanik", "uk-UA", "Головна", "Вхід")]
    [InlineData("demo", "uk-UA", "Головна", "Вхід")]
    [InlineData("neftyanik", "en-US", "Home", "Log in")]
    [InlineData("demo", "en-US", "Home", "Log in")]
    public async Task GetTenantPages_UsesAssociationNameAndPreservesLocalizedPageTitle(
        string slug, string culture, string homeTitle, string loginTitle)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async database =>
        {
            var neftyanik = await database.Associations.SingleAsync(x => x.Slug == "neftyanik");
            neftyanik.Name = "Нефтяник — тест";
            database.Associations.Add(new Association { Slug = "demo", Name = "Demo", IsActive = true });
            await database.SaveChangesAsync();
        });
        using var client = factory.CreateAnonymousClient(cultureName: culture);
        var name = slug == "demo" ? "Demo" : "Нефтяник — тест";

        foreach (var (path, title) in new[] { ($"/{slug}/", homeTitle), ($"/{slug}/Account/Login", loginTitle) })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            AssertBranding(html, name, title);
            if (path == $"/{slug}/")
            {
                Assert.Matches($"<h1\\b[^>]*class=\"portal-hero-title mt-3 mb-3\"[^>]*>{Regex.Escape(HtmlEncoder.Default.Encode(name))}</h1>", html);
            }
        }
    }

    [Theory]
    [InlineData("/demo/", "Home")]
    [InlineData("/demo/Account/Login", "Log in")]
    public async Task GetTenantPages_EncodesAssociationNameAsText(string path, string title)
    {
        const string name = "Demo & <script>alert(\"branding\")</script> 'garden'";
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async database =>
        {
            database.Associations.Add(new Association { Slug = "demo", Name = name, IsActive = true });
            await database.SaveChangesAsync();
        });
        using var client = factory.CreateAnonymousClient(cultureName: "en-US");

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        AssertBranding(html, name, title);
        Assert.DoesNotContain(name, html);
        Assert.DoesNotContain("<script>alert", html);
        if (path == "/demo/")
        {
            Assert.Matches($"<h1\\b[^>]*class=\"portal-hero-title mt-3 mb-3\"[^>]*>{Regex.Escape(HtmlEncoder.Default.Encode(name))}</h1>", html);
        }
    }

    [Fact]
    public async Task GetError_WithoutResolvedAssociation_UsesNeutralBranding()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/Error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertBranding(await response.Content.ReadAsStringAsync(), "DachaHub", "Error");
    }

    [Fact]
    public async Task GetRoot_PreservesRedirectToNeftyanik()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/neftyanik/", response.Headers.Location?.OriginalString);
    }

    private static void AssertBranding(string html, string name, string pageTitle)
    {
        var encodedName = HtmlEncoder.Default.Encode(name);
        Assert.Equal(encodedName, Regex.Match(html, "<span\\b[^>]*class=\"portal-brand-title\"[^>]*>(.*?)</span>").Groups[1].Value);
        Assert.Equal($"{HtmlEncoder.Default.Encode(pageTitle)} - {encodedName}", Regex.Match(html, "<title>(.*?)</title>").Groups[1].Value);
        var footer = Regex.Match(html, "<footer\\b[^>]*>(.*?)</footer>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains($"&copy; 2026 - {encodedName}", footer);
    }

    [Fact]
    public async Task GetHome_Anonymous_ShowsLoginFormAndNoHeaderLoginLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/neftyanik/");
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
        var token = await GetAntiforgeryTokenAsync(client, "/neftyanik/");

        using var response = await client.PostAsync("/neftyanik/", CreateLoginContent(token, "unknown", "wrong-password"));
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
        const string returnUrl = "/neftyanik/Member/Finance";
        var token = await GetAntiforgeryTokenAsync(client, $"/neftyanik/?ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        using var response = await client.PostAsync("/neftyanik/", CreateLoginContent(token, "member@example.com", "Pass123!", returnUrl));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostHome_WithAdministratorCredentials_WithoutReturnUrl_RedirectsToAdministration()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "admin@example.com", "Pass123!", RoleNames.Administrator);
        using var client = factory.CreateAnonymousClient();
        var token = await GetAntiforgeryTokenAsync(client, "/neftyanik/");

        using var response = await client.PostAsync("/neftyanik/", CreateLoginContent(token, "admin@example.com", "Pass123!"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Administration", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task PostHome_WithMemberCredentials_WithoutReturnUrl_RedirectsToMember()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "member-default@example.com", "Pass123!");
        using var client = factory.CreateAnonymousClient();
        var token = await GetAntiforgeryTokenAsync(client, "/neftyanik/");

        using var response = await client.PostAsync("/neftyanik/", CreateLoginContent(token, "member-default@example.com", "Pass123!"));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Member", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetAccountLogin_StillDisplaysSharedLoginForm()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync("/neftyanik/Account/Login");
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
        const string returnUrl = "/neftyanik/Member/Index";
        var token = await GetAntiforgeryTokenAsync(client, $"/neftyanik/Account/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");

        using var response = await client.PostAsync("/neftyanik/Account/Login", CreateLoginContent(token, "admin@example.com", "Pass123!", returnUrl, rememberMe: true));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(returnUrl, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetHome_WhenAdministratorIsAuthenticated_RedirectsToAdministration()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "admin-get@example.com", "Pass123!", RoleNames.Administrator, "admin-user");
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("admin-user", RoleNames.Administrator));

        using var response = await client.GetAsync("/neftyanik/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Administration", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GetHome_WhenMemberIsAuthenticated_RedirectsToMember()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateUserAsync(factory, "member-get@example.com", "Pass123!", RoleNames.Member, "member-user");
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("member-user", RoleNames.Member));

        using var response = await client.GetAsync("/neftyanik/");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Member", response.Headers.Location?.OriginalString);
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
            dbContext.AssociationUserMemberships.Add(new AssociationUserMembership
            {
                ApplicationUserId = user.Id, Role = role ?? RoleNames.Member
            });

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
