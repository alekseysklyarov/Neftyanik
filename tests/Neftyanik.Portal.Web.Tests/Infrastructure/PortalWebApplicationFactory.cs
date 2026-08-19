using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PortalWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestConnectionString = "Server=(localdb)\\mssqllocaldb;Database=NeftyanikPortalTests;Trusted_Connection=True;TrustServerCertificate=True";
    private static readonly object CurrentDirectoryLock = new();
    private readonly IReadOnlyDictionary<string, string?> _additionalConfiguration;
    private readonly string _connectionString;
    private readonly string _environmentName;
    private readonly bool _useSqlite;
    private readonly string? _sqlServerDatabaseName;
    private SqliteConnection? _connection;

    public PortalWebApplicationFactory(
        string environmentName = "Testing",
        IReadOnlyDictionary<string, string?>? additionalConfiguration = null,
        bool useSqlite = true)
    {
        _environmentName = environmentName;
        _additionalConfiguration = additionalConfiguration ?? new Dictionary<string, string?>();
        _useSqlite = useSqlite;

        if (useSqlite)
        {
            _connectionString = TestConnectionString;
            return;
        }

        _sqlServerDatabaseName = $"NeftyanikPortalTests_{Guid.NewGuid():N}";
        _connectionString = $"Server=(localdb)\\mssqllocaldb;Database={_sqlServerDatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        builder.UseEnvironment(_environmentName);

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            var configuration = new Dictionary<string, string?>(_additionalConfiguration)
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString
            };

            configurationBuilder.AddInMemoryCollection(configuration);
        });

        builder.ConfigureServices(services =>
        {
            if (_useSqlite)
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();

                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlite(_connection));
            }

            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultForbidScheme = IdentityConstants.ApplicationScheme;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            if (_useSqlite)
            {
                using var scope = services.BuildServiceProvider().CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                dbContext.Database.EnsureCreated();
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (CurrentDirectoryLock)
        {
            var originalCurrentDirectory = Directory.GetCurrentDirectory();

            try
            {
                Directory.SetCurrentDirectory(ResolveRepositoryRootPath());
                return base.CreateHost(builder);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalCurrentDirectory);
            }
        }
    }

    public HttpClient CreateAnonymousClient(bool allowAutoRedirect = false, string? cultureName = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect
        });

        ApplyCulture(client, cultureName);
        return client;
    }

    public HttpClient CreateAuthenticatedClient(TestAuthenticatedUser user, bool allowAutoRedirect = false, string? cultureName = null)
    {
        var client = CreateAnonymousClient(allowAutoRedirect, cultureName);
        client.DefaultRequestHeaders.Add(TestAuthenticationHandler.UserIdHeaderName, user.UserId);

        if (user.Roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthenticationHandler.RolesHeaderName, string.Join(',', user.Roles));
        }

        return client;
    }

    private static void ApplyCulture(HttpClient client, string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return;
        }

        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(cultureName);
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{CookieRequestCultureProvider.DefaultCookieName}={CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(cultureName))}");
    }

    public async Task ExecuteDbContextAsync(Func<ApplicationDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await action(dbContext);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            _connection?.Dispose();
            _connection = null;

            if (!_useSqlite && !string.IsNullOrWhiteSpace(_sqlServerDatabaseName))
            {
                DropSqlServerDatabase(_sqlServerDatabaseName);
            }
        }
    }

    private static void DropSqlServerDatabase(string databaseName)
    {
        SqlConnection.ClearAllPools();

        using var connection = new SqlConnection("Server=(localdb)\\mssqllocaldb;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(@databaseName) IS NOT NULL
            BEGIN
                ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{databaseName}];
            END
            """;
        command.Parameters.AddWithValue("@databaseName", databaseName);
        command.ExecuteNonQuery();
    }

    private static string ResolveRepositoryRootPath()
    {
        var directory = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));

        while (directory is not null)
        {
            var webProjectPath = Path.Combine(directory.FullName, "src", "Neftyanik.Portal.Web");
            if (Directory.Exists(webProjectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root containing 'src/Neftyanik.Portal.Web'.");
    }
}
