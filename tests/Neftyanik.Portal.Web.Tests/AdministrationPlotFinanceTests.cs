using System.Net;
using System.Text.RegularExpressions;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class AdministrationPlotFinanceTests
{
    [Fact]
    public async Task PlotBalances_RespectCrossPlotAllocationsCancellationsAndFinancialFilters()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.ChargeTypes.AddRange(
                new ChargeType { Id = 1, Name = "Annual", IsYearly = true },
                new ChargeType { Id = 2, Name = "Electricity", Code = ChargeTypeCodes.Electricity },
                new ChargeType { Id = 3, Name = "Other" });
            for (var i = 1; i <= 28; i++)
                db.Plots.Add(new Plot { Id = i, Number = $"FIN-{i:D2}", IsActive = true });
            db.Charges.AddRange(
                new Charge { Id = 1, PlotId = 26, ChargeTypeId = 1, Amount = 100, ChargeDate = new(2026, 1, 1) },
                new Charge { Id = 2, PlotId = 27, ChargeTypeId = 1, Amount = 100, ChargeDate = new(2026, 1, 1) },
                new Charge { Id = 3, PlotId = 28, ChargeTypeId = 1, Amount = 75, ChargeDate = new(2026, 1, 1) },
                new Charge { Id = 4, PlotId = 26, ChargeTypeId = 1, Amount = 900, ChargeDate = new(2026, 1, 1), CancelledAtUtc = DateTime.UtcNow },
                new Charge { Id = 5, PlotId = 27, ChargeTypeId = 2, Amount = 700, ChargeDate = new(2026, 1, 1) },
                new Charge { Id = 6, PlotId = 27, ChargeTypeId = 3, Amount = 200, ChargeDate = new(2026, 1, 1) });
            db.Payments.AddRange(
                new Payment { Id = 1, PlotId = 26, Amount = 250, PaymentDate = new(2026, 1, 2) },
                new Payment { Id = 2, PlotId = 28, Amount = 75, PaymentDate = new(2026, 1, 2), CancelledAtUtc = DateTime.UtcNow },
                new Payment { Id = 3, PlotId = 27, Amount = 900, PaymentDate = new(2026, 1, 2) });
            db.PaymentAllocations.AddRange(
                new PaymentAllocation { PaymentId = 1, ChargeId = 1, Amount = 100 },
                new PaymentAllocation { PaymentId = 1, ChargeId = 2, Amount = 100 },
                new PaymentAllocation { PaymentId = 2, ChargeId = 3, Amount = 75 },
                new PaymentAllocation { PaymentId = 3, ChargeId = 5, Amount = 700 },
                new PaymentAllocation { PaymentId = 3, ChargeId = 6, Amount = 200 });
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("finance-admin", RoleNames.Administrator), cultureName: "ru-RU");
        const string url = "/neftyanik/Administration/Plots";

        var response = await client.GetAsync($"{url}?financialStatus=debt");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("FIN-28", html);
        Assert.DoesNotContain("FIN-26", html);
        Assert.DoesNotContain("FIN-27", html);
        Assert.Contains("75,00 ₴", html);
        Assert.Contains("financialStatus=debt", html);

        html = await (await client.GetAsync($"{url}?financialStatus=overpayment")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain("FIN-26", html);
        Assert.DoesNotContain("FIN-27", html);
        Assert.DoesNotContain("FIN-28", html);

        // The 50 advance has no category and must not be counted as an annual payment.
        html = await (await client.GetAsync($"{url}?search=FIN-26")).ReadDecodedHtmlAsync();
        Assert.Equal(2, Regex.Matches(html, "100,00 ₴").Count);
        Assert.Contains("Задолженности нет", html);
        Assert.DoesNotContain("150,00 ₴", html);

        html = await (await client.GetAsync($"{url}?financialStatus=nodebt&search=FIN-27")).ReadDecodedHtmlAsync();
        Assert.Contains("FIN-27", html);
        Assert.Equal(2, Regex.Matches(html, "100,00 ₴").Count);
        Assert.Contains("Задолженности нет", html);
        Assert.DoesNotContain("FIN-26", html);
        Assert.DoesNotContain("FIN-28", html);

        // Filtering must happen before pagination, including on later pages.
        html = await (await client.GetAsync($"{url}?financialStatus=nodebt&pageNumber=2")).ReadDecodedHtmlAsync();
        Assert.Contains("FIN-27", html);
        Assert.Contains("FIN-26", html);
        Assert.DoesNotContain("FIN-28", html);
        Assert.Contains("financialStatus=nodebt", html);

        var overview = await client.GetAsync("/neftyanik/Administration/Finance");
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        html = await overview.ReadDecodedHtmlAsync();
        Assert.Contains("Участки и балансы", html);
        Assert.DoesNotContain("<table", html);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var page = new Neftyanik.Portal.Web.Pages.Administration.Finance.IndexModel(db);
            await page.OnGetAsync(default);
            Assert.Equal(1, page.Summary.PlotsWithDebtCount);
            Assert.Equal(1, page.Summary.PlotsWithOverpaymentCount);
            Assert.Equal(26, page.Summary.PlotsWithZeroBalanceCount);
        });
    }
}
