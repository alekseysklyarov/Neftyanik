using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Web.Pages.Administration.Finance.Settings;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class AssociationReadinessPageTests
{
    private const string OverviewPath = "/new-readiness/Administration/Finance/Settings";

    [Theory]
    [InlineData(RoleNames.Administrator)]
    [InlineData(RoleNames.Accountant)]
    public async Task GetAsync_NewTenant_ShowsReadOnlyCardsAndExistingTenantLinks(string role)
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateTenantAsync(factory);
        using var client = factory.CreateAuthenticatedClient(new("readiness-user", role), cultureName: "ru-RU", associationSlug: "new-readiness");
        var response = await client.GetAsync(OverviewPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var cards = Regex.Matches(html, "<section[^>]*data-readiness-area=.*?</section>", RegexOptions.Singleline);
        Assert.Equal(9, cards.Count);
        Assert.All(cards.Cast<Match>(), card =>
        {
            Assert.Contains("class=\"badge", card.Value);
            Assert.Contains("href=\"/new-readiness/Administration/", card.Value);
            Assert.DoesNotContain("<form", card.Value);
            Assert.DoesNotContain("<input", card.Value);
        });
        Assert.Contains("Учёт начинается с нулевого остатка", html);
        Assert.Contains("Что уже можно делать", html);
        Assert.Contains("Автоматические ставки членских взносов пока не подключены к начислениям", html);
        Assert.Contains("Функция не реализована", Card(html, ReadinessArea.MembershipFeeRates));
        Assert.Contains("Не используется / нет данных", Card(html, ReadinessArea.IndividualMeters));
        foreach (var page in new[]
        {
            "Electricity/MemberTariffs", "Electricity/Association/Tariffs", "Electricity/Association/Initial",
            "Electricity/Meters", "Finance/ChargeTypes", "Finance/ExpenseCategories",
            "Finance/Settings/CashInitialization", "Members", "Plots"
        })
        {
            Assert.Contains($"href=\"/new-readiness/Administration/{page}\"", html);
            using var target = await client.GetAsync($"/new-readiness/Administration/{page}");
            Assert.NotEqual(HttpStatusCode.NotFound, target.StatusCode);
            Assert.NotEqual(HttpStatusCode.InternalServerError, target.StatusCode);
        }
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.SystemSettings.AnyAsync());
            Assert.False(await db.Charges.AnyAsync());
            Assert.False(await db.Expenses.AnyAsync());
            Assert.False(await db.Payments.AnyAsync());
            Assert.False(await db.FinancialAuditLogs.AnyAsync());
        }, "new-readiness");
    }

    [Theory]
    [InlineData("uk-UA", "Облік починається з нульового залишку", "Функцію не реалізовано")]
    [InlineData("en-US", "Accounting starts with a zero balance", "Not implemented")]
    public async Task GetAsync_UsesExistingLocalization(string culture, string cashText, string unimplementedText)
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateTenantAsync(factory);
        using var client = factory.CreateAuthenticatedClient(new("localized-admin", RoleNames.Administrator), cultureName: culture, associationSlug: "new-readiness");
        var response = await client.GetAsync(OverviewPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(cashText, Card(html, ReadinessArea.Cash));
        Assert.Contains(unimplementedText, Card(html, ReadinessArea.MembershipFeeRates));
        Assert.DoesNotContain("Учёт начинается", html);
    }

    [Fact]
    public async Task GetAsync_CashInitialization_ShowsDateWithoutAmountsOrNames()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateTenantAsync(factory);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.SystemSettings.Add(new() { Key = "Finance.CashInitialization", Value = "{\"Amount\":987654.32,\"AcceptedAt\":\"2026-09-25\",\"AcceptedFrom\":\"PrivateCashSource\",\"AdvancePaymentsAmount\":123456.78}" });
            db.MembershipFeeRates.Add(new() { Year = 2035, AmountPerPlot = 777 });
            await db.SaveChangesAsync();
        }, "new-readiness");
        using var client = factory.CreateAuthenticatedClient(new("cash-admin", RoleNames.Administrator), cultureName: "ru-RU", associationSlug: "new-readiness");
        var response = await client.GetAsync(OverviewPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("25.09.2026", Card(html, ReadinessArea.Cash));
        Assert.Contains("Готово", Card(html, ReadinessArea.Cash));
        Assert.DoesNotContain("PrivateCashSource", html);
        Assert.DoesNotContain("987", Card(html, ReadinessArea.Cash));
        Assert.DoesNotContain("123", Card(html, ReadinessArea.Cash));
        Assert.Contains("Функция не реализована", Card(html, ReadinessArea.MembershipFeeRates));
        Assert.DoesNotContain("777", Card(html, ReadinessArea.MembershipFeeRates));
    }

    [Fact]
    public async Task GetAsync_SecondTenant_DoesNotUseFirstTenantData()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateTenantAsync(factory);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Members.Add(new() { FullName = "Private foreign member" });
            db.Plots.Add(new() { Number = "Private foreign plot" });
            db.MemberElectricityTariffs.Add(new() { EffectiveFrom = new(2020, 1, 1), Rate = 5 });
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new("isolated-admin", RoleNames.Administrator), cultureName: "ru-RU", associationSlug: "new-readiness");
        var response = await client.GetAsync(OverviewPath);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Участников: 0; участков: 0", Card(html, ReadinessArea.MembersAndPlots));
        Assert.Contains("На сегодня нет действующего тарифа", Card(html, ReadinessArea.MemberElectricity));
        Assert.DoesNotContain("Private foreign", html);
        using var forbidden = await client.GetAsync("/neftyanik/Administration/Finance/Settings");
        Assert.NotEqual(HttpStatusCode.OK, forbidden.StatusCode);
    }

    [Fact]
    public async Task GetAsync_MemberAndAnonymous_CannotAccessFinanceReadiness()
    {
        using var factory = new PortalWebApplicationFactory();
        await CreateTenantAsync(factory);
        using var member = factory.CreateAuthenticatedClient(new("readiness-member", RoleNames.Member), associationSlug: "new-readiness");
        using var anonymous = factory.CreateAnonymousClient();
        using var memberResponse = await member.GetAsync(OverviewPath);
        using var anonymousResponse = await anonymous.GetAsync(OverviewPath);
        Assert.Equal(HttpStatusCode.Found, memberResponse.StatusCode);
        Assert.Contains("AccessDenied", memberResponse.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Found, anonymousResponse.StatusCode);
        Assert.Contains("Login", anonymousResponse.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task OnGetAsync_PassesCancellationTokenToReadOnlyService()
    {
        var service = new StubReadinessService();
        using var cancellation = new CancellationTokenSource();
        var page = new IndexModel(service);
        await page.OnGetAsync(cancellation.Token);
        Assert.Equal(cancellation.Token, service.Token);
        Assert.Same(service.Snapshot, page.Readiness);
        Assert.Equal(ReadinessArea.MembershipFeeRates, Assert.Single(page.Cards).Area);
    }

    private static string Card(string html, ReadinessArea area)
    {
        var match = Regex.Match(html, $"<section[^>]*data-readiness-area=\"{area}\".*?</section>", RegexOptions.Singleline);
        Assert.True(match.Success);
        return match.Value;
    }

    private static Task CreateTenantAsync(PortalWebApplicationFactory factory) => factory.ExecuteDbContextAsync(async db =>
    {
        db.Associations.Add(new Association { Slug = "new-readiness", Name = "Readiness tenant", IsActive = true });
        await db.SaveChangesAsync();
    });

    private sealed class StubReadinessService : IAssociationReadinessService
    {
        public CancellationToken Token { get; private set; }
        public AssociationReadinessSnapshot Snapshot { get; } = new(new(2026, 9, 25),
            [new(ReadinessArea.MembershipFeeRates, ReadinessStatus.NotImplemented, ReadinessReason.MembershipRatesNotIntegrated)], new());

        public Task<AssociationReadinessSnapshot> GetAsync(CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            return Task.FromResult(Snapshot);
        }
    }
}
