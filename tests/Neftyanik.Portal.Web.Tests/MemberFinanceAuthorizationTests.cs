using System.Net;
using Microsoft.AspNetCore.Identity;
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

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(userId, RoleNames.Member));

        var response = await client.GetAsync($"/Member/Plots/{plotId}/Finance");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("OWNED-PLOT-301", html, StringComparison.Ordinal);
        Assert.Contains("Начислено", html, StringComparison.Ordinal);
        Assert.Contains("Оплачено", html, StringComparison.Ordinal);
        Assert.Contains("Баланс", html, StringComparison.Ordinal);
        Assert.Contains("Owned charge description", html, StringComparison.Ordinal);
        Assert.Contains("OWNED-PAYMENT-REF", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Добавить начисление", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Зарегистрировать платеж", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Отменить начисление", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Отменить платеж", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Архивировать", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Изменить", html, StringComparison.Ordinal);
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
