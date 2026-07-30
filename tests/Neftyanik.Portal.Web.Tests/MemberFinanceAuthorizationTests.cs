using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class MemberFinanceAuthorizationTests
{
    [Fact]
    public async Task GetMemberDashboard_AsAnonymous_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/Member");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_AsAnonymous_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        const int ownedPlotId = 101;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Plots.Add(new Plot { Id = ownedPlotId, Number = "P-101" });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync($"/Member/Plots/{ownedPlotId}/Finance");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForOwnedPlot_ReturnsReadOnlyFinanceDetails()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-1";
        const int memberId = 201;
        const int plotId = 301;
        const int chargeTypeId = 401;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member1@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member One",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "OWNED-PLOT-301",
                Address = "Owned Address 301",
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
                Name = "Членский взнос"
            });
            dbContext.Charges.Add(new Charge
            {
                Id = 1,
                PlotId = plotId,
                ChargeTypeId = chargeTypeId,
                Amount = 150m,
                ChargeDate = new DateOnly(2026, 1, 10),
                Description = "Owned charge description"
            });
            dbContext.Payments.Add(new Payment
            {
                Id = 1,
                PlotId = plotId,
                Amount = 50m,
                PaymentDate = new DateOnly(2026, 1, 11),
                PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.Cash,
                ReferenceNumber = "OWNED-PAYMENT-REF"
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("OWNED-PLOT-301", html, StringComparison.Ordinal);
        Assert.Contains("Owned charge description", html, StringComparison.Ordinal);
        Assert.Contains("OWNED-PAYMENT-REF", html, StringComparison.Ordinal);
        Assert.Contains($"/Member/Plots/{plotId}/Finance", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/Administration/", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_WhenPaymentIsAllocatedAcrossPlots_ShowsAllocationBasedPlotBalances()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-dashboard";
        const int memberId = 1201;
        const int firstPlotId = 1301;
        const int secondPlotId = 1302;
        const int chargeTypeId = 1401;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-dashboard@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member Dashboard",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.AddRange(
                new Plot { Id = firstPlotId, Number = "P-1301", Address = "First Plot", IsActive = true },
                new Plot { Id = secondPlotId, Number = "P-1302", Address = "Second Plot", IsActive = true });
            dbContext.PlotOwnerships.AddRange(
                new PlotOwnership
                {
                    Id = 1,
                    PlotId = firstPlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    OwnershipShare = 1m,
                    IsPrimaryContact = true
                },
                new PlotOwnership
                {
                    Id = 2,
                    PlotId = secondPlotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    OwnershipShare = 1m,
                    IsPrimaryContact = false
                });
            dbContext.ChargeTypes.Add(new ChargeType
            {
                Id = chargeTypeId,
                Name = "Членский взнос",
                IsActive = true
            });
            dbContext.Charges.AddRange(
                new Charge { Id = 1, PlotId = firstPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 1) },
                new Charge { Id = 2, PlotId = secondPlotId, ChargeTypeId = chargeTypeId, Amount = 100m, ChargeDate = new DateOnly(2026, 1, 2) });
            dbContext.Payments.Add(new Payment
            {
                Id = 1,
                PlotId = firstPlotId,
                Amount = 150m,
                PaymentDate = new DateOnly(2026, 1, 10),
                PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.Cash,
                ReferenceNumber = "DASHBOARD-REF"
            });
            dbContext.PaymentAllocations.AddRange(
                new PaymentAllocation { PaymentId = 1, ChargeId = 1, Amount = 100m },
                new PaymentAllocation { PaymentId = 1, ChargeId = 2, Amount = 50m });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), cultureName: "ru-RU");

        var response = await client.GetAsync("/Member");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("P-1301", html, StringComparison.Ordinal);
        Assert.Contains("P-1302", html, StringComparison.Ordinal);
        Assert.Contains("100,00", html, StringComparison.Ordinal);
        Assert.Contains("50,00", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-50,00", html, StringComparison.Ordinal);
        Assert.Contains($"/Member/Plots/{firstPlotId}/Finance", html, StringComparison.Ordinal);
        Assert.Contains($"/Member/Plots/{secondPlotId}/Finance", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_WithChargeTypeFilter_ShowsOnlySelectedCharges()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-filter";
        const int memberId = 2201;
        const int plotId = 2301;
        const int membershipChargeTypeId = 2401;
        const int electricityChargeTypeId = 2402;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-filter@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member Filter",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-2301",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            dbContext.ChargeTypes.AddRange(
                new ChargeType
                {
                    Id = membershipChargeTypeId,
                    Name = "Членский взнос",
                    IsActive = true
                },
                new ChargeType
                {
                    Id = electricityChargeTypeId,
                    Name = "Электроэнергия",
                    IsActive = true
                });
            dbContext.Charges.AddRange(
                new Charge
                {
                    Id = 1,
                    PlotId = plotId,
                    ChargeTypeId = membershipChargeTypeId,
                    Amount = 150m,
                    ChargeDate = new DateOnly(2026, 1, 10),
                    Description = "Membership charge"
                },
                new Charge
                {
                    Id = 2,
                    PlotId = plotId,
                    ChargeTypeId = electricityChargeTypeId,
                    Amount = 250m,
                    ChargeDate = new DateOnly(2026, 1, 11),
                    Description = "Electricity charge"
                });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), cultureName: "ru-RU");

        var response = await client.GetAsync($"/Member?chargeTypeId={electricityChargeTypeId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Electricity charge", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Membership charge", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_WithReadyElectricityMeter_ShowsSubmitReadingButton()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-meter";
        const int memberId = 1501;
        const int plotId = 1502;
        const int meterId = 1503;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-meter@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member With Meter",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-1502",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
            {
                Id = meterId,
                MemberId = memberId,
                BillingPlotId = plotId,
                IsActive = true,
                Readings =
                [
                    new MemberElectricityReading
                    {
                        ReadingDate = new DateOnly(2026, 1, 1),
                        CurrentReading = 100m,
                        IsInitialReading = true
                    }
                ]
            });

            await dbContext.SaveChangesAsync();

            var readyMeterPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
            readyMeterPlot.MemberElectricityMeterId = meterId;

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), cultureName: "ru-RU");

        var response = await client.GetAsync("/Member");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"/Member/Electricity/Meters/{meterId}/Readings/Create", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberDashboard_WithElectricityHistory_ShowsAllReadings()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-history";
        const int memberId = 1601;
        const int plotId = 1602;
        const int meterId = 1603;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-history@example.com"));
            dbContext.Members.Add(new Member
            {
                Id = memberId,
                FullName = "Member With History",
                ApplicationUserId = userId,
                IsActive = true
            });
            dbContext.Plots.Add(new Plot
            {
                Id = plotId,
                Number = "P-1602",
                IsActive = true
            });
            dbContext.PlotOwnerships.Add(new PlotOwnership
            {
                Id = 1,
                PlotId = plotId,
                MemberId = memberId,
                ValidFrom = new DateOnly(2020, 1, 1),
                IsPrimaryContact = true
            });
            dbContext.MemberElectricityMeters.Add(new MemberElectricityMeter
            {
                Id = meterId,
                MemberId = memberId,
                BillingPlotId = plotId,
                IsActive = true,
                Readings =
                [
                    new MemberElectricityReading
                    {
                        ReadingDate = new DateOnly(2026, 1, 1),
                        CurrentReading = 100m,
                        IsInitialReading = true
                    },
                    new MemberElectricityReading
                    {
                        ReadingDate = new DateOnly(2026, 2, 1),
                        CurrentReading = 130m,
                        Amount = 150m,
                        IsInitialReading = false
                    }
                ]
            });

            await dbContext.SaveChangesAsync();

            var historyPlot = await dbContext.Plots.SingleAsync(plot => plot.Id == plotId);
            historyPlot.MemberElectricityMeterId = meterId;

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member), cultureName: "ru-RU");

        var response = await client.GetAsync("/Member");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("01.02.2026", html, StringComparison.Ordinal);
        Assert.Contains("130", html, StringComparison.Ordinal);
        Assert.Contains("кВт·ч", html, StringComparison.Ordinal);
        Assert.Contains("30", html, StringComparison.Ordinal);
        Assert.Contains("150", html, StringComparison.Ordinal);
        Assert.Contains("01.01.2026", html, StringComparison.Ordinal);
        Assert.Contains("100", html, StringComparison.Ordinal);
        Assert.Contains($"/Member/Plots/{plotId}/Finance", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForAnotherMembersPlot_ReturnsNotFoundWithoutLeakingFinanceData()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userAId = "member-user-a";
        const string userBId = "member-user-b";
        const int memberAId = 501;
        const int memberBId = 502;
        const int plotBId = 601;
        const int chargeTypeId = 701;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.AddRange(
                CreateUser(userAId, "member-a@example.com"),
                CreateUser(userBId, "member-b@example.com"));
            dbContext.Members.AddRange(
                new Member { Id = memberAId, FullName = "Member A", ApplicationUserId = userAId, IsActive = true },
                new Member { Id = memberBId, FullName = "Member B", ApplicationUserId = userBId, IsActive = true });
            dbContext.Plots.Add(new Plot
            {
                Id = plotBId,
                Number = "UNOWNED-PLOT-601",
                Address = "Leaked Address Marker 601",
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
            dbContext.ChargeTypes.Add(new ChargeType
            {
                Id = chargeTypeId,
                Name = "Электроэнергия"
            });
            dbContext.Charges.Add(new Charge
            {
                Id = 1,
                PlotId = plotBId,
                ChargeTypeId = chargeTypeId,
                Amount = 321m,
                ChargeDate = new DateOnly(2026, 2, 1),
                Description = "Leaked Charge Marker 601"
            });
            dbContext.Payments.Add(new Payment
            {
                Id = 1,
                PlotId = plotBId,
                Amount = 123m,
                PaymentDate = new DateOnly(2026, 2, 2),
                PaymentMethod = Neftyanik.Portal.Domain.Enums.PaymentMethod.Card,
                ReferenceNumber = "LEAKED-PAYMENT-REF-601",
                Description = "Leaked Payment Description 601"
            });

            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userAId, RoleNames.Member));

        var response = await client.GetAsync($"/Member/Plots/{plotBId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("UNOWNED-PLOT-601", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaked Address Marker 601", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaked Charge Marker 601", html, StringComparison.Ordinal);
        Assert.DoesNotContain("LEAKED-PAYMENT-REF-601", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaked Payment Description 601", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForMemberWithoutLinkedMemberRecord_ReturnsNotFound()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "member-user-unlinked";
        const int plotId = 801;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "member-unlinked@example.com"));
            dbContext.Plots.Add(new Plot { Id = plotId, Number = "P-801" });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForAuthenticatedUserWithoutMemberRole_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "non-member-user";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "non-member@example.com"));
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId));

        var response = await client.GetAsync("/Member/Plots/999/Finance");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMemberPlotFinance_ForAdministratorWithoutMemberRole_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        const string userId = "admin-only-user";

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Users.Add(CreateUser(userId, "admin-only@example.com"));
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Administrator));

        var response = await client.GetAsync("/Member/Plots/999/Finance");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
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
