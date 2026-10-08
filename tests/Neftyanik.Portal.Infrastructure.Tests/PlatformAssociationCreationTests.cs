using Neftyanik.Portal.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public class PlatformAssociationCreationTests
{
    [Fact]
    public async Task AddAdministrator_CreatesIsolatedAccountAndAuditWithoutChangingOtherAssociation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.ExistingDataAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = new Association { Name = "Additional garden", Slug = "additional" };
        db.Associations.Add(association);
        await db.SaveChangesAsync();
        var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationAdministratorCreator>();
        var request = new AssociationAdministratorRequest("additional-admin", "shared-email@example.invalid", "New administrator", Request("unused").TemporaryPassword, true);
        Assert.Equal(AdministratorCreationOutcome.Created, await creator.CreateAsync(association.Id, request));
        Assert.False(db.IsAssociationResolved);
        var user = await db.Users.SingleAsync(x => x.UserName == request.UserName);
        Assert.True(user.MustChangePassword);
        Assert.True(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CheckPasswordAsync(user, request.TemporaryPassword));
        Assert.Equal(association.Id, (await db.AssociationAccountBindings.SingleAsync(x => x.ApplicationUserId == user.Id)).AssociationId);
        var assignment = Assert.Single(await db.AssociationUserMemberships.IgnoreQueryFilters().Where(x => x.ApplicationUserId == user.Id).ToListAsync());
        Assert.Equal(RoleNames.Administrator, assignment.Role);
        Assert.Empty(await db.UserRoles.Where(x => x.UserId == user.Id).ToListAsync());
        var audit = Assert.Single(await db.PlatformAuditLogs.ToListAsync());
        Assert.Equal(association.Id, audit.AssociationId);
        Assert.Equal("operator", audit.OperatorUserId);
        Assert.Equal(PlatformAuditActions.AdministratorAssigned, audit.Action);
        Assert.DoesNotContain(request.TemporaryPassword, audit.NewValuesJson);
        Assert.Equal(before, await fixture.ExistingDataAsync(request.UserName));
        Assert.Equal(AdministratorCreationOutcome.UsernameExists, await creator.CreateAsync(association.Id, request));
        Assert.Equal(1, await db.PlatformAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("AspNetUsers")]
    [InlineData("AssociationAccountBindings")]
    [InlineData("AssociationUserMemberships")]
    [InlineData("PlatformAuditLogs")]
    public async Task AddAdministrator_FailedWriteRollsBackAccountBindingAssignmentAndAudit(string table)
    {
        var failure = new CreationInterceptor { FailTable = table };
        await using var fixture = await Fixture.CreateAsync(failure);
        var before = await fixture.ExistingDataAsync();
        failure.Enabled = true;
        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPlatformAssociationAdministratorCreator>()
            .CreateAsync(fixture.InitialId, new("rollback-admin", "test@example.invalid", null, Request("unused").TemporaryPassword, true));
        Assert.Equal(AdministratorCreationOutcome.Failed, result);
        Assert.True(failure.WasTriggered);
        Assert.Equal(before, await fixture.ExistingDataAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task AddAdministrator_RejectsUnauthorizedInvalidAndInactiveRequestsWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var inactive = new Association { Name = "Inactive", Slug = "inactive-admin", IsActive = false };
        db.Associations.Add(inactive);
        await db.SaveChangesAsync();
        var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationAdministratorCreator>();
        var request = new AssociationAdministratorRequest("new-admin", "test@example.invalid", null, Request("unused").TemporaryPassword, true);
        var before = await fixture.ExistingDataAsync();
        fixture.Access.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.CreateAsync(int.MaxValue, request));
        fixture.Access.Denied = false;
        Assert.Equal(AdministratorCreationOutcome.NotFound, await creator.CreateAsync(int.MaxValue, request));
        Assert.Equal(AdministratorCreationOutcome.InactiveAssociation, await creator.CreateAsync(inactive.Id, request));
        Assert.Equal(AdministratorCreationOutcome.ConfirmationRequired, await creator.CreateAsync(fixture.InitialId, request with { ConfirmAssignment = false }));
        Assert.Equal(AdministratorCreationOutcome.InvalidInput, await creator.CreateAsync(fixture.InitialId, request with { Email = "invalid" }));
        Assert.Equal(AdministratorCreationOutcome.IdentityRejected, await creator.CreateAsync(fixture.InitialId, request with { TemporaryPassword = "weak" }));
        Assert.Equal(AdministratorCreationOutcome.UsernameExists, await creator.CreateAsync(fixture.InitialId, request with { UserName = "EXISTING-ADMIN" }));
        Assert.Equal(before, await fixture.ExistingDataAsync());
        Assert.Empty(await db.PlatformAuditLogs.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_CommitsLocalAdministratorInitializationAndAuditWithoutChangingExistingData()
    {
        var writes = new CreationInterceptor();
        await using var fixture = await Fixture.CreateAsync(writes);
        var before = await fixture.ExistingDataAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>();
        writes.Enabled = true;
        var result = await creator.CreateAsync(Request("new-association"));
        Assert.Equal(AssociationCreationOutcome.Created, result.Outcome);
        Assert.Equal(7, writes.Writes.Count);
        Assert.Single(writes.Writes.Select(x => x.ContextId).Distinct());
        var transaction = Assert.Single(writes.Writes.Select(x => x.Transaction).Distinct());
        Assert.NotNull(transaction);
        Assert.Equal(7, writes.Writes.Select(x => x.Table).Distinct().Count());
        Assert.False(scope.ServiceProvider.GetRequiredService<IAssociationContext>().IsResolved);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Members.ToListAsync());
        Assert.Equal(before, await fixture.ExistingDataAsync("admin-new-association"));
        await using var tenantScope = fixture.Services.CreateAsyncScope();
        var database = tenantScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = await database.Associations.SingleAsync(x => x.Id == result.AssociationId);
        tenantScope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
        var membership = Assert.Single(await database.AssociationUserMemberships.AsNoTracking().ToListAsync());
        var administrator = await database.Users.SingleAsync(x => x.UserName == "admin-new-association");
        Assert.NotEqual("existing", administrator.Id);
        Assert.Equal(administrator.Id, membership.ApplicationUserId);
        Assert.True(administrator.MustChangePassword);
        Assert.Equal("shared-email@example.invalid", administrator.Email);
        Assert.Equal(association.Id, (await database.AssociationAccountBindings.SingleAsync(x => x.ApplicationUserId == administrator.Id)).AssociationId);
        Assert.Equal(1, await database.AssociationUserMemberships.IgnoreQueryFilters().CountAsync(x => x.ApplicationUserId == administrator.Id));
        Assert.False(await database.AssociationUserMemberships.IgnoreQueryFilters().AnyAsync(x => x.ApplicationUserId == administrator.Id && x.AssociationId == fixture.InitialId));
        Assert.Equal(RoleNames.Administrator, membership.Role);
        Assert.True(membership.IsActive);
        Assert.Empty(await database.UserRoles.Where(x => x.UserId == "existing").ToListAsync());
        Assert.Empty(await database.Members.ToListAsync());
        Assert.Empty(await database.Charges.ToListAsync());
        Assert.Empty(await database.Payments.ToListAsync());
        Assert.Empty(await database.MemberElectricityReadings.ToListAsync());
        Assert.Empty(await database.MembershipFeeRates.ToListAsync());
        Assert.Empty(await database.MemberElectricityTariffs.ToListAsync());
        Assert.Empty(await database.AssociationElectricityTariffs.ToListAsync());
        var category = Assert.Single(await database.ExpenseCategories.ToListAsync());
        Assert.NotEqual(ExpenseCategoryIds.ElectricityPayment, category.Id);
        Assert.Equal(category.Id, await database.GetElectricityExpenseCategoryIdAsync());
        var audit = await database.PlatformAuditLogs.SingleAsync(x => x.AssociationId == association.Id);
        Assert.Equal("operator", audit.OperatorUserId);
        Assert.Equal(PlatformAuditActions.AssociationCreated, audit.Action);
        using var payload = JsonDocument.Parse(audit.NewValuesJson);
        Assert.Equal(administrator.Id, payload.RootElement.GetProperty("AdministratorUserId").GetString());
        Assert.DoesNotContain("TemporaryPassword", audit.NewValuesJson);
        Assert.Equal(AssociationCreationOutcome.SlugExists, (await creator.CreateAsync(Request("new-association"))).Outcome);
        Assert.Equal(1, await database.PlatformAuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("AspNetUsers")]
    [InlineData("AssociationAccountBindings")]
    [InlineData("AssociationUserMemberships")]
    [InlineData("ExpenseCategories")]
    [InlineData("SystemSettings")]
    [InlineData("PlatformAuditLogs")]
    public async Task CreateAsync_FailureAtAnyProvisioningStageRollsBackEverything(string table)
    {
        var failure = new CreationInterceptor { FailTable = table };
        await using var fixture = await Fixture.CreateAsync(failure);
        var before = await fixture.ExistingDataAsync();
        failure.Enabled = true;
        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>().CreateAsync(Request("rollback"));
        Assert.Equal(AssociationCreationOutcome.Failed, result.Outcome);
        Assert.True(failure.WasTriggered);
        Assert.Equal(before, await fixture.ExistingDataAsync());
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await database.Associations.AnyAsync(x => x.Slug == "rollback"));
        Assert.Empty(await database.PlatformAuditLogs.ToListAsync());
        Assert.False(await database.AssociationUserMemberships.IgnoreQueryFilters().AnyAsync(x => x.AssociationId != fixture.InitialId));
        Assert.False(await database.ExpenseCategories.IgnoreQueryFilters().AnyAsync(x => x.AssociationId != fixture.InitialId));
        Assert.False(await database.SystemSettings.IgnoreQueryFilters().AnyAsync(x => x.AssociationId != fixture.InitialId));
    }

    [Fact]
    public async Task CreateAsync_ConcurrentSameSlugReturnsOneCreatedAndOneFriendlyConflict()
    {
        var interceptor = new CreationInterceptor { SynchronizeAssociationInsert = true };
        await using var fixture = await Fixture.CreateAsync(interceptor);
        interceptor.Enabled = true;
        await using var first = fixture.Services.CreateAsyncScope();
        await using var second = fixture.Services.CreateAsyncScope();
        var results = await Task.WhenAll(
            first.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>().CreateAsync(Request("concurrent")),
            second.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>().CreateAsync(Request("concurrent")));
        Assert.Single(results.Where(x => x.Outcome == AssociationCreationOutcome.Created));
        Assert.Single(results.Where(x => x.Outcome == AssociationCreationOutcome.SlugExists));
        Assert.Equal(2, interceptor.Arrivals);
        var database = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = await database.Associations.SingleAsync(x => x.Slug == "concurrent");
        Assert.Equal(1, await database.AssociationUserMemberships.IgnoreQueryFilters().CountAsync(x => x.AssociationId == association.Id));
        Assert.Equal(1, await database.PlatformAuditLogs.CountAsync(x => x.AssociationId == association.Id));
    }

    [Theory]
    [InlineData("existing-admin")]
    [InlineData("EXISTING-ADMIN")]
    public async Task CreateAsync_RejectsExistingUsernameWithoutChangingAccount(string login)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>();
        var request = Request("invalid-account") with { AdministratorUserName = login };
        var before = await fixture.ExistingDataAsync();
        Assert.Equal(AssociationCreationOutcome.UsernameExists, (await creator.CreateAsync(request)).Outcome);
        Assert.Equal(before, await fixture.ExistingDataAsync());
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Associations.AnyAsync(x => x.Slug == "invalid-account"));
    }

    [Fact]
    public async Task CreateAsync_IdentityRejectionRollsBackAssociation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var before = await fixture.ExistingDataAsync();
        var result = await scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>()
            .CreateAsync(Request("bad-password") with { TemporaryPassword = "short" });
        Assert.Equal(AssociationCreationOutcome.IdentityRejected, result.Outcome);
        Assert.Equal(before, await fixture.ExistingDataAsync());
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Associations.AnyAsync(x => x.Slug == "bad-password"));
    }

    [Fact]
    public async Task Service_RequiresAuthorizationAndExplicitConfirmation()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationCreator>();
        Assert.Equal(AssociationCreationOutcome.ConfirmationRequired, (await creator.CreateAsync(Request("unconfirmed") with { ConfirmAdministrator = false })).Outcome);
        fixture.Access.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.CreateAsync(Request("unauthorized")));
    }

    [Theory]
    [InlineData("platform")]
    [InlineData("health")]
    [InlineData("error")]
    [InlineData("account")]
    [InlineData("administration")]
    [InlineData("member")]
    [InlineData("payments")]
    [InlineData("localization")]
    [InlineData("privacy")]
    [InlineData("index")]
    [InlineData("home")]
    [InlineData("finance")]
    [InlineData("shared")]
    [InlineData("css")]
    [InlineData("js")]
    [InlineData("lib")]
    public void ValidateSlug_RejectsReservedRoots(string slug) =>
        Assert.Throws<InvalidOperationException>(() => new Association { Slug = slug }.ValidateSlug());

    [Fact]
    public async Task AdministratorManagement_ProtectsLastAdminScopesAccessAndResetsPassword()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var association = await db.Associations.SingleAsync(a => a.Id == fixture.InitialId);
        scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
        db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = "existing", Role = RoleNames.Administrator });
        await db.SaveChangesAsync();
        var manager = scope.ServiceProvider.GetRequiredService<PlatformAssociationAdministratorManager>();
        Assert.NotNull(await manager.ChangeAsync(association.Id, "existing-admin", false, null, default));
        db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = "other", Role = RoleNames.Administrator });
        await db.SaveChangesAsync();
        Assert.Null(await manager.ChangeAsync(association.Id, "existing-admin", false, null, default));
        db.ChangeTracker.Clear();
        Assert.False((await db.AssociationUserMemberships.SingleAsync(m => m.ApplicationUserId == "existing" && m.Role == RoleNames.Administrator)).IsActive);
        Assert.NotNull(await manager.ChangeAsync(association.Id, "other-admin", false, null, default));
        Assert.NotNull(await manager.ChangeAsync(association.Id, "operator", false, null, default));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        Assert.Null(await manager.ChangeAsync(association.Id, "other-admin", null, password, default));
        db.ChangeTracker.Clear();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByNameAsync("other-admin"))!;
        Assert.True(user.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(user, password));
        var audit = await db.PlatformAuditLogs.SingleAsync(a => a.Action == "AdministratorPasswordReset");
        Assert.DoesNotContain(password, audit.NewValuesJson);
        fixture.Access.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => manager.ChangeAsync(association.Id, "other-admin", true, null, default));
    }

    private static AssociationCreationRequest Request(string slug) =>
        new("New association", slug, null, null, null, "shared-email@example.invalid", "admin-" + slug, true,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!");

    private sealed class Access : IPlatformAssociationWriteAccess
    {
        public bool Denied { get; set; }
        public Task<string> GetOperatorIdAsync(CancellationToken cancellationToken = default) =>
            Denied ? throw new UnauthorizedAccessException() : Task.FromResult("operator");
    }

    private sealed class Reservations : IAssociationSlugReservations
    {
        public bool IsReserved(string slug) => AssociationSlugRules.IsReserved(slug);
    }

    private sealed class CreationInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public string? FailTable { get; init; }
        public bool WasTriggered { get; private set; }
        public bool SynchronizeAssociationInsert { get; init; }
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public int Arrivals => arrivals;
        public System.Collections.Concurrent.ConcurrentQueue<(Guid ContextId, DbTransaction? Transaction, string Table)> Writes { get; } = new();
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
            {
                foreach (var table in new[] { "Associations", "AspNetUsers", "AssociationAccountBindings", "AssociationUserMemberships", "ExpenseCategories", "SystemSettings", "PlatformAuditLogs" })
                {
                    if (command.CommandText.Contains($"INSERT INTO [{table}]", StringComparison.Ordinal))
                        Writes.Enqueue((eventData.Context!.ContextId.InstanceId, command.Transaction, table));
                }
            }
            if (Enabled && FailTable is not null && command.CommandText.Contains($"INSERT INTO [{FailTable}]", StringComparison.Ordinal))
            {
                WasTriggered = true;
                throw new InvalidOperationException("Injected provisioning persistence failure.");
            }
            if (Enabled && SynchronizeAssociationInsert && command.CommandText.Contains("INSERT INTO [Associations]", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly AssociationDatabaseFixture databaseFixture = new();
        public ServiceProvider Services { get; private set; } = null!;
        public Access Access { get; } = new();
        public int InitialId { get; private set; }

        public static async Task<Fixture> CreateAsync(CreationInterceptor? interceptor = null)
        {
            var fixture = new Fixture();
            await fixture.databaseFixture.InitializeAsync();
            await using var database = fixture.databaseFixture.CreateUnresolvedContext();
            var connection = database.Database.GetConnectionString();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = connection }).Build());
            if (interceptor is not null)
            {
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection).AddInterceptors(interceptor));
            }
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
            services.AddSingleton<IPlatformAssociationWriteAccess>(fixture.Access);
            services.AddSingleton<IAssociationSlugReservations, Reservations>();
            fixture.Services = services.BuildServiceProvider();
            await using var scope = fixture.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var (id, name) in new[] { ("operator", "operator"), ("existing", "existing-admin"), ("other", "other-admin") })
            {
                var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) + "aA1!";
                Assert.True((await users.CreateAsync(new ApplicationUser { Id = id, UserName = name, Email = "shared-email@example.invalid", FirstName = "Test", LastName = "Account" }, password)).Succeeded);
            }
            var association = await db.Associations.SingleAsync(x => x.Slug == "neftyanik");
            fixture.InitialId = association.Id;
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
            db.AssociationUserMemberships.Add(new AssociationUserMembership { ApplicationUserId = "existing", Role = RoleNames.Member });
            db.AddRange(AssociationFoundationTests.CreateBusinessGraph(association.Id, "existing"));
            await db.SaveChangesAsync();
            return fixture;
        }

        public async Task<string> ExistingDataAsync(string? excludeLogin = null)
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.OpenConnectionAsync();
            var parts = new List<string>();
            foreach (var entity in db.Model.GetEntityTypes().Where(x => typeof(IAssociationOwned).IsAssignableFrom(x.ClrType)))
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = $"SELECT * FROM [{entity.GetTableName()}] WHERE AssociationId = @id ORDER BY Id FOR JSON PATH, INCLUDE_NULL_VALUES";
                var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = InitialId; command.Parameters.Add(parameter);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) parts.Add(reader.GetString(0));
            }
            parts.Add(JsonSerializer.Serialize(await db.Users.AsNoTracking().Where(x => excludeLogin == null || x.UserName != excludeLogin).OrderBy(x => x.Id).ToListAsync()));
            parts.Add(JsonSerializer.Serialize(await db.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync()));
            parts.Add(JsonSerializer.Serialize(await db.PlatformBootstrapStates.AsNoTracking().ToListAsync()));
            return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", parts))));
        }

        public async ValueTask DisposeAsync()
        {
            if (Services is not null) await Services.DisposeAsync();
            await databaseFixture.DisposeAsync();
        }
    }
}
