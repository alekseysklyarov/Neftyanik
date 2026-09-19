using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Identity;
using Xunit;

namespace Neftyanik.Portal.Infrastructure.Tests;

public sealed class PlatformBootstrapMigrationTests
{
    [Theory]
    [InlineData("marker")]
    [InlineData("empty-role")]
    [InlineData("orphan-user")]
    [InlineData("pristine")]
    [InlineData("user-claim")]
    [InlineData("onboarding-token")]
    [InlineData("assignment")]
    public async Task Migration_PreservesConsumedState_AndFailsClosedForAmbiguousLegacyState(string state)
    {
        await using var database = new ApplicationDbContext(AssociationDatabaseFixture.CreateOptions());
        try
        {
            await database.GetService<IMigrator>().MigrateAsync("20260918151504_AddAssociationMemberships");
            if (state is "marker" or "empty-role" or "assignment")
            {
                var role = new IdentityRole(RoleNames.PlatformAdministrator) { NormalizedName = "PLATFORMADMINISTRATOR" };
                database.Roles.Add(role);
                if (state == "marker") database.RoleClaims.Add(new IdentityRoleClaim<string>
                {
                    RoleId = role.Id, ClaimType = PlatformAdministratorOnboarding.BootstrapMarker, ClaimValue = "deleted-user"
                });
                if (state == "assignment")
                {
                    var user = new ApplicationUser { UserName = "prior", FirstName = "Prior", LastName = "Operator" };
                    database.Users.Add(user);
                    database.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
                }
            }
            if (state is "user-claim" or "onboarding-token")
            {
                var user = new ApplicationUser { UserName = "prior", FirstName = "Prior", LastName = "Operator" };
                database.Users.Add(user);
                if (state == "user-claim") database.UserClaims.Add(new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "role", ClaimValue = RoleNames.PlatformAdministrator });
                else database.UserTokens.Add(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = PlatformAdministratorOnboarding.TokenProvider, Name = PlatformAdministratorOnboarding.ExpiryToken, Value = "expired" });
            }
            if (state == "orphan-user") database.Users.Add(new ApplicationUser { UserName = "legacy", FirstName = "Legacy", LastName = "Operator" });
            await database.SaveChangesAsync();
            await database.Database.MigrateAsync();
            var marker = await database.PlatformBootstrapStates.AsNoTracking().SingleOrDefaultAsync();
            if (state == "pristine") Assert.Null(marker);
            else
            {
                Assert.NotNull(marker);
                Assert.Equal(1, marker.Id);
                Assert.Equal(state == "orphan-user" ? PlatformBootstrapDisposition.LegacyReviewRequired : PlatformBootstrapDisposition.Consumed, marker.Disposition);
                Assert.Null(marker.InitializedAtUtc);
                Assert.Null(marker.InitializedUserId);
                Assert.Null(marker.OperatorIdentity);
                Assert.Null(marker.ApprovalReference);
                if (state == "marker") Assert.Contains("migrated", marker.Reason);
                database.Roles.RemoveRange(await database.Roles.Where(x => x.Name == RoleNames.PlatformAdministrator).ToListAsync());
                database.Users.RemoveRange(await database.Users.ToListAsync());
                await database.SaveChangesAsync();
                await database.Database.MigrateAsync();
                var preserved = Assert.Single(await database.PlatformBootstrapStates.AsNoTracking().ToListAsync());
                Assert.Equal(marker.Disposition, preserved.Disposition);
                Assert.Equal(marker.ConsumedAtUtc, preserved.ConsumedAtUtc);
            }
        }
        finally { await database.Database.EnsureDeletedAsync(); }
    }
}
