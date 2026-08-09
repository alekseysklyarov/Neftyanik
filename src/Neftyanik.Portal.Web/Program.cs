using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.CookiePolicy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Neftyanik.Portal.Application.Exceptions;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Application.Interfaces;
using Neftyanik.Portal.Application.LegacyImport;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Localization;
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

var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];

builder.Services.AddRazorPages(options =>
{
    options.RootDirectory = razorPagesRootDirectory;
});
builder.Services.AddControllersWithViews();
builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 443;
});

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName("Neftyanik.Portal");

if (!string.IsNullOrWhiteSpace(dataProtectionKeysDirectory))
{
    dataProtectionBuilder.PersistKeysToFileSystem(Directory.CreateDirectory(dataProtectionKeysDirectory));
}

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
    var requireSecureCookies = IsHttpsRequired(builder.Configuration, builder.Environment);
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
});

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    var requireSecureCookies = IsHttpsRequired(builder.Configuration, builder.Environment);
    options.HttpOnly = HttpOnlyPolicy.Always;
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAntiforgery(options =>
{
    var requireSecureCookies = IsHttpsRequired(builder.Configuration, builder.Environment);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.Configure<ForwardedHeadersOptions>(options => ConfigureForwardedHeaders(options, builder.Configuration));

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdministrator", policy => policy.RequireRole(RoleNames.Administrator));
    options.AddPolicy("RequireAccountant", policy => policy.RequireRole(RoleNames.Accountant));
    options.AddPolicy("RequireMember", policy => policy.RequireRole(RoleNames.Member));
});

var app = builder.Build();
var requireHttps = IsHttpsRequired(app.Configuration, app.Environment);

if (IsLegacyElectricityImportCommand(args))
{
    return await ExecuteLegacyElectricityImportCommandAsync(app, args);
}

if (IsCreateAdminCommand(args))
{
    return await ExecuteCreateAdminCommandAsync(app, args[1..]);
}

if (IsMigrateDatabaseCommand(args))
{
    return await ExecuteMigrateDatabaseCommandAsync(app, args[1..]);
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

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");

    if (requireHttps)
    {
        app.UseHsts();
    }
}

if (requireHttps)
{
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments("/health"),
        branch => branch.UseHttpsRedirection());
}

app.UseCookiePolicy();
app.UseRequestLocalization(LocalizationConfiguration.CreateOptions());
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapGet("/health", () => Results.Text("OK", "text/plain")).AllowAnonymous();
app.MapRazorPages();

await app.RunAsync();
return 0;

static bool IsCreateAdminCommand(string[] arguments)
{
    return arguments.Length > 0
        && string.Equals(arguments[0], "create-admin", StringComparison.OrdinalIgnoreCase);
}

static bool IsMigrateDatabaseCommand(string[] arguments)
{
    return arguments.Length > 0
        && string.Equals(arguments[0], "migrate-database", StringComparison.OrdinalIgnoreCase);
}

static bool IsLegacyElectricityImportCommand(string[] arguments)
{
    return arguments.Any(argument => string.Equals(argument, "--import-legacy-electricity", StringComparison.OrdinalIgnoreCase));
}

static bool IsHttpsRequired(IConfiguration configuration, IWebHostEnvironment environment)
{
    return configuration.GetValue<bool?>("Security:RequireHttps")
        ?? environment.IsProduction();
}

static async Task<int> ExecuteLegacyElectricityImportCommandAsync(WebApplication app, string[] arguments)
{
    if (!app.Environment.IsDevelopment())
    {
        Console.Error.WriteLine("Legacy electricity import can only run in the Development environment.");
        return 1;
    }

    try
    {
        var request = CreateLegacyElectricityImportRequest(arguments);

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        var service = scope.ServiceProvider.GetRequiredService<ILegacyElectricityImportService>();
        var result = await service.ExecuteAsync(request);

        Console.WriteLine(result.SummaryMessage);
        Console.WriteLine($"Workbook: {result.WorkbookPath}");
        Console.WriteLine($"Markdown report: {result.MarkdownReportPath}");
        Console.WriteLine($"JSON report: {result.JsonReportPath}");
        Console.WriteLine($"Issues: {result.Issues.Count}");
        return 0;
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Legacy electricity import command failed.");
        Console.Error.WriteLine(exception.Message);
        return 1;
    }
}

static async Task<int> ExecuteMigrateDatabaseCommandAsync(WebApplication app, string[] commandArguments)
{
    try
    {
        ValidateMigrateDatabaseCommandArguments(commandArguments);

        using var scope = app.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();

        Console.WriteLine("Database migrations applied successfully.");
        return 0;
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Database migration command failed.");
        Console.Error.WriteLine("Database migration failed.");
        return 1;
    }
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

static LegacyElectricityImportRequest CreateLegacyElectricityImportRequest(string[] arguments)
{
    var supportedOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "--import-legacy-electricity",
        "--dry-run",
        "--commit",
        "--force"
    };

    var commit = arguments.Any(argument => string.Equals(argument, "--commit", StringComparison.OrdinalIgnoreCase));
    var dryRun = arguments.Any(argument => string.Equals(argument, "--dry-run", StringComparison.OrdinalIgnoreCase));
    var force = arguments.Any(argument => string.Equals(argument, "--force", StringComparison.OrdinalIgnoreCase));
    if (commit && dryRun)
    {
        throw new InvalidOperationException("Specify either --dry-run or --commit, but not both.");
    }

    var defaultPreviousReadingDate = ParseDateOption(arguments, "--default-previous-reading-date");
    var defaultCurrentReadingDate = ParseDateOption(arguments, "--default-current-reading-date");
    var ownershipEffectiveFrom = ParseDateOption(arguments, "--ownership-effective-from");
    var workbookRelativePath = ParseStringOption(arguments, "--workbook-path");
    var reportsRelativePath = ParseStringOption(arguments, "--reports-path");

    foreach (var argument in arguments.Where(argument => argument.StartsWith("--", StringComparison.OrdinalIgnoreCase)))
    {
        var optionName = argument.Split('=', 2)[0];
        if (!supportedOptions.Contains(optionName)
            && !string.Equals(optionName, "--default-previous-reading-date", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(optionName, "--default-current-reading-date", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(optionName, "--ownership-effective-from", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(optionName, "--workbook-path", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(optionName, "--reports-path", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown option '{optionName}'.");
        }
    }

    return new LegacyElectricityImportRequest(
        commit,
        force,
        defaultPreviousReadingDate,
        defaultCurrentReadingDate,
        ownershipEffectiveFrom,
        workbookRelativePath,
        reportsRelativePath);
}

static DateOnly? ParseDateOption(string[] arguments, string optionName)
{
    var value = ParseStringOption(arguments, optionName);
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
    {
        throw new InvalidOperationException($"Option '{optionName}' must use yyyy-MM-dd format.");
    }

    return parsedDate;
}

static string? ParseStringOption(string[] arguments, string optionName)
{
    var argument = arguments.FirstOrDefault(value => value.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase));
    if (argument is null)
    {
        return null;
    }

    return argument[(optionName.Length + 1)..];
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

static void ValidateMigrateDatabaseCommandArguments(string[] commandArguments)
{
    if (commandArguments.Length > 0)
    {
        throw new InvalidOperationException($"Unknown option(s): {string.Join(", ", commandArguments)}.");
    }
}

static void ConfigureForwardedHeaders(ForwardedHeadersOptions options, IConfiguration configuration)
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    foreach (var proxyAddress in GetConfiguredProxyAddresses(configuration))
    {
        options.KnownProxies.Add(proxyAddress);
    }

    foreach (var network in GetConfiguredProxyNetworks(configuration))
    {
        options.KnownNetworks.Add(network);
    }
}

static IReadOnlyList<IPAddress> GetConfiguredProxyAddresses(IConfiguration configuration)
{
    var configuredValues = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
    var addresses = new List<IPAddress>(configuredValues.Length);

    foreach (var configuredValue in configuredValues)
    {
        if (!IPAddress.TryParse(configuredValue, out var parsedAddress))
        {
            throw new InvalidOperationException($"Reverse proxy IP address '{configuredValue}' is invalid.");
        }

        addresses.Add(parsedAddress);
    }

    return addresses;
}

static IReadOnlyList<Microsoft.AspNetCore.HttpOverrides.IPNetwork> GetConfiguredProxyNetworks(IConfiguration configuration)
{
    var configuredValues = configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? Array.Empty<string>();
    var networks = new List<Microsoft.AspNetCore.HttpOverrides.IPNetwork>(configuredValues.Length);

    foreach (var configuredValue in configuredValues)
    {
        var segments = configuredValue.Split('/', 2, StringSplitOptions.TrimEntries);
        if (segments.Length != 2 || !IPAddress.TryParse(segments[0], out var prefixAddress) || !int.TryParse(segments[1], out var prefixLength))
        {
            throw new InvalidOperationException($"Reverse proxy network '{configuredValue}' must use CIDR notation.");
        }

        var maxPrefixLength = prefixAddress.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw new InvalidOperationException($"Reverse proxy network '{configuredValue}' has an invalid prefix length.");
        }

        networks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefixAddress, prefixLength));
    }

    return networks;
}

public partial class Program
{
}
