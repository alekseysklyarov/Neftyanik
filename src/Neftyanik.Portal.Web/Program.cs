using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Exceptions;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Interfaces;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Security;

var builder = WebApplication.CreateBuilder(args);

var webProjectPath = Path.Combine(builder.Environment.ContentRootPath, "src", "Neftyanik.Portal.Web");
if (Directory.Exists(webProjectPath))
{
    builder.Configuration
        .AddJsonFile(Path.Combine(webProjectPath, "appsettings.json"), optional: true, reloadOnChange: true)
        .AddJsonFile(Path.Combine(webProjectPath, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true);
}

var razorPagesRootDirectory = Directory.Exists(Path.Combine(builder.Environment.ContentRootPath, "src", "Neftyanik.Portal.Web", "Pages"))
    ? "/src/Neftyanik.Portal.Web/Pages"
    : "/Pages";

builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = razorPagesRootDirectory;
});
builder.Services.AddControllersWithViews();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddPasswordValidator<SimplePasswordValidator>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator", policy => policy.RequireRole(RoleNames.Administrator));
    options.AddPolicy("RequireAccountant", policy => policy.RequireRole(RoleNames.Accountant));
    options.AddPolicy("RequireMember", policy => policy.RequireRole(RoleNames.Member));
});

var app = builder.Build();

if (IsCreateAdminCommand(args))
{
    return await ExecuteCreateAdminCommandAsync(app, args[1..]);
}

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    if (app.Environment.IsDevelopment())
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }
    else
    {
        logger.LogInformation("Automatic database migration is disabled outside the Development environment.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

await app.RunAsync();
return 0;

static bool IsCreateAdminCommand(string[] arguments)
{
    return arguments.Length > 0
        && string.Equals(arguments[0], "create-admin", StringComparison.OrdinalIgnoreCase);
}

static async Task<int> ExecuteCreateAdminCommandAsync(WebApplication app, string[] commandArguments)
{
    try
    {
        ValidateCreateAdminCommandArguments(commandArguments);

        using var scope = app.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IAdminBootstrapService>();
        var result = await service.CreateAdministratorAsync(CreateAdminRequest(commandArguments));

        Console.WriteLine(result.Message);
        return 0;
    }
    catch (AdminBootstrapException exception)
    {
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
    catch
    {
        app.Logger.LogError("Administrator creation command failed unexpectedly.");
        Console.Error.WriteLine("Administrator creation failed due to an unexpected error.");
        return 1;
    }
}

static AdminBootstrapRequest CreateAdminRequest(string[] commandArguments)
{
    return new AdminBootstrapRequest(
        Environment.GetEnvironmentVariable("NEFTYANIK_ADMIN_EMAIL"),
        Environment.GetEnvironmentVariable("NEFTYANIK_ADMIN_PASSWORD"),
        Environment.GetEnvironmentVariable("NEFTYANIK_ADMIN_NAME"),
        commandArguments.Any(argument => string.Equals(argument, "--allow-existing-user-role-assignment", StringComparison.OrdinalIgnoreCase)));
}

static void ValidateCreateAdminCommandArguments(string[] commandArguments)
{
    var supportedOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--allow-existing-user-role-assignment"
    };

    var unknownOptions = commandArguments
        .Where(argument => !supportedOptions.Contains(argument))
        .ToArray();

    if (unknownOptions.Length > 0)
    {
        throw new AdminBootstrapException($"Unknown option(s): {string.Join(", ", unknownOptions)}.");
    }
}

public partial class Program
{
}
