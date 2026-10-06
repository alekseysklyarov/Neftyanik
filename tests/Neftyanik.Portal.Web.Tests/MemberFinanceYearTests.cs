using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Pages.Administration.Members;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class MemberFinanceYearTests
{
    [Fact]
    public async Task YearScope_CarriesDebtAndCredit_ExcludesFutureAndCancelledPayments_AndPreservesHistoryYear()
    {
        using var factory = new PortalWebApplicationFactory();
        var year = DateTime.Today.Year;
        await factory.SeedMembershipsAsync(new TestAuthenticatedUser("year-member", RoleNames.Member));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Users.Single(u => u.Id == "year-member").MustChangePassword = false;
            db.Members.Add(new Member { Id = 701, FullName = "Year member", ApplicationUserId = "year-member" });
            db.Plots.AddRange(new Plot { Id = 801, Number = "Y-801" }, new Plot { Id = 802, Number = "Y-802" });
            db.PlotOwnerships.AddRange(new PlotOwnership { MemberId = 701, PlotId = 801 }, new PlotOwnership { MemberId = 701, PlotId = 802 });
            db.ChargeTypes.Add(new ChargeType { Id = 1, Name = "Annual", IsYearly = true });
            db.Charges.AddRange(
                new Charge { Id = 1, PlotId = 801, ChargeTypeId = 1, Amount = 500, ChargeDate = new(year - 1, 12, 31), Description = "previous-charge" },
                new Charge { Id = 2, PlotId = 802, ChargeTypeId = 1, Amount = 300, ChargeDate = new(year - 1, 12, 31) },
                new Charge { Id = 7, PlotId = 801, ChargeTypeId = 1, Amount = 900, ChargeDate = new(year - 1, 1, 1), CancelledAtUtc = DateTime.UtcNow });
            for (var i = 3; i <= 6; i++)
                db.Charges.Add(new Charge { Id = i, PlotId = 801, ChargeTypeId = 1, Amount = 30, ChargeDate = new(year, 1, 1), Description = $"selected-charge-{i}" });
            db.Payments.AddRange(
                new Payment { Id = 1, MemberId = 701, PlotId = 801, Amount = 200, PaymentDate = new(year - 1, 12, 31) },
                new Payment { Id = 2, MemberId = 701, PlotId = 801, Amount = 350, PaymentDate = new(year, 1, 1), Description = "selected-payment" },
                new Payment { Id = 3, MemberId = 701, PlotId = 801, Amount = 1000, PaymentDate = new(year + 1, 1, 1), Description = "future-payment" },
                new Payment { Id = 4, MemberId = 701, PlotId = 801, Amount = 1000, PaymentDate = new(year, 1, 1), CancelledAtUtc = DateTime.UtcNow });
            db.PaymentAllocations.AddRange(
                new PaymentAllocation { PaymentId = 1, ChargeId = 1, Amount = 100 },
                new PaymentAllocation { PaymentId = 1, ChargeId = 2, Amount = 100 },
                new PaymentAllocation { PaymentId = 2, ChargeId = 1, Amount = 250 },
                new PaymentAllocation { PaymentId = 2, ChargeId = 2, Amount = 100 },
                new PaymentAllocation { PaymentId = 3, ChargeId = 1, Amount = 150 },
                new PaymentAllocation { PaymentId = 3, ChargeId = 2, Amount = 100 },
                new PaymentAllocation { PaymentId = 3, ChargeId = 3, Amount = 30 });
            await db.SaveChangesAsync();

            var service = new MemberElectricityService(db, new FinancialAuditService(db, new HttpContextAccessor()));
            foreach (var (selectedYear, opening, charged, paid, closing) in new[]
            {
                (year - 1, 0m, 800m, 200m, 600m),
                (year, 600m, 120m, 350m, 370m),
                (year + 1, 370m, 0m, 1000m, -630m),
                (year + 2, -630m, 0m, 0m, -630m)
            })
            {
                var page = new FinanceModel(db, service, null!) { Year = selectedYear };
                await page.OnGetAsync(701, default);
                Assert.Equal(opening, page.OpeningBalance);
                Assert.Equal(charged, page.Member.TotalCharges);
                Assert.Equal(paid, page.Member.TotalPayments);
                Assert.Equal(closing, page.Member.Balance);
                Assert.Equal(closing, page.Plots.Sum(p => p.Balance));
                Assert.All(page.Charges, c => Assert.Equal(selectedYear, c.ChargeDate.Year));
                Assert.All(page.Payments, p => Assert.Equal(selectedYear, p.PaymentDate.Year));
                if (selectedYear == year)
                {
                    Assert.Equal(270m, page.Plots.Single(p => p.PlotId == 801).Balance);
                    Assert.Equal(100m, page.Plots.Single(p => p.PlotId == 802).Balance);
                    Assert.Equal(4, page.ChargeCount);
                    Assert.Equal(3, page.Charges.Count);
                }
                if (opening < 0) Assert.Equal(630m, page.OpeningCredit);
            }
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("year-admin", RoleNames.Administrator), cultureName: "ru-RU");
        const string url = "/neftyanik/Administration/Members/Finance/701/Finance";
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("Долг на начало года", html);
        Assert.Contains("600,00", html);
        Assert.Contains($"year={year}", html);
        Assert.DoesNotContain("previous-charge", html);
        Assert.DoesNotContain("future-payment", html);
        html = await (await client.GetAsync($"{url}?year={year}&chargePage=2")).ReadDecodedHtmlAsync();
        Assert.Contains("selected-charge-3", html);
        Assert.Contains("selected-charge-6", html);
        html = await (await client.GetAsync($"{url}?year={year - 1}")).ReadDecodedHtmlAsync();
        Assert.Contains("previous-charge", html);
        Assert.DoesNotContain("selected-payment", html);
        Assert.DoesNotContain("selected-charge", html);

        using var memberClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("year-member", RoleNames.Member), cultureName: "ru-RU");
        foreach (var (selectedYear, opening, charged, paid, closing) in new[]
        {
            (year - 1, 0m, 800m, 200m, 600m),
            (year, 600m, 120m, 350m, 370m),
            (year + 1, 370m, 0m, 1000m, -630m),
            (year + 2, -630m, 0m, 0m, -630m)
        })
        {
            var memberUrl = selectedYear == year ? "/neftyanik/Member" : $"/neftyanik/Member?year={selectedYear}";
            var memberResponse = await memberClient.GetAsync(memberUrl);
            Assert.Equal(HttpStatusCode.OK, memberResponse.StatusCode);
            var memberHtml = await memberResponse.ReadDecodedHtmlAsync();
            var values = Regex.Matches(memberHtml, "<p class=\"member-dashboard-summary-value[^>]*>(.*?)</p>")
                .Select(m => decimal.Parse(Regex.Match(m.Groups[1].Value, @"\d+(?:,\d+)?").Value, CultureInfo.GetCultureInfo("ru-RU"))).ToArray();
            Assert.Equal(new[] { 2m, Math.Abs(opening), charged, paid, Math.Abs(closing) }, values);
            Assert.Contains($"year={selectedYear}", memberHtml);
            Assert.Contains(opening < 0 ? "Переплата на начало года" : "Долг на начало года", memberHtml);
            if (selectedYear == year)
            {
                Assert.DoesNotContain("previous-charge", memberHtml);
                Assert.DoesNotContain("future-payment", memberHtml);
            }
        }
        var filteredHtml = await (await memberClient.GetAsync($"/neftyanik/Member?year={year - 1}&chargeTypeId=1")).ReadDecodedHtmlAsync();
        Assert.Contains("previous-charge", filteredHtml);
        Assert.DoesNotContain("selected-charge", filteredHtml);
        Assert.Contains($"year={year - 1}", filteredHtml);
    }
}
