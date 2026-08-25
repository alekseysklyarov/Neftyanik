#if WEB_TESTS
using System.Net;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class AdministrationAccountantAuthorizationTests
{
    [Fact]
    public async Task GetAdministrationMembersIndex_ForAccountant_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant), cultureName: "ru");

        var response = await client.GetAsync("/Administration/Members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCashInitialization_ForAccountant_ReturnsOkWithoutAdjustmentForm()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.FinancialAuditLogs.Add(new FinancialAuditLog
            {
                Action = FinancialAuditLogActions.Updated,
                EntityType = nameof(SystemSetting),
                EntityId = "1",
                Description = "Скорректирована инициализация кассы. Причина: test"
            });
            dbContext.SystemSettings.Add(new SystemSetting
            {
                Id = 1,
                Key = "Finance.CashInitialization",
                Value = "{\"Amount\":120.00,\"AcceptedAt\":\"2025-01-10\",\"AcceptedFrom\":\"Кассир\",\"AdvancePaymentsAmount\":10.00}",
                Description = "Initial cash amount configured from finance settings.",
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Finance/Settings/CashInitialization");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Сохранить корректировку", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Причина корректировки", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationMemberCreate_ForAccountant_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Members/Create");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCashInitialization_ForMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("member-user", RoleNames.Member));

        var response = await client.GetAsync("/Administration/Finance/Settings/CashInitialization");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationPlotOwnerships_ForAccountant_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Plots/1/Ownerships");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationAuditLogDetails_ForAdministrator_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("administrator-user", RoleNames.Administrator));

        var response = await client.GetAsync("/Administration/AuditLog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationAuditLog_ForAdministrator_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.FinancialAuditLogs.Add(new FinancialAuditLog
            {
                Id = 1,
                Action = FinancialAuditLogActions.Created,
                EntityType = nameof(FinancialAuditLog),
                EntityId = "1",
                Description = "Test audit entry"
            });
            await dbContext.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("administrator-user", RoleNames.Administrator));

        var response = await client.GetAsync("/Administration/AuditLog/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationAuditLog_ForAccountant_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/AuditLog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationAuditLogDetails_ForAccountant_ReturnsOk()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.FinancialAuditLogs.Add(new FinancialAuditLog
            {
                Id = 2,
                Action = FinancialAuditLogActions.Cancelled,
                EntityType = nameof(Payment),
                EntityId = "42",
                Description = "Test financial audit entry"
            });
            await dbContext.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/AuditLog/2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAdministrationAuditLog_ForMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("member-user", RoleNames.Member));

        var response = await client.GetAsync("/Administration/AuditLog");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationAuditLogDetails_ForMember_RedirectsToAccessDenied()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.FinancialAuditLogs.Add(new FinancialAuditLog
            {
                Id = 3,
                Action = FinancialAuditLogActions.Created,
                EntityType = nameof(Charge),
                EntityId = "7"
            });
            await dbContext.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("member-user", RoleNames.Member));

        var response = await client.GetAsync("/Administration/AuditLog/3");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationAuditLog_ForUnauthenticatedUser_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Administration/AuditLog");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationAuditLogDetails_ForUnauthenticatedUser_RedirectsToLogin()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.FinancialAuditLogs.Add(new FinancialAuditLog
            {
                Id = 4,
                Action = FinancialAuditLogActions.Updated,
                EntityType = nameof(Expense),
                EntityId = "9"
            });
            await dbContext.SaveChangesAsync();
        });
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Administration/AuditLog/4");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationDashboard_ForAdministrator_ContainsAuditLogLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("administrator-user", RoleNames.Administrator));

        var response = await client.GetAsync("/Administration");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Administration/AuditLog", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationDashboard_ForAccountant_ContainsAuditLogLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Administration/AuditLog", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationFinance_ForAccountant_ContainsAuditLogLink()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("accountant-user", RoleNames.Accountant));

        var response = await client.GetAsync("/Administration/Finance");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Administration/AuditLog", content, StringComparison.Ordinal);
    }
}
#endif
