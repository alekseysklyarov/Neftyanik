using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Commands;

public static class PlatformPasswordRecoveryCommand
{
    public const string Name = "reset-platform-admin-password";

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
            Console.Write("Exact existing PlatformAdministrator login (case-sensitive; not an email alias): ");
            var login = Console.ReadLine() ?? string.Empty;
            var recovery = scope.ServiceProvider.GetRequiredService<IPlatformAdministratorPasswordRecovery>();
            var stamp = await recovery.GetSecurityStampAsync(login);
            if (string.IsNullOrEmpty(stamp))
            {
                Console.Error.WriteLine("Recovery refused: no unambiguous, active, unlocked platform account matches this exact login.");
                return 1;
            }
            Console.Write("Approval/change reference (max 100 characters; no secrets): ");
            var approval = Console.ReadLine() ?? string.Empty;
            Console.Write("New temporary password (input hidden): ");
            var password = PlatformBootstrapCommand.ReadHidden();
            Console.Write("Confirm temporary password (input hidden): ");
            if (password != PlatformBootstrapCommand.ReadHidden())
            {
                Console.Error.WriteLine("Password confirmation did not match.");
                return 1;
            }
            var result = await recovery.ResetAsync(login, stamp, password,
                Environment.UserDomainName + "\\" + Environment.UserName, approval);
            Console.WriteLine(result switch
            {
                PlatformOperatorPasswordResetResult.Succeeded => "Password reset committed and prior sessions invalidated. Sign in at /Platform/Account/Login within 24 hours and change the temporary password. SMTP and email confirmation are not required. No account was created; roles, active state, email confirmation and the bootstrap marker were preserved.",
                PlatformOperatorPasswordResetResult.Conflict => "Recovery refused: account state changed while this reset was being prepared. Review the current state before starting another reset.",
                PlatformOperatorPasswordResetResult.Denied => "Recovery refused: account is no longer eligible. No account was created, enabled or promoted.",
                PlatformOperatorPasswordResetResult.InvalidInput => "Recovery refused: invalid login, password input or audit metadata.",
                PlatformOperatorPasswordResetResult.InvalidPassword => "Recovery refused: the password does not meet Identity policy or the account changed concurrently.",
                _ => "Recovery did not report success. Ask an authorized operator to inspect state before retrying."
            });
            return result == PlatformOperatorPasswordResetResult.Succeeded ? 0 : 1;
        }
        catch
        {
            Console.Error.WriteLine("Recovery could not be completed or confirmed. Inspect audit/account state before retrying. No credentials or exception details will be printed.");
            return 1;
        }
    }
}
