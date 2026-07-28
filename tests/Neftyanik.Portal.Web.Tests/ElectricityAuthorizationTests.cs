using System.Net;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class ElectricityAuthorizationTests
{
    [Fact]
    public async Task GetAdminElectricityTariffs_AsAnonymous_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/Administration/Finance/Electricity/Tariffs");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdminElectricityTariffs_AsMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-tariffs";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-tariffs@example.com"));
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync("/Administration/Finance/Electricity/Tariffs");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdminPlotElectricityCreate_AsMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-create-reading";
        const int plotId = 1001;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-create-reading@example.com"));
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-1001", IsActive = true });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync($"/Administration/Plots/{plotId}/Electricity/Create");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForOwnedPlot_ShowsElectricityHistory()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-electricity-owned";
        const int memberId = 2001;
        const int plotId = 3001;
        const int chargeTypeId = 4001;
        const long chargeId = 5001;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-electricity-owned@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member Electricity Owned",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "OWNED-ELECTRICITY-3001",
                Address = "Owned Electricity Address",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                OwnershipShare = 0.5m,
                IsPrimaryContact = true
            });
            dbContext.ChargeTypes.Add(new ChargeType
            {
                Id = chargeTypeId,
                Code = ChargeTypeCodes.Electricity,
                Name = "Электроэнергия",
                IsActive = true
            });
            dbContext.Charges.Add(new Charge
            {
                Id = chargeId,
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 670.98m,
                ChargeDate = new DateOnly(2026, 7, 31),
                Description = "Электроэнергия за 31.07.2026"
            });
            dbContext.ElectricityReadings.AddRange(
                new ElectricityReading
                {
                    Id = 1,
                    PlotId = plotId,
                    ReadingDate = new DateOnly(2026, 7, 1),
                    CurrentDayReading = 12320.4m,
                    CurrentNightReading = 6180.1m,
                    IsInitialReading = true
                },
                new ElectricityReading
                {
                    Id = 2,
                    PlotId = plotId,
                    ReadingDate = new DateOnly(2026, 7, 31),
                    PreviousDayReading = 12320.4m,
                    CurrentDayReading = 12450.7m,
                    DayConsumption = 130.3m,
                    DayRate = 4.3200m,
                    DayAmount = 562.90m,
                    PreviousNightReading = 6180.1m,
                    CurrentNightReading = 6230.2m,
                    NightConsumption = 50.1m,
                    NightRate = 2.1560m,
                    NightAmount = 108.08m,
                    TotalAmount = 670.98m,
                    IsInitialReading = false,
                    ChargeId = chargeId
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Электроэнергия", html, StringComparison.Ordinal);
        Assert.Contains("OWNED-ELECTRICITY-3001", html, StringComparison.Ordinal);
        Assert.Contains("130,300", html, StringComparison.Ordinal);
        Assert.Contains("50,100", html, StringComparison.Ordinal);
        Assert.Contains("4,3200", html, StringComparison.Ordinal);
        Assert.Contains("670,98", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForAnotherMembersPlot_DoesNotLeakElectricityHistory()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userAId = "member-electricity-a";
        const string userBId = "member-electricity-b";
        const int memberAId = 6001;
        const int memberBId = 6002;
        const int plotBId = 7001;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.AddRange(
                CreateUser(userAId, "member-electricity-a@example.com"),
                CreateUser(userBId, "member-electricity-b@example.com"));
            dbContext.Members.AddRange(
                new Member { Id = memberAId, FullName = "Member A", ApplicationUserId = userAId, IsActive = true },
                new Member { Id = memberBId, FullName = "Member B", ApplicationUserId = userBId, IsActive = true });
            dbContext.Plots.Add(new Plot
            {
                Id = plotBId,
                Number = "HIDDEN-ELECTRICITY-7001",
                Address = "Hidden Electricity Address",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotBId,
                MemberId = memberBId,
                ValidFrom = new DateOnly(2020, 1, 1),
                OwnershipShare = 0.5m,
                IsPrimaryContact = true
            });
            dbContext.ElectricityReadings.Add(new ElectricityReading
            {
                Id = 1,
                PlotId = plotBId,
                ReadingDate = new DateOnly(2026, 7, 31),
                CurrentDayReading = 9999.9m,
                CurrentNightReading = 5555.5m,
                IsInitialReading = true
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userAId, RoleNames.Member));

        var response = await client.GetAsync($"/Member/Plots/{plotBId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("HIDDEN-ELECTRICITY-7001", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Hidden Electricity Address", html, StringComparison.Ordinal);
        Assert.DoesNotContain("9999.9", html, StringComparison.Ordinal);
        Assert.DoesNotContain("5555.5", html, StringComparison.Ordinal);
    }

    private static ApplicationUser CreateUser(string id, string email)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Test",
            LastName = "User",
            MustChangePassword = false,
            IsActive = true
        };
    }
}
