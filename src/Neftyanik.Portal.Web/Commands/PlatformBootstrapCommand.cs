using System.Text;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Commands;

public static class PlatformBootstrapCommand
{
    public static async Task<int> RunAsync(IServiceProvider services)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("An interactive terminal is required.");
            return 1;
        }
        try
        {
            await using var scope = services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await database.Database.OpenConnectionAsync();
            await using var command = database.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')), DB_NAME()";
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return 1;
            var server = reader.GetString(0);
            var databaseName = reader.GetString(1);
            await reader.DisposeAsync();
            Console.WriteLine($"Target server: {server}; database: {databaseName}");
            Console.Write("To confirm this target, type its database name: ");
            if (!string.Equals(Console.ReadLine(), databaseName, StringComparison.Ordinal)) return 1;
            Console.Write("New dedicated login (not an existing account): ");
            var login = Console.ReadLine() ?? string.Empty;
            Console.Write("Administrator email (mailbox ownership must be confirmed): ");
            var email = Console.ReadLine() ?? string.Empty;
            Console.Write("Temporary password (input hidden): ");
            var password = ReadHidden();
            Console.Write("Confirm temporary password (input hidden): ");
            var confirmation = ReadHidden();
            if (password != confirmation)
            {
                Console.Error.WriteLine("Password confirmation did not match.");
                return 1;
            }
            var result = await scope.ServiceProvider.GetRequiredService<IPlatformAdministratorOnboarding>()
                .BootstrapAsync(login, email, password);
            if (result == PlatformBootstrapResult.Created)
            {
                Console.WriteLine("CLI onboarding is ready without SMTP. Email remains unconfirmed; optional confirmation can be requested later from /Platform/Account/ForgotPassword. Do not repeat bootstrap.");
            }
            Console.WriteLine(result switch
            {
                PlatformBootstrapResult.Created => "First platform administrator created. Sign in at /Platform/Account/Login within 24 hours and change the temporary password. No tenant membership was assigned.",
                PlatformBootstrapResult.AlreadyProvisioned => "Bootstrap refused: provisioning is consumed or legacy state requires operator review. Do not remove the marker.",
                PlatformBootstrapResult.AccountExists => "Bootstrap refused: the login conflicts with an existing account. No account was changed.",
                PlatformBootstrapResult.InvalidInput => "Bootstrap refused: invalid login or password does not meet the configured Identity policies.",
                _ => "Bootstrap did not report success. Ask an authorized operator to verify provisioning state before retrying."
            });
            return result == PlatformBootstrapResult.Created ? 0 : 1;
        }
        catch
        {
            Console.Error.WriteLine("Bootstrap failed. Check target availability and schema with an authorized operator. No credentials or exception details will be printed.");
            return 1;
        }
    }

    internal static string ReadHidden()
    {
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                if (buffer.Length >= 1024) throw new InvalidOperationException("Input too long.");
                buffer.Append(key.KeyChar);
            }
        }
    }
}
