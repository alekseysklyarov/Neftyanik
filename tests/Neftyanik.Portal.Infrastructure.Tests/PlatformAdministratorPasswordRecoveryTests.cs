using System.Data.Common;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;
using Fixture = Neftyanik.Portal.Infrastructure.Tests.PlatformLegacyInitializationTests.Fixture;

namespace Neftyanik.Portal.Infrastructure.Tests;

public sealed class PlatformAdministratorPasswordRecoveryTests
{
    private static string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_ExpiredOrForgottenPassword_UsesIdentityAndPreservesTenantDataAndMarker(bool expired)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("operator"))!;
            user.MustChangePassword = expired;
            user.AccessFailedCount = 2;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))).Succeeded);
        });
        var before = await fixture.SnapshotAsync(excludeNewIdentity: true, includeMarker: true);
        var stamp = await PrepareAsync(fixture);
        var password = Secret();
        Assert.Equal(PlatformOperatorPasswordResetResult.Succeeded, await ResetAsync(fixture, stamp!, password));
        var after = await fixture.SnapshotAsync(excludeNewIdentity: true, includeMarker: true);
        foreach (var table in before.Keys.Where(x => x != "PlatformPasswordRecoveryAudits")) Assert.Equal(before[table], after[table]);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("operator"))!;
            Assert.True(user.IsActive);
            Assert.True(user.MustChangePassword);
            Assert.False(user.EmailConfirmed);
            Assert.Equal(2, user.AccessFailedCount);
            Assert.NotEqual(stamp, user.SecurityStamp);
            Assert.True(await users.CheckPasswordAsync(user, password));
            Assert.False(await users.CheckPasswordAsync(user, fixture.Password));
            Assert.Equal(new[] { RoleNames.PlatformAdministrator }, await users.GetRolesAsync(user));
            Assert.Equal(user.SecurityStamp, await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.CliAuthorizationStamp));
            Assert.True(DateTimeOffset.Parse((await users.GetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.ExpiryToken))!) > DateTimeOffset.UtcNow.AddHours(23));
            var audit = await services.GetRequiredService<ApplicationDbContext>().PlatformPasswordRecoveryAudits.SingleAsync();
            Assert.Equal(user.Id, audit.UserId);
            Assert.Equal("os-operator", audit.OperatorIdentity);
            Assert.Equal("RESET-123", audit.ApprovalReference);
            Assert.True(await services.GetRequiredService<IPlatformAdministratorOnboarding>().CanChangePasswordAsync(user.Id, user.SecurityStamp!));
            Assert.Equal(PlatformPasswordChangeResult.Succeeded,
                await services.GetRequiredService<IPlatformAdministratorOnboarding>().ChangePasswordAsync(user.Id, user.SecurityStamp!, password, Secret()));
        });
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("locked")]
    [InlineData("two-factor")]
    [InlineData("ambiguous")]
    [InlineData("wrong-case")]
    [InlineData("email-alias")]
    public async Task Reset_IneligibleOrAmbiguousLogin_IsRefusedWithoutWrites(string state)
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        var stamp = await PrepareAsync(fixture);
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("operator"))!;
            if (state == "disabled") user.IsActive = false;
            if (state == "locked") user.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            if (state == "two-factor") user.TwoFactorEnabled = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded);
            if (state == "revoked") Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            if (state == "ambiguous")
            {
                var database = services.GetRequiredService<ApplicationDbContext>();
                database.Users.Add(new ApplicationUser { UserName = "other", NormalizedUserName = "OTHER", Email = "operator", NormalizedEmail = "OPERATOR", FirstName = "Legacy", LastName = "Ambiguous" });
                await database.SaveChangesAsync();
            }
        });
        var login = state switch { "tenant" => "tenant", "missing" => "missing", "wrong-case" => "OPERATOR", "email-alias" => "operator@example.test", _ => "operator" };
        var before = await fixture.SnapshotAsync(includeMarker: true);
        Assert.Null(await PrepareAsync(fixture, login));
        Assert.Equal(PlatformOperatorPasswordResetResult.Denied, await ResetAsync(fixture, stamp!, Secret(), login));
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Fact]
    public async Task Reset_ConcurrentPreparedAttempts_OnlyOneCommits()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        var stamp = await PrepareAsync(fixture);
        var results = await Task.WhenAll(ResetAsync(fixture, stamp!, Secret()), ResetAsync(fixture, stamp!, Secret()));
        Assert.Single(results.Where(x => x == PlatformOperatorPasswordResetResult.Succeeded));
        Assert.Single(results.Where(x => x == PlatformOperatorPasswordResetResult.Conflict));
        await fixture.ScopeAsync(async services => Assert.Single(await services.GetRequiredService<ApplicationDbContext>().PlatformPasswordRecoveryAudits.ToListAsync()));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("expiry")]
    public async Task Reset_PersistenceFailure_RollsBackPasswordStampOnboardingAndAudit(string failure)
    {
        var interceptor = new FailResetWrite();
        await using var fixture = await Fixture.CreateAsync(interceptor: interceptor);
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        var stamp = await PrepareAsync(fixture);
        var before = await fixture.SnapshotAsync(includeMarker: true);
        interceptor.Fragment = failure == "audit" ? "INSERT INTO [PlatformPasswordRecoveryAudits]" : "UPDATE [AspNetUserTokens]";
        Assert.Equal(PlatformOperatorPasswordResetResult.Failed, await ResetAsync(fixture, stamp!, Secret()));
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Fact]
    public async Task Reset_InvalidPasswordOrAuditMetadata_ProducesNoChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        var stamp = await PrepareAsync(fixture);
        var before = await fixture.SnapshotAsync(includeMarker: true);
        Assert.Equal(PlatformOperatorPasswordResetResult.InvalidPassword, await ResetAsync(fixture, stamp!, "x"));
        await fixture.ScopeAsync(async services => Assert.Equal(PlatformOperatorPasswordResetResult.InvalidInput,
            await services.GetRequiredService<IPlatformAdministratorPasswordRecovery>().ResetAsync("operator", stamp!, Secret(), "operator", new string('x', 101))));
        var after = await fixture.SnapshotAsync(includeMarker: true);
        foreach (var table in before.Keys) Assert.Equal(before[table], after[table]);
    }

    [Fact]
    public async Task Reset_AuditAndBootstrapStateSurviveUserDeletion()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        Assert.Equal(PlatformOperatorPasswordResetResult.Succeeded, await ResetAsync(fixture, (await PrepareAsync(fixture))!, Secret()));
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.DeleteAsync((await users.FindByNameAsync("operator"))!)).Succeeded);
            var database = services.GetRequiredService<ApplicationDbContext>();
            Assert.Single(await database.PlatformPasswordRecoveryAudits.ToListAsync());
            Assert.Single(await database.PlatformBootstrapStates.ToListAsync());
        });
    }

    [Fact]
    public async Task PendingAccountWithoutCliProofOrProvisioningRecord_CannotUseOnboarding()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = "not-cli-created", FirstName = "Not", LastName = "Cli", IsActive = true, MustChangePassword = true };
            Assert.True((await users.CreateAsync(user, Secret())).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformAdministrator)).Succeeded);
            Assert.True((await users.SetAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider,
                PlatformAdministratorOnboarding.ExpiryToken, DateTimeOffset.UtcNow.AddHours(24).ToString("O"))).Succeeded);
            Assert.False(await services.GetRequiredService<IPlatformAdministratorOnboarding>().CanChangePasswordAsync(user.Id, user.SecurityStamp!));
        });
    }

    [Fact]
    public async Task PreviouslyCliProvisionedPendingAccount_CanUseItsPermanentProvenanceRecord()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(PlatformBootstrapResult.Created, await fixture.InitializeAsync());
        await fixture.ScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync("operator"))!;
            Assert.True((await users.RemoveAuthenticationTokenAsync(user, PlatformAdministratorOnboarding.TokenProvider, PlatformAdministratorOnboarding.CliAuthorizationStamp)).Succeeded);
            Assert.True(await services.GetRequiredService<IPlatformAdministratorOnboarding>().CanChangePasswordAsync(user.Id, user.SecurityStamp!));
        });
    }

    private static async Task<string?> PrepareAsync(Fixture fixture, string login = "operator")
    {
        string? stamp = null;
        await fixture.ScopeAsync(async services => stamp = await services.GetRequiredService<IPlatformAdministratorPasswordRecovery>().GetSecurityStampAsync(login));
        return stamp;
    }

    private static async Task<PlatformOperatorPasswordResetResult> ResetAsync(Fixture fixture, string stamp, string password, string login = "operator")
    {
        var result = PlatformOperatorPasswordResetResult.Failed;
        await fixture.ScopeAsync(async services => result = await services.GetRequiredService<IPlatformAdministratorPasswordRecovery>()
            .ResetAsync(login, stamp, password, "os-operator", "RESET-123"));
        return result;
    }

    private sealed class FailResetWrite : DbCommandInterceptor
    {
        public string? Fragment { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Fragment is not null && command.CommandText.Contains(Fragment, StringComparison.Ordinal)) throw new InvalidOperationException("Injected reset write failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
