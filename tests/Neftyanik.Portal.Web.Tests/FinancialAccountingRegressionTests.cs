using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Services;
using Neftyanik.Portal.Web.Pages.Administration.Finance;
using Neftyanik.Portal.Web.Pages.Finance;
using Xunit;
using ChargeType = Neftyanik.Portal.Domain.Entities.ChargeType;

namespace Neftyanik.Portal.Web.Tests;

public class FinancialAccountingRegressionTests
{
    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("0000000000000000")]
    [InlineData("UA213223130000026007233566001")]
    [InlineData("41111111111a1111")]
    public async Task PaymentInstructions_RejectInvalidCardWithoutSaving(string card)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("card-admin", RoleNames.Administrator));
        const string url = "/neftyanik/Administration/Finance/Settings/PaymentInstructions";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token), ["Input.Recipient"] = "Получатель",
            ["Input.CardNumber"] = card, ["Input.Purpose"] = "Взносы"
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Проверьте номер карты", await response.ReadDecodedHtmlAsync());
        await factory.ExecuteDbContextAsync(async db => Assert.False(await db.SystemSettings.AnyAsync(s => s.Key == PaymentInstructionsData.SettingKey)));
    }

    [Fact]
    public async Task PaymentInstructions_LegacyIbanIsNotShownAsCard_AndNewCardAppearsForMember()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("card-member", RoleNames.Member));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Members.Add(new Member { Id = 711, FullName = "Card Member", ApplicationUserId = "card-member" });
            db.SystemSettings.Add(new SystemSetting { Key = PaymentInstructionsData.SettingKey,
                Value = "{\"Recipient\":\"Получатель\",\"Iban\":\"UA213223130000026007233566001\",\"Purpose\":\"Взносы\"}" });
            await db.SaveChangesAsync();
            var legacy = await PaymentInstructionsData.LoadAsync(db, default);
            Assert.False(legacy.IsConfigured);
            Assert.Equal("Получатель", legacy.Recipient);
            (await db.SystemSettings.SingleAsync(s => s.Key == PaymentInstructionsData.SettingKey)).Value =
                System.Text.Json.JsonSerializer.Serialize(new PaymentInstructionsData("Получатель", "4111111111111111", "Взносы"));
            await db.SaveChangesAsync();
        });
        var html = await (await client.GetAsync("/neftyanik/Member")).ReadDecodedHtmlAsync();
        Assert.Contains("Номер карты:", html);
        Assert.Contains("4111111111111111", html);
        Assert.DoesNotContain("IBAN:", html);
    }

    [Fact]
    public async Task PaymentInstructions_SaveLongCyrillicTextWithEmptyOptionalContact()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("instructions-admin", RoleNames.Administrator));
        const string url = "/neftyanik/Administration/Finance/Settings/PaymentInstructions";
        var html = await (await client.GetAsync(url)).ReadDecodedHtmlAsync();
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        var purpose = new string('я', 500);
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token),
            ["Input.Recipient"] = new string('ю', 200), ["Input.CardNumber"] = "4111 1111-1111 1111",
            ["Input.Purpose"] = purpose, ["Input.Contact"] = ""
        }));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var setting = await db.SystemSettings.SingleAsync(s => s.Key == PaymentInstructionsData.SettingKey);
            Assert.True(setting.Value.Length <= 2000);
            var data = await PaymentInstructionsData.LoadAsync(db, default);
            Assert.Equal(purpose, data.Purpose);
            Assert.Equal("", data.Contact);
            Assert.Equal("4111111111111111", data.CardNumber);
        });
    }

    [Fact]
    public async Task AssignedHistoricalCharge_IsVisibleWithoutOwnershipRecord()
    {
        using var factory = new PortalWebApplicationFactory();
        using var memberClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("historical-member", RoleNames.Member));
        using var adminClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("historical-admin", RoleNames.Administrator));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Members.Add(new Member { Id = 711, FullName = "Historical Member", ApplicationUserId = "historical-member" });
            db.Plots.Add(new Plot { Id = 811, Number = "HISTORICAL-811" });
            db.ChargeTypes.Add(new ChargeType { Id = 911, Name = "Historical fee" });
            db.Charges.Add(new Charge { MemberId = 711, PlotId = 811, ChargeTypeId = 911, Amount = 123,
                ChargeDate = DateOnly.FromDateTime(DateTime.Today) });
            await db.SaveChangesAsync();
        });
        foreach (var (client, url) in new[] { (memberClient, "/neftyanik/Member"),
            (memberClient, "/neftyanik/Member/Plots/811/Finance"),
            (adminClient, "/neftyanik/Administration/Members/Finance/711/Finance") })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode}");
            Assert.Contains("HISTORICAL-811", await response.ReadDecodedHtmlAsync());
        }
    }

    [Fact]
    public async Task SupplierPartialPayments_UseActualDateAndAccount_CancellationReopensDebt()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("supplier-admin", RoleNames.Administrator));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.AssociationElectricityReadings.Add(new AssociationElectricityReading
                { Id = 501, ReadingDate = new(2026, 1, 10), TotalSupplierAmount = 1000, CurrentDayReading = 100, CurrentNightReading = 20 });
            db.Payments.Add(new Payment { Amount = 2000, PaymentDate = new(2026, 1, 1), PaymentMethod = PaymentMethod.BankTransfer });
            await db.SaveChangesAsync();
            var service = new AssociationElectricityService(db, new FinancialAuditService(db, new HttpContextAccessor()));
            Assert.True((await service.CreateExpenseAsync(new(501, "supplier-admin", new(2026, 2, 1), 400, PaymentMethod.BankTransfer, "BANK-1"))).Succeeded);
            Assert.False((await service.CreateExpenseAsync(new(501, "supplier-admin", new(2026, 2, 1), 601, PaymentMethod.BankTransfer))).Succeeded);
            Assert.True((await service.CreateExpenseAsync(new(501, "supplier-admin", new(2026, 3, 1), 600, PaymentMethod.BankTransfer))).Succeeded);
            var payments = await db.Expenses.OrderBy(e => e.ExpenseDate).ToListAsync();
            Assert.Equal(new DateOnly(2026, 2, 1), payments[0].ExpenseDate);
            Assert.All(payments, p => Assert.Equal(PaymentMethod.BankTransfer, p.PaymentMethod));
            var overview = new IndexModel(db);
            await overview.OnGetAsync(default);
            var cash = overview.Summary;
            Assert.Equal(0, cash.CurrentCashOnlyAmount);
            Assert.Equal(1000, cash.CurrentNonCashAmount);
            payments[0].IsCancelled = true;
            await db.SaveChangesAsync();
            Assert.True((await service.CreateExpenseAsync(new(501, "supplier-admin", new(2026, 4, 1), 400, PaymentMethod.BankTransfer))).Succeeded);
            Assert.False((await service.CreateExpenseAsync(new(501, "supplier-admin", new(2026, 4, 1), 1, PaymentMethod.BankTransfer))).Succeeded);
            Assert.Equal(3, await db.Expenses.CountAsync());
        });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/neftyanik/Administration/Finance/Expenses/Electricity/Pay/501")).StatusCode);
    }

    [Fact]
    public async Task Funds_SeparatesDuesElectricityAndUnallocatedAdvance()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("report-admin", RoleNames.Administrator));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Members.Add(new Member { Id = 701, FullName = "Report Member" });
            db.ChargeTypes.AddRange(new ChargeType { Id = 801, Name = "Dues", IsMembershipFee = true },
                new ChargeType { Id = 802, Name = "Electricity", Code = ChargeTypeCodes.Electricity });
            db.Charges.AddRange(new Charge { Id = 901, MemberId = 701, ChargeTypeId = 801, Amount = 100, ChargeDate = new(2025, 12, 1) },
                new Charge { Id = 902, MemberId = 701, ChargeTypeId = 802, Amount = 50, ChargeDate = new(2026, 2, 1) });
            db.Payments.AddRange(new Payment { Id = 903, MemberId = 701, Amount = 100, PaymentDate = new(2025, 12, 1), PaymentMethod = PaymentMethod.Cash },
                new Payment { Id = 904, MemberId = 701, Amount = 70, PaymentDate = new(2026, 2, 1), PaymentMethod = PaymentMethod.Cash });
            db.PaymentAllocations.AddRange(new PaymentAllocation { PaymentId = 903, ChargeId = 901, Amount = 100 },
                new PaymentAllocation { PaymentId = 904, ChargeId = 902, Amount = 50 });
            db.Expenses.Add(new Expense { ExpenseCategoryId = ExpenseCategoryIds.ElectricityPayment, Amount = 30,
                FundingSource = 1, CreatedByUserId = "report-admin", ExpenseDate = new(2026, 2, 5) });
            await db.SaveChangesAsync();
            var report = new FundsModel(db) { From = new(2026, 1, 1), To = new(2026, 3, 1) };
            await report.OnGetAsync(default);
            Assert.Equal(20, report.Rows[0].Received);
            Assert.Equal(100, report.Rows[1].Opening);
            Assert.Equal(30, report.Rows[1].Spent);
            Assert.Equal(70, report.Rows[1].Closing);
            Assert.Equal(50, report.Rows[2].Received);
            Assert.Equal(140, report.Rows.Sum(r => r.Closing));
        });
    }

    [Theory]
    [InlineData("/neftyanik/Administration/Finance/Funds")]
    [InlineData("/neftyanik/Administration/Finance/UnassignedCharges")]
    [InlineData("/neftyanik/Administration/Finance/Settings/PaymentInstructions")]
    public async Task NewFinancePages_RenderForAdministrator(string url)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("finance-admin", RoleNames.Administrator));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }
}
