using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Associations;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public class PlatformAssociationReadTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Found)]
    [InlineData("member", HttpStatusCode.Forbidden)]
    [InlineData("accountant", HttpStatusCode.Forbidden)]
    [InlineData("tenant-admin", HttpStatusCode.Forbidden)]
    [InlineData("operator", HttpStatusCode.OK)]
    public async Task Pages_RequirePlatformAdministratorWithRealIdentityCookie(string? user, HttpStatusCode expected)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync(user);
        foreach (var path in new[] { "/Platform", "/Platform/Associations", $"/Platform/Associations/Details?id={fixture.SleepingId}", $"/Platform/Associations/AddAdministrator/{fixture.SummerId}" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(expected, response.StatusCode);
            if (user is null)
            {
                Assert.Equal("/Platform/Account/Login", response.Headers.Location?.OriginalString);
            }
        }
        if (user != "operator")
        {
            var token = await AuthenticationCookieTests.TokenAsync(client, "/neftyanik/Privacy");
            using var post = await client.PostAsync($"/Platform/Associations/AddAdministrator/{fixture.SummerId}",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token, ["Input.UserName"] = "forged-admin", ["Input.Email"] = "test@example.invalid",
                    ["Input.TemporaryPassword"] = "Temporary123!", ["Input.ConfirmPassword"] = "Temporary123!", ["Input.ConfirmAssignment"] = "true"
                }));
            Assert.Equal(expected, post.StatusCode);
        }
    }

    [Fact]
    public async Task List_CountsMembersRatherThanRolesAndIncludesInactiveAssociationAdministrators()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("Товариства", html);
        Assert.Contains("Нефтяник", html);
        Assert.Contains("Літній сад", html);
        Assert.Contains("Спляче товариство", html);
        var row = Regex.Match(html, $"<tr data-association-id=\"{fixture.SleepingId}\">(.*?)</tr>", RegexOptions.Singleline).Value;
        Assert.Contains("Неактивне", row);
        Assert.Contains("data-member-count=\"3\"", row);
        Assert.Contains("data-administrator-count=\"2\"", row);
        Assert.Contains($"/Platform/Associations/Details?id={fixture.SleepingId}", row);
        Assert.DoesNotContain("private-personal-value", html);
        using var dashboard = await client.GetAsync("/Platform");
        Assert.Contains("/Platform/Associations", await dashboard.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?Search=sleeping", "Спляче товариство", "Літній сад")]
    [InlineData("?Search=%D0%9B%D1%96%D1%82%D0%BD%D1%96%D0%B9", "Літній сад", "Спляче товариство")]
    [InlineData("?IsActive=false", "Спляче товариство", "Нефтяник")]
    [InlineData("?IsActive=true", "Літній сад", "Спляче товариство")]
    public async Task List_FiltersByNameSlugAndStatus(string query, string included, string excluded)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync("/Platform/Associations" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains(included, html);
        Assert.DoesNotContain(excluded, html);
    }

    [Fact]
    public async Task List_PaginatesOnServerAndPreservesFilters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            database.Associations.AddRange(Enumerable.Range(1, 23).Select(i =>
                new Association { Name = $"Paged {i:00}", Slug = $"paged-{i}", IsActive = false }));
            await database.SaveChangesAsync();
        }
        using var client = await fixture.ClientAsync("operator");
        using var first = await client.GetAsync("/Platform/Associations?Search=Paged&IsActive=false");
        var html = await first.ReadDecodedHtmlAsync();
        Assert.Equal(20, Regex.Matches(html, "<tr data-association-id=").Count);
        Assert.Contains("Paged 01", html);
        Assert.DoesNotContain("Paged 21", html);
        Assert.Contains("search=Paged", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isActive=False", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pageNumber=2", html, StringComparison.OrdinalIgnoreCase);
        using var last = await client.GetAsync("/Platform/Associations?Search=Paged&IsActive=false&PageNumber=2147483647");
        var lastHtml = await last.ReadDecodedHtmlAsync();
        Assert.Equal(3, Regex.Matches(lastHtml, "<tr data-association-id=").Count);
        Assert.Contains("Paged 23", lastHtml);
        using var negative = await client.GetAsync("/Platform/Associations?Search=Paged&PageNumber=-1");
        Assert.Contains("Paged 01", await negative.ReadDecodedHtmlAsync());
        using var empty = await client.GetAsync("/Platform/Associations?Search=no-matches");
        Assert.Contains("Товариства не знайдено", await empty.ReadDecodedHtmlAsync());
        using var invalid = await client.GetAsync("/Platform/Associations?IsActive=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Details_ShowsOnlyRequiredAdministratorFieldsAndHandlesMissingAndEmptyAssociations()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        using var response = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains("sleeping-admin", html);
        Assert.Contains("blocked-admin", html);
        Assert.Contains("former-admin", html);
        Assert.Contains("Вимкнене", html);
        Assert.Contains("Заблокований", html);
        Assert.Contains("Неактивний", html);
        Assert.Contains("Дата створення (UTC)", html);
        Assert.Contains("data-member-count=\"3\"", html);
        Assert.DoesNotContain("private-personal-value", html);
        Assert.DoesNotContain("tenant-admin", html);
        Assert.DoesNotContain("SecurityStamp", html);
        Assert.DoesNotContain("PasswordHash", html);
        using var empty = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SummerId}");
        Assert.Contains("Адміністраторів не призначено", await empty.ReadDecodedHtmlAsync());
        using var missing = await client.GetAsync("/Platform/Associations/Details?id=2147483647");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Read_DoesNotGrantTenantFinanceAccessOrChangeGlobalState()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.SnapshotAsync();
        using var client = await fixture.ClientAsync("operator");
        using var list = await client.GetAsync("/Platform/Associations");
        using var details = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        foreach (var slug in new[] { "neftyanik", "summer" })
        {
            using var finance = await client.GetAsync($"/{slug}/Administration/Finance");
            Assert.Equal(HttpStatusCode.Found, finance.StatusCode);
            Assert.Contains($"/{slug}/Account/AccessDenied", finance.Headers.Location!.OriginalString);
        }
        using var inactive = await client.GetAsync("/sleeping/Administration/Finance");
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        using var prefixed = await client.GetAsync("/neftyanik/Platform/Associations");
        Assert.Equal(HttpStatusCode.NotFound, prefixed.StatusCode);
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Fact]
    public async Task ForgedRoleAndRevokedPlatformRole_CannotReadAssociations()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var forged = await fixture.ClientAsync("tenant-admin", forgePlatformClaim: true);
        using var denied = await forged.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var deniedHistory = await forged.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}&historyPage=2");
        Assert.Equal(HttpStatusCode.Forbidden, deniedHistory.StatusCode);
        using var client = await fixture.ClientAsync("operator");
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.RemoveFromRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
        }
        using var revoked = await client.GetAsync("/Platform/Associations");
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
        using var revokedHistory = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}&historyPage=2");
        Assert.Equal(HttpStatusCode.Forbidden, revokedHistory.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("member")]
    [InlineData("accountant")]
    [InlineData("tenant-admin")]
    public async Task Service_RejectsDirectCallsWithoutPlatformPermission(string? user)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = user is null ? null : await fixture.ContextAsync(scope.ServiceProvider, user);
            var reader = scope.ServiceProvider.GetRequiredService<IPlatformAssociationReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetPageAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetDetailsAsync(fixture.SleepingId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetHistoryAsync(fixture.SleepingId));
            var overview = scope.ServiceProvider.GetRequiredService<IPlatformOverviewReader>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => overview.GetAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => overview.GetSetupAsync(fixture.SleepingId));
            var creator = scope.ServiceProvider.GetRequiredService<IPlatformAssociationAdministratorCreator>();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.CreateAsync(fixture.SummerId,
                new("denied", "test@example.invalid", null, "Temporary123!", true)));
        }
        finally { accessor.HttpContext = null; }
    }

    [Fact]
    public async Task Service_ReadsWithUnresolvedContextWithoutWeakeningTenantFilters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.App.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        try
        {
            accessor.HttpContext = await fixture.ContextAsync(scope.ServiceProvider, "operator");
            var reader = scope.ServiceProvider.GetRequiredService<IPlatformAssociationReader>();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var page = await reader.GetPageAsync();
            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.Items.Single(x => x.Id == fixture.SleepingId).AdministratorCount);
            Assert.NotNull(await reader.GetDetailsAsync(fixture.SleepingId));
            var overview = scope.ServiceProvider.GetRequiredService<IPlatformOverviewReader>();
            Assert.Equal(3, (await overview.GetAsync()).TotalCount);
            Assert.NotNull(await overview.GetSetupAsync(fixture.SleepingId));
            Assert.False(database.IsAssociationResolved);
            Assert.Empty(database.ChangeTracker.Entries<IAssociationOwned>());
            Assert.All(database.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
            Assert.Empty(await database.Members.ToListAsync());
            Assert.Empty(await database.AssociationUserMemberships.ToListAsync());
            Assert.Empty(await database.Charges.ToListAsync());
            accessor.HttpContext.Request.Path = "/neftyanik/Administration";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetPageAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetHistoryAsync(fixture.SleepingId));
            accessor.HttpContext.Request.Path = "/Platform/Associations";
            scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(await database.Associations.SingleAsync(x => x.Slug == "neftyanik"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetDetailsAsync(fixture.SleepingId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.GetHistoryAsync(fixture.SleepingId));
        }
        finally { accessor.HttpContext = null; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_OnlySelectedAssociationHasStablePagesAndReadDoesNotWrite(bool useSqlite)
    {
        await using var fixture = await Fixture.CreateAsync(useSqlite);
        var entries = new List<PlatformAuditLog>();
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(useSqlite, database.Database.IsSqlite());
            Assert.Equal(!useSqlite, database.Database.IsSqlServer());
            var timestamp = database.Model.FindEntityType(typeof(PlatformAuditLog))!
                .FindProperty(nameof(PlatformAuditLog.OccurredAtUtc))!;
            if (useSqlite)
            {
                Assert.Equal(typeof(long), timestamp.GetValueConverter()!.ProviderClrType);
                Assert.Equal("INTEGER", timestamp.GetColumnType());
            }
            else
            {
                Assert.Null(timestamp.GetValueConverter());
                Assert.Equal("datetimeoffset", timestamp.GetColumnType());
            }
            for (var i = 0; i < 43; i++)
            {
                entries.Add(new PlatformAuditLog
                {
                    AssociationId = fixture.SleepingId, OperatorUserId = "operator",
                    // Equal instants with different offsets, plus 100 ns differences.
                    OccurredAtUtc = new DateTimeOffset(2026, 9, 26, 12, i % 3, 0, TimeSpan.Zero)
                        .AddTicks(i % 2).ToOffset(TimeSpan.FromHours(i % 5 - 2)),
                    Action = PlatformAuditActions.AssociationEdited,
                    OldValuesJson = "{\"Name\":\"Old name\"}", NewValuesJson = $"{{\"Name\":\"History {i}\"}}"
                });
            }
            database.PlatformAuditLogs.AddRange(entries);
            database.PlatformAuditLogs.Add(new PlatformAuditLog
            {
                AssociationId = fixture.SummerId, OperatorUserId = "operator", OccurredAtUtc = DateTimeOffset.UtcNow.AddYears(1),
                Action = PlatformAuditActions.AssociationCreated, OldValuesJson = "{}", NewValuesJson = "{\"Name\":\"FOREIGN-HISTORY\"}"
            });
            await database.SaveChangesAsync();
            var stored = await database.PlatformAuditLogs.AsNoTracking().Where(x => x.AssociationId == fixture.SleepingId)
                .Select(x => new { x.Id, x.OccurredAtUtc }).ToListAsync();
            Assert.All(stored, row =>
            {
                var original = entries.Single(x => x.Id == row.Id).OccurredAtUtc;
                Assert.Equal(original.UtcTicks, row.OccurredAtUtc.UtcTicks);
                // SQLite deliberately normalizes offsets; SQL Server must preserve them.
                Assert.Equal(useSqlite ? TimeSpan.Zero : original.Offset, row.OccurredAtUtc.Offset);
            });
        }
        var before = await fixture.SnapshotAsync();
        var expected = entries.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Select(x => x.Id).ToArray();
        using var client = await fixture.ClientAsync("operator");
        foreach (var (requested, page) in new[] { (int.MinValue, 1), (-1, 1), (0, 1), (1, 1), (2, 2), (3, 3), (int.MaxValue, 3), (1, 1) })
        {
            using var response = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}&historyPage={requested}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.ReadDecodedHtmlAsync();
            var actual = Regex.Matches(html, "data-history-id=\"(\\d+)\"").Select(x => long.Parse(x.Groups[1].Value));
            Assert.Equal(expected.Skip((page - 1) * 20).Take(20), actual);
            Assert.DoesNotContain("FOREIGN-HISTORY", html);
            Assert.Contains("operator", html);
            Assert.Contains("26.09.2026 12:", html);
            var links = Regex.Matches(html, "href=\"([^\"]+#association-history)\"").Select(x => x.Groups[1].Value).ToArray();
            Assert.NotEmpty(links);
            Assert.All(links, link => Assert.Contains($"id={fixture.SleepingId}&historyPage=", link));
            Assert.Contains($"historyPage={(page == 1 ? 2 : page - 1)}#association-history", html);
        }
        foreach (var invalidPage in new[] { "invalid", "2147483648", "-2147483649", "1.5" })
        {
            using var invalid = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}&historyPage={invalidPage}");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Equal(before, await fixture.SnapshotAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OverviewAndSetup_ReportRealAccessAndIsolatedSetupFacts(bool useSqlite)
    {
        await using var fixture = await Fixture.CreateAsync(useSqlite);
        using var client = await fixture.ClientAsync("operator");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        var before = await fixture.SnapshotAsync();
        using var dashboard = await client.GetAsync("/Platform");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var html = await dashboard.ReadDecodedHtmlAsync();
        Assert.Contains("data-total-count=\"3\"", html);
        Assert.Contains("data-active-count=\"2\"", html);
        Assert.Contains("data-without-administrator=\"1\"", html);
        Assert.Contains($"data-attention-id=\"{fixture.SummerId}\"", html);
        Assert.DoesNotContain($"data-attention-id=\"{fixture.SleepingId}\"", html);
        Assert.DoesNotContain("private-personal-value", html);
        using var details = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        var setup = await details.ReadDecodedHtmlAsync();
        Assert.Contains("/sleeping/Account/Login", setup);
        Assert.Contains("data-setup-check=\"Members\" data-complete=\"true\"", setup);
        Assert.Contains("data-setup-check=\"ChargeTypes\" data-complete=\"false\"", setup);
        using var empty = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SummerId}");
        Assert.Contains("data-setup-check=\"Members\" data-complete=\"false\"", await empty.ReadDecodedHtmlAsync());
        Assert.Equal(before, await fixture.SnapshotAsync());

        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sleeping = await db.Associations.SingleAsync(x => x.Id == fixture.SleepingId);
            sleeping.IsActive = true;
            var admin = await db.Users.SingleAsync(x => x.Id == "sleeping-admin");
            admin.MustChangePassword = true;
            await db.SaveChangesAsync();
        }
        using var awaiting = await client.GetAsync("/Platform");
        Assert.Contains("data-awaiting-administrator=\"1\"", await awaiting.ReadDecodedHtmlAsync());
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var admin = await db.Users.SingleAsync(x => x.Id == "sleeping-admin");
            admin.LockoutEnd = DateTimeOffset.UtcNow.AddHours(1);
            await db.SaveChangesAsync();
        }
        using var locked = await client.GetAsync("/Platform");
        var lockedHtml = await locked.ReadDecodedHtmlAsync();
        Assert.Contains("data-without-administrator=\"2\"", lockedHtml);
        Assert.Contains("data-awaiting-administrator=\"0\"", lockedHtml);
    }

    [Fact]
    public async Task AddAdministrator_HttpRequiresCsrfConfirmationAndNewAccountThenForcesPasswordChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        var path = $"/Platform/Associations/AddAdministrator/{fixture.SummerId}";
        var password = "Temporary-Admin-567!";
        var token = await AuthenticationCookieTests.TokenAsync(client, path);
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.UserName"] = "new-summer-admin", ["Input.Email"] = "admin@example.invalid",
            ["Input.TemporaryPassword"] = password, ["Input.ConfirmPassword"] = password,
            ["Input.ConfirmAssignment"] = "true", ["Input.Role"] = RoleNames.PlatformAdministrator
        };
        using var csrf = await client.PostAsync(path, new FormUrlEncodedContent(fields.Where(x => x.Key != "__RequestVerificationToken")));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var unconfirmed = await client.PostAsync(path, new FormUrlEncodedContent(fields.Where(x => x.Key != "Input.ConfirmAssignment")));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Confirm the administrator assignment", await unconfirmed.ReadDecodedHtmlAsync());
        Assert.DoesNotContain(password, await unconfirmed.Content.ReadAsStringAsync());
        using var mismatch = await client.PostAsync(path, new FormUrlEncodedContent(fields.Select(x => x.Key == "Input.ConfirmPassword" ? new KeyValuePair<string, string>(x.Key, "different") : x)));
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);
        using var created = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Found, created.StatusCode);
        Assert.Contains($"id={fixture.SummerId}", created.Headers.Location!.OriginalString);
        using var repeat = await client.PostAsync(path, new FormUrlEncodedContent(fields));
        Assert.Contains("This username is already taken", await repeat.ReadDecodedHtmlAsync());
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(x => x.UserName == "new-summer-admin");
            Assert.True(user.MustChangePassword);
            var membership = Assert.Single(await db.AssociationUserMemberships.IgnoreQueryFilters().Where(x => x.ApplicationUserId == user.Id).ToListAsync());
            Assert.Equal(fixture.SummerId, membership.AssociationId);
            Assert.Equal(RoleNames.Administrator, membership.Role);
            Assert.Empty(await db.UserRoles.Where(x => x.UserId == user.Id).ToListAsync());
            var audit = Assert.Single(await db.PlatformAuditLogs.ToListAsync());
            Assert.Equal(PlatformAuditActions.AdministratorAssigned, audit.Action);
            Assert.DoesNotContain(password, audit.NewValuesJson);
        }
        using var fresh = await fixture.ClientAsync(null);
        var loginToken = await AuthenticationCookieTests.TokenAsync(fresh, "/summer/Account/Login");
        using var login = await fresh.PostAsync("/summer/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = loginToken, ["Input.Login"] = "new-summer-admin", ["Input.Password"] = password
        }));
        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        Assert.Equal("/summer/Account/ChangeInitialPassword", login.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("ru-RU", "История изменений", "Товарищество создано", "Реквизиты изменены", "Товарищество активировано", "Товарищество деактивировано", "Администратор назначен", "Подробности недоступны.")]
    [InlineData("uk-UA", "Історія змін", "Товариство створено", "Реквізити змінено", "Товариство активовано", "Товариство деактивовано", "Адміністратора призначено", "Подробиці недоступні.")]
    [InlineData("en-US", "Change history", "Association created", "Details updated", "Association activated", "Association deactivated", "Administrator assigned", "Details unavailable.")]
    public async Task History_LocalizesKnownDetailsAndNeverRendersRawOrSecretPayloads(
        string culture, string title, string created, string edited, string activated, string deactivated, string assigned, string unavailable)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var scope = fixture.App.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payloads = new[]
            {
                (PlatformAuditActions.AssociationCreated, "{}", "{\"Name\":\"Created garden\",\"Slug\":\"created-garden\",\"AdministratorUserName\":\"new-admin\",\"Role\":\"Administrator\",\"Password\":\"SECRET-PASSWORD\",\"Token\":\"SECRET-TOKEN\"}"),
                (PlatformAuditActions.AssociationEdited, "{\"Name\":\"Old garden\",\"ContactPhone\":\"123\",\"PasswordHash\":\"SECRET-HASH\"}", "{\"Name\":\"<script>alert(1)</script>\",\"ContactPhone\":null,\"ContactEmail\":\"mail@example.invalid\",\"PostalAddress\":\"Garden road\",\"SecurityStamp\":\"SECRET-STAMP\"}"),
                (PlatformAuditActions.AssociationActivated, "{\"IsActive\":false}", "{\"IsActive\":true}"),
                (PlatformAuditActions.AssociationDeactivated, "{\"IsActive\":true}", "{\"IsActive\":false}"),
                (PlatformAuditActions.AdministratorAssigned, "{}", "{\"AdministratorUserId\":\"historical-admin-id\",\"Role\":\"Administrator\"}"),
                (PlatformAuditActions.AssociationEdited, "{}", "broken SECRET-JSON"),
                (PlatformAuditActions.AssociationEdited, "{}", "{\"Name\":{\"Token\":\"SECRET-NESTED\"}}"),
                ("SECRET-UNKNOWN-ACTION", "{}", "{\"Name\":\"SECRET-UNKNOWN-PAYLOAD\"}")
            };
            database.PlatformAuditLogs.AddRange(payloads.Select(x => new PlatformAuditLog
            {
                AssociationId = fixture.SleepingId, OperatorUserId = "operator", OccurredAtUtc = DateTimeOffset.UtcNow,
                Action = x.Item1, OldValuesJson = x.Item2, NewValuesJson = x.Item3
            }));
            await database.SaveChangesAsync();
        }
        using var client = await fixture.ClientAsync("operator");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var response = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SleepingId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawHtml = await response.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(rawHtml);
        foreach (var text in new[] { title, created, edited, activated, deactivated, assigned, unavailable,
            "Created garden", "created-garden", "new-admin", "Old garden", "123", "mail@example.invalid", "Garden road", "historical-admin-id" })
            Assert.Contains(text, html);
        Assert.Contains("&lt;script&gt;", rawHtml);
        Assert.DoesNotContain("<script>alert(1)</script>", rawHtml);
        Assert.DoesNotContain("SECRET-", html);
        Assert.DoesNotContain("OldValuesJson", html);
        Assert.DoesNotContain("PasswordHash", html);
    }

    [Theory]
    [InlineData("ru-RU", "История изменений товарищества пока пуста.")]
    [InlineData("uk-UA", "Історія змін товариства поки порожня.")]
    [InlineData("en-US", "This association has no change history yet.")]
    public async Task History_EmptyStateIsLocalized(string culture, string message)
    {
        await using var fixture = await Fixture.CreateAsync();
        using var client = await fixture.ClientAsync("operator");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var response = await client.GetAsync($"/Platform/Associations/Details?id={fixture.SummerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.ReadDecodedHtmlAsync();
        Assert.Contains(message, html);
        Assert.DoesNotContain("data-history-id=", html);
    }

    private sealed class Fixture(bool useSqlite) : IAsyncDisposable
    {
        private readonly PortalWebApplicationFactory factory = new(useSqlite: useSqlite);
        public WebApplicationFactory<Program> App { get; private set; } = null!;
        public int SleepingId { get; private set; }
        public int SummerId { get; private set; }

        public static async Task<Fixture> CreateAsync(bool useSqlite = true)
        {
            var fixture = new Fixture(useSqlite);
            fixture.App = AuthenticationCookieTests.CreateCookieApplication(fixture.factory);
            await using (var scope = fixture.App.Services.CreateAsyncScope())
            {
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                if (!useSqlite) await database.Database.MigrateAsync();
                var sleeping = new Association { Name = "Спляче товариство", Slug = "sleeping" };
                var summer = new Association { Name = "Літній сад", Slug = "summer" };
                database.Associations.AddRange(sleeping, summer);
                await database.SaveChangesAsync();
                fixture.SleepingId = sleeping.Id;
                fixture.SummerId = summer.Id;
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var name in new[] { "operator", "member", "accountant", "tenant-admin", "neftyanik-admin", "sleeping-admin", "blocked-admin", "former-admin" })
                {
                    var user = new ApplicationUser
                    {
                        Id = name, UserName = name, FirstName = "private-personal-value", LastName = "private-personal-value",
                        Email = name + "@private-personal-value.invalid", IsActive = name != "blocked-admin",
                        LockoutEnabled = true
                    };
                    Assert.True((await users.CreateAsync(user)).Succeeded);
                    if (name == "blocked-admin")
                    {
                        Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(1))).Succeeded);
                    }
                }
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole(RoleNames.PlatformAdministrator))).Succeeded);
                Assert.True((await users.AddToRoleAsync((await users.FindByNameAsync("operator"))!, RoleNames.PlatformAdministrator)).Succeeded);
            }
            foreach (var slug in new[] { "neftyanik", "sleeping" })
            {
                await using var scope = fixture.App.Services.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var association = await database.Associations.SingleAsync(x => x.Slug == slug);
                scope.ServiceProvider.GetRequiredService<AssociationContext>().Resolve(association);
                var localAdmin = slug == "neftyanik" ? "neftyanik-admin" : "sleeping-admin";
                database.AssociationUserMemberships.AddRange(
                    new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Administrator },
                    new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Member });
                if (slug == "neftyanik")
                {
                    database.AssociationUserMemberships.AddRange(
                        new AssociationUserMembership { ApplicationUserId = "member", Role = RoleNames.Member },
                        new AssociationUserMembership { ApplicationUserId = "accountant", Role = RoleNames.Accountant },
                        new AssociationUserMembership { ApplicationUserId = "tenant-admin", Role = RoleNames.Administrator },
                        new AssociationUserMembership { ApplicationUserId = localAdmin, Role = RoleNames.Accountant });
                }
                else
                {
                    database.AssociationUserMemberships.AddRange(
                        new AssociationUserMembership { ApplicationUserId = "blocked-admin", Role = RoleNames.Administrator },
                        new AssociationUserMembership { ApplicationUserId = "former-admin", Role = RoleNames.Administrator, IsActive = false });
                    database.Members.AddRange(
                        new Member { FullName = "private-personal-value", ApplicationUserId = localAdmin },
                        new Member { FullName = "private-personal-value" },
                        new Member { FullName = "private-personal-value", IsActive = false });
                    association.IsActive = false;
                }
                await database.SaveChangesAsync();
            }
            return fixture;
        }

        public async Task<DefaultHttpContext> ContextAsync(IServiceProvider services, string user)
        {
            var signIn = services.GetRequiredService<SignInManager<ApplicationUser>>();
            var principal = await signIn.CreateUserPrincipalAsync((await signIn.UserManager.FindByNameAsync(user))!);
            var context = new DefaultHttpContext { RequestServices = services, User = principal };
            context.Request.Path = "/Platform/Associations";
            return context;
        }

        public async Task<HttpClient> ClientAsync(string? user, bool forgePlatformClaim = false)
        {
            var cookies = new CookieContainer();
            if (user is not null)
            {
                await using var scope = App.Services.CreateAsyncScope();
                var context = await ContextAsync(scope.ServiceProvider, user);
                if (forgePlatformClaim)
                {
                    ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, RoleNames.PlatformAdministrator));
                }
                var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
                var ticket = new AuthenticationTicket(context.User, new AuthenticationProperties
                {
                    IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                }, IdentityConstants.ApplicationScheme);
                cookies.Add(new Uri("https://localhost"), new Cookie(options.Cookie.Name!, options.TicketDataFormat.Protect(ticket), "/") { Secure = true, HttpOnly = true });
            }
            return AuthenticationCookieTests.CreateBrowser(App, cookies);
        }

        public async Task<string> SnapshotAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var snapshot = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                Associations = await database.Associations.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                Users = await database.Users.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                Memberships = await database.AssociationUserMemberships.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id)
                    .Select(x => new { x.Id, x.AssociationId, x.ApplicationUserId, x.Role, x.IsActive, x.CreatedAtUtc }).ToListAsync(),
                Roles = await database.UserRoles.AsNoTracking().OrderBy(x => x.UserId).ThenBy(x => x.RoleId).ToListAsync(),
                Bootstrap = await database.PlatformBootstrapStates.AsNoTracking().ToListAsync(),
                History = await database.PlatformAuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
            });
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(snapshot));
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            await factory.DisposeAsync();
        }
    }
}
