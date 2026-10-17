using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Demo;

public static class PublicDemo
{
    public const string Scheme = "PublicDemo";
    public const string SelectorScheme = "PortalOrDemo";
    public const string SessionClaim = "dachahub:demo-session";
    public const string Prefix = "/Product/Try";
    public const int DurationMinutes = 30;
    public static bool IsRequest(HttpRequest request) => request.Path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);
    public static string UserId(string role) => "demo-" + role.ToLowerInvariant();
    public static bool IsDemoUser(string? id) => new[] { RoleNames.Member, RoleNames.Accountant, RoleNames.Administrator }.Any(role => UserId(role) == id);

    public static (string Role, string Path)? Screen(string screen) => screen switch
    {
        "member" => (RoleNames.Member, "/Member"),
        "electricity" => (RoleNames.Member, "/Member/Electricity"),
        "finance" => (RoleNames.Accountant, "/Administration/Finance"),
        "members" => (RoleNames.Administrator, "/Administration/Members"),
        "plots" => (RoleNames.Administrator, "/Administration/Plots"),
        "expenses" => (RoleNames.Accountant, "/Administration/Finance/Expenses"),
        _ => null
    };

    public static void AddServices(IServiceCollection services)
    {
        services.AddSingleton<DemoWorkspaceStore>();
        services.AddScoped<DemoRequestScope>();
        UseScopedDatabase(services);
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = SelectorScheme;
            options.DefaultChallengeScheme = SelectorScheme;
            options.DefaultForbidScheme = SelectorScheme;
        })
        .AddPolicyScheme(SelectorScheme, SelectorScheme, options => options.ForwardDefaultSelector = context =>
            context.RequestServices.GetRequiredService<DemoRequestScope>().IsActive || IsRequest(context.Request)
                ? Scheme : IdentityConstants.ApplicationScheme)
        .AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "DachaHub.Demo";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = false;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(DurationMinutes);
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.Redirect("/Product/Demo");
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });
    }

    // Kept separate so test hosts can retain the demo database override after replacing the normal database.
    public static void UseScopedDatabase(IServiceCollection services) => services.AddScoped(provider =>
    {
        var demo = provider.GetRequiredService<DemoRequestScope>();
        var options = demo.Workspace is { } workspace ? DemoData.Options(workspace.ConnectionString)
            : provider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        return new ApplicationDbContext(options, provider.GetRequiredService<IAssociationContext>());
    });

    public static ClaimsPrincipal Principal(string sessionId, string role) => new(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, UserId(role)), new Claim(ClaimTypes.Name, UserId(role)),
        new Claim(SessionClaim, sessionId)
    }, Scheme));
}

public sealed class DemoRequestScope
{
    public DemoWorkspace? Workspace { get; set; }
    public string OriginPathBase { get; set; } = string.Empty;
    public bool IsActive => Workspace is not null;
}
