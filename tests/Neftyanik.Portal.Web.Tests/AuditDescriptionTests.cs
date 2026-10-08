using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Web.Pages.Finance;
using Xunit;
using AuditIndex = Neftyanik.Portal.Web.Pages.Administration.FinancialAuditLog.IndexModel;

namespace Neftyanik.Portal.Web.Tests;

public class AuditDescriptionTests
{
    [Fact]
    public async Task ExistingEvents_ShowMemberPlotAndHistoricalAmounts_WithoutChangingAudit()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db);
            var entries = await db.FinancialAuditLogs.OrderBy(a => a.Id).ToListAsync();
            var descriptions = await AuditDescriptionFormatter.FormatAsync(db, entries);
            var payment = descriptions[entries[0].Id]!;
            Assert.Contains("Андрущенко", payment);
            Assert.Contains("участок 186", payment);
            Assert.Contains("125,50 грн", payment);
            Assert.DoesNotContain("999", payment);
            Assert.DoesNotContain("Новый владелец", payment);
            Assert.DoesNotContain("#67", payment);
            Assert.Contains("743,00 грн", descriptions[entries[1].Id]);
            Assert.Contains("Андрущенко", descriptions[entries[1].Id]);
            // A pending notification has no selected plot. Do not infer it from current ownership.
            Assert.DoesNotContain("участок", descriptions[entries[1].Id]);
            Assert.Contains("Т1 140 кВт·ч", descriptions[entries[2].Id]);
            Assert.Contains("45,00 грн", descriptions[entries[2].Id]);
            Assert.Contains("участок 186", descriptions[entries[2].Id]);
            Assert.Contains("45,00 грн", descriptions[entries[3].Id]);
            var advance = descriptions[entries[4].Id]!;
            Assert.Contains("Сумма зачёта: 10,00 грн", advance);
            Assert.Contains("Андрущенко", advance);
            Assert.DoesNotContain("90,00 грн", advance);
            Assert.DoesNotContain("#37", advance);
            Assert.Equal("Создан платеж #67.", (await db.FinancialAuditLogs.AsNoTracking().FirstAsync()).Description);
        });
    }

    [Fact]
    public async Task SearchAndDetails_UseSameReadableDescriptions_AndEscapeNames()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(Seed);
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("audit-reader", RoleNames.Accountant), cultureName: "ru-RU");
        var response = await client.GetAsync("/neftyanik/Administration/AuditLog?search=" + Uri.EscapeDataString("Андрущенко"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("125,50 грн", html);
        Assert.Contains("Сумма зачёта: 10,00 грн", html);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var byPlot = new AuditIndex(db) { Search = "186" };
            await byPlot.OnGetAsync(default);
            Assert.Equal(4, byPlot.TotalCount);
            (await db.Members.SingleAsync(m => m.Id == 1)).FullName = "Андрущенко <script>alert(1)</script>";
            await db.SaveChangesAsync();
        });
        var detailsResponse = await client.GetAsync("/neftyanik/Administration/AuditLog/1");
        Assert.Equal(HttpStatusCode.OK, detailsResponse.StatusCode);
        var rawHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains("&lt;script&gt;", rawHtml);
        Assert.DoesNotContain("<script>alert(1)</script>", rawHtml);
        var details = await detailsResponse.ReadDecodedHtmlAsync();
        Assert.Contains("125,50 грн", details);
        Assert.Contains("Создан платеж #67.", details);
    }

    [Fact]
    public async Task MissingEntitiesAndMalformedSnapshots_KeepReadableFallback_WithoutCrossTenantLookup()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await Seed(db);
            db.Associations.Add(new Association { Name = "Other", Slug = "second" });
            await db.SaveChangesAsync();
        });
        await factory.ExecuteDbContextAsync(async db =>
        {
            var member = new Member { FullName = "Чужое товарищество" };
            db.Payments.Add(new Payment { Id = 900, Member = member, Amount = 900 });
            await db.SaveChangesAsync();
        }, "second");
        await factory.ExecuteDbContextAsync(async db =>
        {
            var entries = new[]
            {
                Entry(10, "Payment", 900, "Создан платеж #900.", null),
                Entry(11, "Payment", 901, "Создан платеж #901.", new { MemberId = 1, PlotId = 101, Amount = 777m }),
                Entry(12, "Payment", 902, "Создан платеж #902.", null),
                Entry(13, "Payment", 67, "Исправление платежа: ошибка суммы", new { MemberId = 1, PlotId = 101, Amount = 80m })
            };
            entries[2].NewValuesJson = "{broken";
            entries[3].OldValuesJson = "{\"Amount\":125.50}";
            var descriptions = await AuditDescriptionFormatter.FormatAsync(db, entries);
            Assert.Equal(entries[0].Description, descriptions[10]);
            Assert.Contains("Андрущенко", descriptions[11]);
            Assert.Contains("777,00 грн", descriptions[11]);
            Assert.Equal(entries[2].Description, descriptions[12]);
            Assert.Contains("125,50 грн → 80,00 грн", descriptions[13]);
            Assert.DoesNotContain("Чужое товарищество", string.Join(" ", descriptions.Values));
        });
    }

    private static FinancialAuditLog Entry(long id, string type, long entityId, string description, object? values) => new()
    {
        Id = id, EntityType = type, EntityId = entityId.ToString(), Action = "Create", Description = description,
        NewValuesJson = values is null ? null : JsonSerializer.Serialize(values)
    };

    private static async Task Seed(ApplicationDbContext db)
    {
        db.Members.AddRange(new Member { Id = 1, FullName = "Андрущенко" }, new Member { Id = 2, FullName = "Новый владелец" });
        db.Plots.Add(new Plot { Id = 101, Number = "186" });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 2, PlotId = 101 });
        db.ChargeTypes.Add(new ChargeType { Id = 1, Name = "Электроэнергия" });
        db.Charges.AddRange(new Charge { Id = 103, MemberId = 1, PlotId = 101, ChargeTypeId = 1, Amount = 90 },
            new Charge { Id = 105, MemberId = 1, PlotId = 101, ChargeTypeId = 1, Amount = 90 });
        db.Payments.AddRange(new Payment { Id = 67, MemberId = 1, PlotId = 101, Amount = 999 }, new Payment { Id = 37, MemberId = 1, PlotId = 101, Amount = 500 });
        db.PaymentNotifications.Add(new PaymentNotification { Id = 4, MemberId = 1, Amount = 743 });
        db.MemberElectricityMeters.Add(new MemberElectricityMeter { Id = 14, MemberId = 1, BillingPlotId = 101, Name = "Основной" });
        db.MemberElectricityReadings.Add(new MemberElectricityReading { Id = 87, MemberElectricityMeterId = 14, CurrentReading = 150, Amount = 90, ChargeId = 103 });
        db.FinancialAuditLogs.AddRange(
            Entry(1, "Payment", 67, "Создан платеж #67.", new { MemberId = 1, PlotId = 101, Amount = 125.50m }),
            Entry(2, "PaymentNotification", 4, "Создано уведомление о платеже #4.", new { MemberId = 1, Amount = 743m }),
            Entry(3, "MemberElectricityReading", 87, "Добавлено показание #87 для счетчика #14. Создано начисление #103.", new { Value = 140m, Amount = 45m, RelatedChargeId = 103 }),
            Entry(4, "Charge", 103, "Создано начисление #103.", new { MemberId = 1, PlotId = 101, Amount = 45m }),
            Entry(5, "Charge", 105, "Зачтён аванс из платежа #37 в начисление #105.", new { PaymentId = 37, ChargeId = 105, Amount = 10m, Source = "Advance" }));
        await db.SaveChangesAsync();
    }
}
