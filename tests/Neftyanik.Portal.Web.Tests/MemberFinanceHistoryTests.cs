using System.Net;
using System.Text.RegularExpressions;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class MemberFinanceHistoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_ExpandsIndependently_KeepsEarlierRecordsAndTotals_AndStopsAtEnd(bool personalAccount)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Users.Add(new ApplicationUser { Id = "history-member", UserName = "history@example.com",
                NormalizedUserName = "HISTORY@EXAMPLE.COM", Email = "history@example.com", SecurityStamp = Guid.NewGuid().ToString(),
                FirstName = "History", LastName = "Member", IsActive = true, MustChangePassword = false });
            db.Members.Add(new Member { Id = 701, FullName = "History member", ApplicationUserId = "history-member" });
            db.Plots.Add(new Plot { Id = 801, Number = "H-801" });
            db.PlotOwnerships.Add(new PlotOwnership { MemberId = 701, PlotId = 801 });
            db.ChargeTypes.Add(new ChargeType { Id = 1, Name = "History charge" });
            db.ChargeTypes.Add(new ChargeType { Id = 2, Name = "Other charge" });
            for (var i = 1; i <= 35; i++)
            {
                db.Charges.Add(new Charge { Id = i, PlotId = 801, ChargeTypeId = i % 2 == 0 ? 2 : 1, Amount = 10,
                    ChargeDate = new(2026, 1, 1), Description = $"charge-detail-{i:D2}" });
                db.Payments.Add(new Payment { Id = i, MemberId = 701, PlotId = 801, Amount = 5,
                    PaymentDate = new(2026, 1, 1), Description = $"payment-detail-{i:D2}" });
            }
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(personalAccount
            ? new TestAuthenticatedUser("history-member", RoleNames.Member)
            : new TestAuthenticatedUser("history-admin", RoleNames.Administrator), cultureName: "ru-RU");
        var pageUrl = personalAccount ? "/neftyanik/Member" : "/neftyanik/Administration/Members/Finance/701/Finance";
        var cases = personalAccount ? new[]
        {
            (1, 1, 5, 5), (2, 1, 20, 5), (2, 2, 20, 20), (3, 2, 35, 20), (4, 4, 35, 35),
            (int.MaxValue, int.MaxValue, 35, 35), (-1, 0, 5, 5)
        } : new[]
        {
            (1, 1, 3, 3), (2, 1, 18, 3), (2, 2, 18, 18), (3, 2, 33, 18), (4, 4, 35, 35),
            (int.MaxValue, int.MaxValue, 35, 35), (-1, 0, 3, 3)
        };
        foreach (var (chargePage, paymentPage, charges, payments) in cases)
        {
            var response = await client.GetAsync($"{pageUrl}?chargePage={chargePage}&paymentPage={paymentPage}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.ReadDecodedHtmlAsync();
            Assert.Equal(Enumerable.Range(36 - charges, charges).Reverse().Select(i => $"charge-detail-{i:D2}"),
                Regex.Matches(html, "charge-detail-[0-9]{2}").Select(m => m.Value));
            Assert.Equal(Enumerable.Range(36 - payments, payments).Reverse().Select(i => $"payment-detail-{i:D2}"),
                Regex.Matches(html, "payment-detail-[0-9]{2}").Select(m => m.Value));
            Assert.Contains("350,00", html);
            Assert.Contains("175,00", html);
            Assert.Equal(charges < 35, html.Contains("data-history-more aria-controls=\"charge-history\""));
            Assert.Equal(payments < 35, html.Contains("data-history-more aria-controls=\"payment-history\""));
        }

        if (personalAccount)
        {
            foreach (var (page, count) in new[] { (1, 5), (2, 17) })
            {
                var html = await (await client.GetAsync($"{pageUrl}?chargeTypeId=2&chargePage={page}&paymentPage=2")).ReadDecodedHtmlAsync();
                Assert.Equal(Enumerable.Range(1, 17).Reverse().Take(count).Select(i => $"charge-detail-{i * 2:D2}"),
                    Regex.Matches(html, "charge-detail-[0-9]{2}").Select(m => m.Value));
                Assert.Equal(20, Regex.Matches(html, "payment-detail-[0-9]{2}").Count);
                Assert.Contains("350,00", html);
            }
        }
    }
}
