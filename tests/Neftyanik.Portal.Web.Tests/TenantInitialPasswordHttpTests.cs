using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class TenantInitialPasswordHttpTests
{
    [Theory]
    [InlineData(RoleNames.Administrator, "/Administration", "https://evil.invalid/")]
    [InlineData(RoleNames.Accountant, "/Administration", "/neftyanik/Administration")]
    [InlineData(RoleNames.Member, "/Member", "/onboarding/Administration/Finance")]
    public async Task MustChangePassword_EnforcedOnEveryRequestAndEndsOnlyAfterPasswordChange(string role, string dashboard, string returnUrl)
    {
        await using var factory = new PortalWebApplicationFactory();
        await using var app = AuthenticationCookieTests.CreateCookieApplication(factory);
        var temporary = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        var replacement = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA2!";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var association = new Association { Slug = "onboarding", Name = "Onboarding" };
            db.Associations.Add(association);
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "initial-user", MustChangePassword = true };
            Assert.True((await users.CreateAsync(user, temporary)).Succeeded);
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = user.Id, Role = role });
            if (role == RoleNames.Member) db.Members.Add(new Member { FullName = "Test member", ApplicationUserId = user.Id });
            await db.SaveChangesAsync();
        }
        using var client = AuthenticationCookieTests.CreateBrowser(app, new CookieContainer());
        var loginToken = await AuthenticationCookieTests.TokenAsync(client, "/onboarding/Account/Login");
        using (var login = await client.PostAsync("/onboarding/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl), Form(loginToken,
            ("Input.Login", "initial-user"), ("Input.Password", temporary))))
        {
            Assert.Equal(HttpStatusCode.Found, login.StatusCode);
            Assert.Equal("/onboarding/Account/ChangeInitialPassword", login.Headers.Location!.OriginalString);
        }
        foreach (var path in new[] { "/Administration", "/Administration/Members", "/Administration/Finance", "/Administration/Finance/Expenses", "/Member", "/Account/Login" })
        {
            using var denied = await client.GetAsync("/onboarding" + path + "?ReturnUrl=https://evil.invalid/");
            Assert.Equal(HttpStatusCode.Found, denied.StatusCode);
            Assert.Equal("/onboarding/Account/ChangeInitialPassword", denied.Headers.Location!.OriginalString);
        }
        var changeToken = await AuthenticationCookieTests.TokenAsync(client, "/onboarding/Account/ChangeInitialPassword");
        using (var blockedPost = await client.PostAsync("/onboarding/Administration/Members/Create", Form(changeToken)))
        {
            Assert.Equal(HttpStatusCode.Found, blockedPost.StatusCode);
            Assert.Equal("/onboarding/Account/ChangeInitialPassword", blockedPost.Headers.Location!.OriginalString);
        }
        using (var logout = await client.PostAsync("/onboarding/Account/Logout", Form(changeToken)))
            Assert.Equal(HttpStatusCode.Found, logout.StatusCode);
        using (var signedOut = await client.GetAsync("/onboarding/Administration"))
            Assert.Contains("/onboarding/Account/Login", signedOut.Headers.Location!.OriginalString);
        loginToken = await AuthenticationCookieTests.TokenAsync(client, "/onboarding/Account/Login");
        using (var login = await client.PostAsync("/onboarding/Account/Login", Form(loginToken, ("Input.Login", "initial-user"), ("Input.Password", temporary))))
            Assert.Equal("/onboarding/Account/ChangeInitialPassword", login.Headers.Location!.OriginalString);
        changeToken = await AuthenticationCookieTests.TokenAsync(client, "/onboarding/Account/ChangeInitialPassword");
        using (var unchanged = await client.PostAsync("/onboarding/Account/ChangeInitialPassword", Form(changeToken,
            ("Input.CurrentPassword", temporary), ("Input.NewPassword", temporary), ("Input.ConfirmNewPassword", temporary))))
            Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        using (var changed = await client.PostAsync("/onboarding/Account/ChangeInitialPassword?ReturnUrl=" + Uri.EscapeDataString(returnUrl), Form(changeToken,
            ("Input.CurrentPassword", temporary), ("Input.NewPassword", replacement), ("Input.ConfirmNewPassword", replacement))))
        {
            Assert.Equal(HttpStatusCode.Found, changed.StatusCode);
            Assert.Equal("/onboarding" + dashboard, changed.Headers.Location!.OriginalString.TrimEnd('/'));
            using var destination = await client.GetAsync(changed.Headers.Location);
            Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        }
        using (var revisited = await client.GetAsync("/onboarding/Account/ChangeInitialPassword"))
            Assert.Equal("/onboarding" + dashboard, revisited.Headers.Location!.OriginalString.TrimEnd('/'));
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("initial-user"))!;
            Assert.False(user.MustChangePassword);
            Assert.False(await users.CheckPasswordAsync(user, temporary));
            Assert.True(await users.CheckPasswordAsync(user, replacement));
        }
        using var fresh = AuthenticationCookieTests.CreateBrowser(app, new CookieContainer());
        var token = await AuthenticationCookieTests.TokenAsync(fresh, "/onboarding/Account/Login");
        using (var rejected = await fresh.PostAsync("/onboarding/Account/Login", Form(token, ("Input.Login", "initial-user"), ("Input.Password", temporary))))
        {
            Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
            Assert.Null(rejected.Headers.Location);
        }
        using var accepted = await fresh.PostAsync("/onboarding/Account/Login", Form(token, ("Input.Login", "initial-user"), ("Input.Password", replacement)));
        Assert.Equal("/onboarding" + dashboard, accepted.Headers.Location!.OriginalString.TrimEnd('/'));
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields) =>
        new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)).Prepend(new("__RequestVerificationToken", token)));
}
