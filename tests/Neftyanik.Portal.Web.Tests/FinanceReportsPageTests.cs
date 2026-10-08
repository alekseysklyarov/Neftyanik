using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class FinanceReportsPageTests
{
    private const string ReportsPath = "/neftyanik/Administration/Finance/Reports";

    [Theory]
    [InlineData(RoleNames.Administrator)]
    [InlineData(RoleNames.Accountant)]
    public async Task Reports_KeepExpenseDataAndFiltersWithoutManagementActions(string role)
    {
        using var factory = new PortalWebApplicationFactory(environmentName: "Development", useSqlite: false);
        using var client = factory.CreateAuthenticatedClient(new("reports-editor", role), cultureName: "ru-RU");
        var categoryId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            var category = new ExpenseCategory { Name = "Report manual category" };
            db.ExpenseCategories.Add(category);
            db.Associations.Add(new Association { Slug = "other-report", Name = "Other association", IsActive = true });
            await db.SaveChangesAsync();
            categoryId = category.Id;
            for (var index = 1; index <= 24; index++)
                db.Expenses.Add(new Expense { ExpenseCategoryId = categoryId, ExpenseDate = new(2026, 10, 1),
                    Amount = 10, Description = $"report-active-{index:00}", CreatedByUserId = "reports-editor" });
            db.Expenses.AddRange(
                new Expense { ExpenseCategoryId = categoryId, ExpenseDate = new(2026, 10, 1), Amount = 90,
                    Description = "report-cancelled", IsCancelled = true, CreatedByUserId = "reports-editor" },
                new Expense { ExpenseCategoryId = ExpenseCategoryIds.ElectricityPayment, ExpenseDate = new(2026, 10, 1),
                    Amount = 55, Description = "report-electricity", CreatedByUserId = "reports-editor" });
            await db.SaveChangesAsync();
        });
        await factory.ExecuteDbContextAsync(async db =>
        {
            var category = new ExpenseCategory { Name = "Foreign category" };
            db.ExpenseCategories.Add(category);
            await db.SaveChangesAsync();
            db.Expenses.Add(new Expense { ExpenseCategoryId = category.Id, Amount = 999, ExpenseDate = new(2026, 10, 1),
                Description = "foreign-report-secret", CreatedByUserId = "reports-editor" });
            await db.SaveChangesAsync();
        }, "other-report");

        var overview = await GetHtmlAsync(client, "/neftyanik/Administration/Finance");
        Assert.Contains($"href=\"{ReportsPath}\"", overview);
        var hub = await GetHtmlAsync(client, ReportsPath);
        Assert.Contains("Взносы и расходование средств", hub);
        Assert.Contains("href=\"/neftyanik/Administration/Finance/Funds\"", hub);
        Assert.Contains($"href=\"{ReportsPath}/Expenses\"", hub);

        var filter = $"?Search=report&Kind=manual&Status=active&ExpenseCategoryId={categoryId}&PageNumber=2";
        var report = await GetHtmlAsync(client, ReportsPath + "/Expenses" + filter);
        var journal = await GetHtmlAsync(client, "/neftyanik/Administration/Finance/Expenses" + filter);
        foreach (var html in new[] { report, journal })
        {
            Assert.Matches("295[,.]00", html);
            Assert.Contains("report-active-01", html);
            Assert.Contains("report-active-04", html);
            Assert.DoesNotContain("report-active-05", html);
            Assert.DoesNotContain("report-electricity", html);
            Assert.DoesNotContain("report-cancelled", html);
            Assert.DoesNotContain("foreign-report-secret", html);
        }
        Assert.Contains("Действия", journal);
        Assert.Contains("/Administration/Finance/Expenses/Edit", journal);
        Assert.Equal(7, Regex.Matches(Regex.Match(report, "<thead.*?</thead>", RegexOptions.Singleline).Value, "<th>").Count);
        Assert.Contains("colspan=\"7\"", report);
        Assert.DoesNotContain("Действия", report);
        foreach (var action in new[] { "Create", "Edit", "Cancel", "Details" })
            Assert.DoesNotContain($"/Administration/Finance/Expenses/{action}", report);
        Assert.DoesNotContain("href=\"/neftyanik/Administration/Finance/Expenses", report);
        Assert.Contains($"href=\"{ReportsPath}/Expenses?", report);
        Assert.Contains("expenseCategoryId=" + categoryId, report);
        Assert.Contains("kind=manual", report);
        await factory.ExecuteDbContextAsync(async db => Assert.False(await db.FinancialAuditLogs.AnyAsync()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(RoleNames.Member)]
    public async Task Reports_RequireFinanceAccess(string? role)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = role is null ? factory.CreateAnonymousClient()
            : factory.CreateAuthenticatedClient(new("reports-member", role));
        foreach (var path in new[] { ReportsPath, ReportsPath + "/Expenses" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains(role is null ? "Login" : "AccessDenied", response.Headers.Location!.OriginalString);
        }
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}
