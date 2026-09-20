using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Neftyanik.Portal.Web.Commands;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PlatformLegacyInitializationCommandTests
{
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Theory]
    [InlineData("CREATE FIRST PLATFORMADMINISTRATOR", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("REVIEWED", false)]
    [InlineData("YES", false)]
    [InlineData("create first platformadministrator", false)]
    [InlineData(" CREATE FIRST PLATFORMADMINISTRATOR", false)]
    [InlineData("CREATE FIRST PLATFORMADMINISTRATOR ", false)]
    public void OwnerConfirmation_RequiresExactExplicitDeclaration(string? answer, bool expected)
    {
        using var input = new StringReader(answer is null ? string.Empty : answer + Environment.NewLine);
        using var output = new StringWriter();
        Assert.Equal(expected, PlatformLegacyInitializationCommand.ConfirmOwnerDecision(input, output));
        Assert.Contains("I am the project owner", output.ToString());
        Assert.Contains("first PlatformAdministrator account", output.ToString());
        Assert.Contains("I authorize its creation", output.ToString());
        Assert.DoesNotContain("REVIEWED", output.ToString());
        Assert.DoesNotContain("Approval/change reference", output.ToString());
        Assert.DoesNotContain("Independent", output.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyCli_RedirectedInputAndExtraArguments_StopBeforeHostStartup(bool extraArgument)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(PlatformLegacyInitializationCommand.Name);
        if (extraArgument) start.ArgumentList.Add("--force");
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(1, process.ExitCode);
        Assert.Empty(await output);
        Assert.Contains("requires an interactive terminal", await error);
        Assert.DoesNotContain("--force", await error);
    }

    [Fact]
    public async Task WebHost_DoesNotRegisterLegacyInitializer_OrExposeItToTenantAdministrators()
    {
        await using var factory = new PortalWebApplicationFactory();
        using var anonymous = factory.CreateAnonymousClient();
        using var tenant = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("tenant-admin", RoleNames.Administrator));
        using var scope = factory.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IPlatformLegacyInitialization>());
        foreach (var path in new[] { "/" + PlatformLegacyInitializationCommand.Name, "/Platform/Account/" + PlatformLegacyInitializationCommand.Name, "/neftyanik/Administration/" + PlatformLegacyInitializationCommand.Name })
        {
            using var anonymousResponse = await anonymous.GetAsync(path);
            using var tenantResponse = await tenant.PostAsync(path, new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.NotFound, anonymousResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, tenantResponse.StatusCode);
        }
    }

    [Fact]
    public async Task LegacyInitialization_OnNonSqlServer_IsRefused()
    {
        await using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var initializer = new PlatformLegacyInitialization(services.GetRequiredService<ApplicationDbContext>(),
            services.GetRequiredService<UserManager<ApplicationUser>>(), services.GetRequiredService<RoleManager<IdentityRole>>(), TimeProvider.System);
        Assert.Equal(PlatformBootstrapResult.Failed, await initializer.InitializeAsync("operator", "operator@example.test", Secret(), "operator", ownerConfirmed: true));
    }

    [Fact]
    public async Task LegacyInitialization_SmtpFailureAfterCommit_PreservesConsumedMarkerAndAllowsOnlyEmailRetry()
    {
        await using var factory = new PortalWebApplicationFactory(useSqlite: false, additionalConfiguration: new Dictionary<string, string?>
        {
            ["PlatformRecovery:BaseUrl"] = "https://trusted.example"
        });
        var sender = new FailingEmailSender();
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPlatformEmailSender>();
            services.AddSingleton<IPlatformEmailSender>(sender);
        }));
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var database = services.GetRequiredService<ApplicationDbContext>();
        await database.Database.MigrateAsync();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(new ApplicationUser { UserName = "tenant", FirstName = "Tenant", LastName = "User" }, Secret())).Succeeded);
        database.PlatformBootstrapStates.Add(new PlatformBootstrapState { Id = 1, Disposition = PlatformBootstrapDisposition.LegacyReviewRequired, ConsumedAtUtc = DateTimeOffset.UtcNow, Reason = "Reviewed fixture" });
        await database.SaveChangesAsync();
        var initializer = new PlatformLegacyInitialization(database, users, services.GetRequiredService<RoleManager<IdentityRole>>(), TimeProvider.System);
        var password = Secret();
        Assert.Equal(PlatformBootstrapResult.Created, await initializer.InitializeAsync("operator", "operator@example.test", password, "operator", ownerConfirmed: true));
        var marker = await database.PlatformBootstrapStates.SingleAsync();
        Assert.Equal("Owner attestation: first PlatformAdministrator; creation authorized", marker.Reason);
        Assert.Equal("operator", marker.OperatorIdentity);
        Assert.Null(marker.ApprovalReference);
        using var output = new StringWriter();
        var recovery = services.GetRequiredService<IPlatformAccountRecovery>();
        await PlatformLegacyInitializationCommand.CompleteAsync(output);
        Assert.Equal(0, sender.Attempts);
        await recovery.RequestAsync("operator@example.test", confirmEmail: true);
        Assert.Equal(1, sender.Attempts);
        Assert.Contains("creation committed", output.ToString());
        Assert.DoesNotContain(password, output.ToString());
        Assert.Equal(PlatformBootstrapResult.AlreadyProvisioned, await initializer.InitializeAsync("replacement", "replacement@example.test", Secret(), "operator", ownerConfirmed: true));
        await recovery.RequestAsync("operator@example.test", confirmEmail: true);
        Assert.Equal(2, sender.Attempts);
        var user = (await users.FindByNameAsync("operator"))!;
        Assert.False(user.EmailConfirmed);
        Assert.True(user.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(user, password));
        Assert.Equal(2, await database.Users.CountAsync());
        Assert.Equal(PlatformBootstrapDisposition.Consumed, (await database.PlatformBootstrapStates.SingleAsync()).Disposition);
    }

    private sealed class FailingEmailSender : IPlatformEmailSender
    {
        public int Attempts { get; private set; }
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Simulated transport failure; no real email sent.");
        }
    }
}
