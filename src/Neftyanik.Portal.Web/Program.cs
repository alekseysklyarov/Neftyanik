using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
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

if (IsLegacyElectricityImportCommand(args))
{
    return await ExecuteLegacyElectricityImportCommandAsync(app, args);
}

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
app.UseRequestLocalization(LocalizationConfiguration.CreateOptions());
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

static bool IsLegacyElectricityImportCommand(string[] arguments)
{
    return arguments.Any(argument => string.Equals(argument, "--import-legacy-electricity", StringComparison.OrdinalIgnoreCase));
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

public partial class Program
{
}
