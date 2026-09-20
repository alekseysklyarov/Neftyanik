using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Commands;

public static class PlatformLegacyInitializationCommand
{
    public const string Name = "initialize-first-platform-admin-for-existing-installation";

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("An interactive operator terminal is required.");
            return 1;
        }
        try
        {
            await using var scope = services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (!database.Database.IsSqlServer() || database.IsAssociationResolved) return 1;
            await database.Database.OpenConnectionAsync();
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME()";
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return 1;
            var server = reader.GetString(0);
            var databaseName = reader.GetString(1);
            await reader.DisposeAsync();
            Console.WriteLine($"Target SQL Server: {server}; database: {databaseName}");
            Console.Write("Type the exact SQL Server name: ");
            if (!string.Equals(Console.ReadLine(), server, StringComparison.Ordinal)) return 1;
            Console.Write("Type the exact database name: ");
            if (!string.Equals(Console.ReadLine(), databaseName, StringComparison.Ordinal)) return 1;
            var ownerConfirmed = ConfirmOwnerDecision(Console.In, Console.Out);
            if (!ownerConfirmed) return 1;
            Console.Write("New dedicated login (not an existing tenant account): ");
            var login = Console.ReadLine() ?? string.Empty;
            Console.Write("Administrator email (mailbox ownership must be confirmed): ");
            var email = Console.ReadLine() ?? string.Empty;
            Console.Write("Temporary password (input hidden): ");
            var password = PlatformBootstrapCommand.ReadHidden();
            Console.Write("Confirm temporary password (input hidden): ");
            if (password != PlatformBootstrapCommand.ReadHidden())
            {
                Console.Error.WriteLine("Password confirmation did not match.");
                return 1;
            }
            var operatorIdentity = Environment.UserDomainName + "\\" + Environment.UserName;
            var result = await scope.ServiceProvider.GetRequiredService<IPlatformLegacyInitialization>()
                .InitializeAsync(login, email, password, operatorIdentity, ownerConfirmed);
            if (result != PlatformBootstrapResult.Created)
            {
                Console.Error.WriteLine(result switch
                {
                    PlatformBootstrapResult.AlreadyProvisioned => "Initialization refused: state is not eligible, already consumed, or provisioning evidence exists. Do not clear the marker.",
                    PlatformBootstrapResult.AccountExists => "Initialization refused: login/email conflicts with an existing account. No account was modified.",
                    PlatformBootstrapResult.InvalidInput => "Initialization refused: invalid input, audit metadata or password policy failure.",
                    _ => "Initialization did not report success. Have an authorized operator inspect state before retrying."
                });
                return 1;
            }
            await CompleteAsync(Console.Out);
            return 0;
        }
        catch
        {
            Console.Error.WriteLine("Initialization could not be completed or confirmed. Inspect provisioning state before retrying; do not remove the marker.");
            return 1;
        }
    }

    public static bool ConfirmOwnerDecision(TextReader input, TextWriter output)
    {
        output.WriteLine("Absence of current platform accounts does not prove historical eligibility. Verify the installation history before confirming.");
        output.WriteLine("Owner declaration: I am the project owner. This is the first PlatformAdministrator account, and I authorize its creation.");
        output.Write("Type CREATE FIRST PLATFORMADMINISTRATOR to confirm this declaration: ");
        return string.Equals(input.ReadLine(), "CREATE FIRST PLATFORMADMINISTRATOR", StringComparison.Ordinal);
    }

    public static async Task CompleteAsync(TextWriter output)
    {
        await output.WriteLineAsync("Administrator creation committed; bootstrap is permanently consumed. SMTP is not required and no email was sent.");
        await output.WriteLineAsync("Sign in at /Platform/Account/Login within 24 hours and change the temporary password. Email remains unconfirmed; optional confirmation can be requested later from /Platform/Account/ForgotPassword. For forgotten or expired passwords use reset-platform-admin-password; never repeat initialization. No tenant membership was assigned.");
    }
}
