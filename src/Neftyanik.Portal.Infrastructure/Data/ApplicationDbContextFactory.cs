using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Neftyanik.Portal.Infrastructure.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? "Production";
        var webProjectPath = ResolveWebProjectPath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(webProjectPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found in the Web project configuration.");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static string ResolveWebProjectPath()
    {
        foreach (var startPath in GetCandidateStartPaths())
        {
            var resolvedPath = TryResolveWebProjectPath(startPath);
            if (resolvedPath is not null)
            {
                return resolvedPath;
            }
        }

        throw new InvalidOperationException("Unable to locate the Web project path 'src/Neftyanik.Portal.Web' for design-time configuration.");
    }

    private static IEnumerable<string> GetCandidateStartPaths()
    {
        yield return Directory.GetCurrentDirectory();
        yield return AppContext.BaseDirectory;
        yield return Path.GetDirectoryName(typeof(ApplicationDbContextFactory).Assembly.Location)!;
    }

    private static string? TryResolveWebProjectPath(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));

        while (directory is not null)
        {
            var directWebProjectPath = Path.Combine(directory.FullName, "src", "Neftyanik.Portal.Web");
            if (IsWebProjectPath(directWebProjectPath))
            {
                return directWebProjectPath;
            }

            if (IsWebProjectPath(directory.FullName))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool IsWebProjectPath(string path)
    {
        return File.Exists(Path.Combine(path, "Neftyanik.Portal.Web.csproj"))
            && File.Exists(Path.Combine(path, "appsettings.json"));
    }
}