using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Electricity;
using Neftyanik.Portal.Application.Finance;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Neftyanik.Portal.Infrastructure.Data.Queries;
using Neftyanik.Portal.Infrastructure.Services;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class MemberReadingDeletionTests
{
    private static readonly DateOnly Date = new(2026, 10, 8);
    private static FinancialAuditService Audit(ApplicationDbContext db) => new(db, new HttpContextAccessor());
    private static MemberElectricityService Readings(ApplicationDbContext db) => new(db, Audit(db));
    private static MemberReadingDeletionService Deletion(ApplicationDbContext db) => new(db, Audit(db));
    private static string Url(long readingId, int meterId = 901) => $"/neftyanik/Administration/Electricity/Meters/{meterId}/Readings/{readingId}/Delete";

    [Theory]
    [InlineData(0)]
    [InlineData(125)]
    [InlineData(300)]
    public async Task DeleteLatest_CancelsChargePreservesPaymentsAndReusesAdvance(decimal paid)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            if (paid > 0)
            {
                db.Payments.Add(new Payment { MemberId = 701, PlotId = 801, Amount = paid, PaymentDate = Date, PaymentMethod = PaymentMethod.Cash });
                await db.SaveChangesAsync();
            }
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            var latest = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            var oldChargeId = latest.ChargeId!.Value;
            var oldAllocated = (await db.PaymentAllocations.Select(a => a.Amount).ToListAsync()).Sum();
            Assert.Equal(Math.Min(250, paid), oldAllocated);
            Assert.Equal(MemberReadingDeletionResultCode.Success,
                (await Deletion(db).DeleteLatestAsync(901, latest.Id, "Ошибочный ввод", "admin")).Code);
            db.ChangeTracker.Clear();
            Assert.Equal(100, (await db.MemberElectricityReadings.SingleAsync()).CurrentReading);
            Assert.NotNull((await db.Charges.SingleAsync()).CancelledAtUtc);
            Assert.Equal(-paid, await db.CalculateActiveBalanceAsync(701, []));
            Assert.Equal(paid, (await db.Payments.Select(p => p.Amount).ToListAsync()).Sum());
            Assert.Equal(oldAllocated, (await db.PaymentAllocations.Select(a => a.Amount).ToListAsync()).Sum());
            Assert.Equal(MemberReadingDeletionResultCode.NotFound,
                (await Deletion(db).DeleteLatestAsync(901, latest.Id, "Повтор", "admin")).Code);
            // The deleted date can be resubmitted using the preceding reading and released advance.
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 140, null, null))).Succeeded);
            var replacement = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.Equal(200, replacement.Amount);
            Assert.NotEqual(oldChargeId, replacement.ChargeId);
            Assert.Equal(Math.Min(200, paid), (await db.PaymentAllocations
                .Where(a => a.Charge!.CancelledAtUtc == null).Select(a => a.Amount).ToListAsync()).Sum());
        });
    }

    [Fact]
    public async Task DeleteLatest_PreservesTwoMeterAndDayNightDetailsInAudit()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db, dayNight: true);
            (await db.Members.SingleAsync()).HasTwoElectricityMeters = true;
            await db.SaveChangesAsync();
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 70, 40, null, true, 80, 30))).Succeeded);
            var latest = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            var breakdown = latest.PhysicalMeterReadingsJson;
            Assert.True((await Deletion(db).DeleteLatestAsync(901, latest.Id, "Неверные показания", "admin")).Succeeded);
            var entry = await db.FinancialAuditLogs.SingleAsync(a => a.EntityType == nameof(MemberElectricityReading)
                && a.Action == FinancialAuditLogActions.Deleted);
            using var values = JsonDocument.Parse(entry.OldValuesJson!);
            Assert.Equal(150, values.RootElement.GetProperty("CurrentReading").GetDecimal());
            Assert.Equal(70, values.RootElement.GetProperty("CurrentNightReading").GetDecimal());
            Assert.Equal(breakdown, values.RootElement.GetProperty("PhysicalMeterReadingsJson").GetString());
            Assert.Equal(290, (await db.Charges.SingleAsync()).Amount);
            Assert.NotNull((await db.Charges.SingleAsync()).CancelledAtUtc);
        });
    }

    [Fact]
    public async Task DeleteLatest_AlreadyCancelledChargeDoesNotCreateDuplicateCancellation()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            var latest = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.True((await new ChargeService(db, Audit(db)).CancelChargeAsync(new(latest.ChargeId!.Value, "Отмена"))).Succeeded);
            Assert.True((await Deletion(db).DeleteLatestAsync(901, latest.Id, "Удаление", "admin")).Succeeded);
            Assert.Equal(1, await db.FinancialAuditLogs.CountAsync(a => a.EntityType == nameof(Charge) && a.Action == FinancialAuditLogActions.Cancelled));
        });
    }

    [Fact]
    public async Task DeleteInitialOnlyReading_AllowsReinitialization()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            var initial = await db.MemberElectricityReadings.SingleAsync();
            Assert.True((await Deletion(db).DeleteLatestAsync(901, initial.Id, "Ошибочная инициализация", "admin")).Succeeded);
            Assert.Empty(await db.MemberElectricityReadings.ToListAsync());
            Assert.Empty(await db.Charges.ToListAsync());
            Assert.True((await Readings(db).CreateInitialReadingAsync(new(901, Date, 120, null, null))).Succeeded);
            Assert.Equal(120, (await db.MemberElectricityReadings.SingleAsync()).CurrentReading);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InvalidReason_DoesNotDeleteOrCancel(string? reason)
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            var latest = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            Assert.Equal(MemberReadingDeletionResultCode.InvalidReason, (await Deletion(db).DeleteLatestAsync(901, latest.Id, reason, "admin")).Code);
            Assert.Equal(MemberReadingDeletionResultCode.InvalidReason, (await Deletion(db).DeleteLatestAsync(901, latest.Id, new string('x', 501), "admin")).Code);
            Assert.Equal(MemberReadingDeletionResultCode.InvalidActor, (await Deletion(db).DeleteLatestAsync(901, latest.Id, "Причина", "")).Code);
            Assert.Equal(2, await db.MemberElectricityReadings.CountAsync());
            Assert.Null((await db.Charges.SingleAsync()).CancelledAtUtc);
        });
    }

    [Fact]
    public async Task AuditFailure_RollsBackReadingDeletionAndChargeCancellation()
    {
        using var factory = new PortalWebApplicationFactory();
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            var latest = await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading);
            var auditsBefore = await db.FinancialAuditLogs.CountAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => new MemberReadingDeletionService(db, new ThrowOnDeletionAudit(db))
                .DeleteLatestAsync(901, latest.Id, "Причина", "admin"));
            db.ChangeTracker.Clear();
            Assert.Equal(2, await db.MemberElectricityReadings.CountAsync());
            Assert.Null((await db.Charges.SingleAsync()).CancelledAtUtc);
            Assert.Equal(auditsBefore, await db.FinancialAuditLogs.CountAsync());
        });
    }

    [Theory]
    [InlineData(RoleNames.Administrator)]
    [InlineData(RoleNames.Accountant)]
    public async Task Page_ConfirmsBeforeDeletionAndPostReturnsToMemberFinance(string role)
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("delete-reading-admin", role), cultureName: "ru-RU");
        long readingId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            readingId = (await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading)).Id;
        });
        var financeHtml = await (await client.GetAsync("/neftyanik/Administration/Members/Finance/701/Finance")).ReadDecodedHtmlAsync();
        Assert.Contains(Url(readingId), financeHtml);
        var html = await (await client.GetAsync(Url(readingId))).ReadDecodedHtmlAsync();
        Assert.Contains("Удалить последнее показание", html);
        var token = Token(html);
        Assert.NotEmpty(token);
        await factory.ExecuteDbContextAsync(async db => Assert.Equal(2, await db.MemberElectricityReadings.CountAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Url(readingId), new FormUrlEncodedContent(
            new Dictionary<string, string> { ["Input.Reason"] = "Без токена" }))).StatusCode);
        var response = await client.PostAsync(Url(readingId), Form(token, "Ошибочные показания"));
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/neftyanik/Administration/Members/Finance/701/Finance", response.Headers.Location?.OriginalString);
        await factory.ExecuteDbContextAsync(async db => Assert.Single(await db.MemberElectricityReadings.ToListAsync()));
    }

    [Fact]
    public async Task StalePage_CannotDeleteEarlierReadingOrAnotherMetersReading()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("stale-delete-admin", RoleNames.Administrator));
        long previousId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date, 150, null, null))).Succeeded);
            previousId = (await db.MemberElectricityReadings.SingleAsync(r => !r.IsInitialReading)).Id;
        });
        var token = Token(await (await client.GetAsync(Url(previousId))).ReadDecodedHtmlAsync());
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.True((await Readings(db).CreateReadingAsync(new(901, Date.AddDays(1), 160, null, null))).Succeeded);
            Assert.Equal(MemberReadingDeletionResultCode.NotLatest,
                (await Deletion(db).DeleteLatestAsync(901, previousId, "Устаревшая форма", "admin")).Code);
            Assert.Equal(MemberReadingDeletionResultCode.NotFound,
                (await Deletion(db).DeleteLatestAsync(999, previousId, "Другой счётчик", "admin")).Code);
        });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(previousId))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(Url(previousId), Form(token, "Устаревшая форма"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(previousId, 999))).StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(3, await db.MemberElectricityReadings.CountAsync());
            Assert.False(await db.Charges.AnyAsync(c => c.CancelledAtUtc != null));
        });
    }

    [Fact]
    public async Task Member_CannotOpenOrSubmitDeletionPage()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("delete-forbidden-member", RoleNames.Member));
        foreach (var response in new[] { await client.GetAsync(Url(1)), await client.PostAsync(Url(1), Form("", "Попытка")) })
        {
            Assert.Equal(HttpStatusCode.Found, response.StatusCode);
            Assert.Contains("AccessDenied", response.Headers.Location?.OriginalString);
        }
    }

    [Fact]
    public async Task OtherAssociation_CannotSeeOrDeleteReading()
    {
        using var factory = new PortalWebApplicationFactory();
        long readingId = 0;
        await factory.ExecuteDbContextAsync(async db =>
        {
            await SeedAsync(db);
            readingId = (await db.MemberElectricityReadings.SingleAsync()).Id;
            db.Associations.Add(new Association { Name = "Other", Slug = "delete-other", IsActive = true });
            await db.SaveChangesAsync();
        });
        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser("other-delete-admin", RoleNames.Administrator), associationSlug: "delete-other");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(readingId).Replace("/neftyanik/", "/delete-other/"))).StatusCode);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(MemberReadingDeletionResultCode.NotFound,
                (await Deletion(db).DeleteLatestAsync(901, readingId, "Другое товарищество", "admin")).Code);
        }, "delete-other");
        await factory.ExecuteDbContextAsync(async db => Assert.Single(await db.MemberElectricityReadings.ToListAsync()));
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string token, string reason) => new(new Dictionary<string, string>
        { ["__RequestVerificationToken"] = token, ["Input.Reason"] = reason });

    private sealed class ThrowOnDeletionAudit(ApplicationDbContext db) : IFinancialAuditService
    {
        public void Add(string action, string entityType, string entityId, string? description = null, object? oldValues = null, object? newValues = null)
        {
            if (action == FinancialAuditLogActions.Deleted) throw new InvalidOperationException("Audit failed");
            Audit(db).Add(action, entityType, entityId, description, oldValues, newValues);
        }
    }

    private static async Task SeedAsync(ApplicationDbContext db, bool dayNight = false)
    {
        db.Members.Add(new Member { Id = 701, FullName = "Delete Reading Member", IsActive = true,
            ElectricityMeterType = dayNight ? MemberElectricityMeterType.DayNight : MemberElectricityMeterType.SingleRate });
        db.Plots.Add(new Plot { Id = 801, Number = "D-801", IsActive = true });
        db.PlotOwnerships.Add(new PlotOwnership { MemberId = 701, PlotId = 801, ValidFrom = new(2020, 1, 1) });
        db.MemberElectricityMeters.Add(new MemberElectricityMeter { Id = 901, MemberId = 701, BillingPlotId = 801, IsActive = true,
            Readings = [new MemberElectricityReading { ReadingDate = Date.AddMonths(-1), CurrentReading = 100,
                CurrentNightReading = dayNight ? 50m : null, IsInitialReading = true }] });
        db.MemberElectricityTariffs.Add(new MemberElectricityTariff { EffectiveFrom = new(2020, 1, 1), Rate = 5m, NightRate = dayNight ? 2m : null });
        await db.SaveChangesAsync();
        (await db.Plots.SingleAsync()).MemberElectricityMeterId = 901;
        await db.SaveChangesAsync();
    }
}
