#if WEB_TESTS
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Domain.Constants;
using Neftyanik.Portal.Domain.Entities;
using Neftyanik.Portal.Domain.Enums;
using Neftyanik.Portal.Infrastructure.Data;
using Xunit;

namespace Neftyanik.Portal.Web.Tests;

public sealed class PaymentNotificationUiTests
{
    [Fact]
    public async Task GetMemberDashboard_ForLinkedMember_ShowsNotifyPaymentButton_AndOnlyOwnNotifications()
    {
        using var factory = new PortalWebApplicationFactory();
        const string memberUserId = "member-user";
        const string otherUserId = "other-user";

        await SeedUserAsync(factory, "admin-user", "admin@example.com");
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");
        await SeedMemberAsync(factory, 2, otherUserId, "Member Two");
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.PaymentNotifications.AddRange(
                new PaymentNotification
                {
                    MemberId = 1,
                    Amount = 100m,
                    PaymentMethod = PaymentMethod.Card,
                    Description = "Мое уведомление",
                    Status = PaymentNotificationStatus.Rejected,
                    AdministratorComment = "Нужна квитанция",
                    ReviewedAtUtc = DateTimeOffset.UtcNow,
                    ReviewedByUserId = "admin-user",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
                },
                new PaymentNotification
                {
                    MemberId = 2,
                    Amount = 200m,
                    PaymentMethod = PaymentMethod.Cash,
                    Description = "Чужое уведомление",
                    Status = PaymentNotificationStatus.Pending,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), cultureName: "ru-RU");
        var response = await client.GetAsync("/Member");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Уведомить о платеже", html, StringComparison.Ordinal);
        Assert.Contains("Мое уведомление", html, StringComparison.Ordinal);
        Assert.Contains("Нужна квитанция", html, StringComparison.Ordinal);
        Assert.Contains("Отклонено", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Чужое уведомление", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Pending<", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Rejected<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostMemberPaymentNotification_ValidRequest_CreatesPendingNotificationWithoutPayment()
    {
        using var factory = new PortalWebApplicationFactory();
        const string memberUserId = "member-user";
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");
        var token = await GetAntiforgeryTokenAsync(client, "/Member");

        var response = await client.PostAsync(
            "/Member?handler=CreatePaymentNotification",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["PaymentNotification.Amount"] = "123,45",
                ["PaymentNotification.PaymentMethod"] = "Card",
                ["PaymentNotification.Description"] = "Оплата через банк",
                ["ChargePage"] = "1",
                ["PaymentPage"] = "1"
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Member?chargePage=1&paymentPage=1", response.Headers.Location?.OriginalString);

        var redirectedResponse = await client.GetAsync(response.Headers.Location);
        var redirectedHtml = await redirectedResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, redirectedResponse.StatusCode);
        Assert.Contains("Уведомление о платеже отправлено. Платеж ожидает подтверждения бухгалтером.", redirectedHtml, StringComparison.Ordinal);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var notifications = await dbContext.PaymentNotifications.ToListAsync();
            var payments = await dbContext.Payments.ToListAsync();

            var notification = Assert.Single(notifications);
            Assert.Equal(PaymentNotificationStatus.Pending, notification.Status);
            Assert.Equal(123.45m, notification.Amount);
            Assert.Equal(PaymentMethod.Card, notification.PaymentMethod);
            Assert.Equal("Оплата через банк", notification.Description);
            Assert.Null(notification.PaymentId);
            Assert.Empty(payments);
        });

        var refreshResponse = await client.GetAsync("/Member?chargePage=1&paymentPage=1");
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            Assert.Single(await dbContext.PaymentNotifications.ToListAsync());
        });
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task PostMemberPaymentNotification_InvalidAmount_DoesNotCreateNotification(string amount)
    {
        using var factory = new PortalWebApplicationFactory();
        const string memberUserId = "member-user";
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), cultureName: "ru-RU");
        var token = await GetAntiforgeryTokenAsync(client, "/Member");

        var response = await client.PostAsync(
            "/Member?handler=CreatePaymentNotification",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["PaymentNotification.Amount"] = amount,
                ["PaymentNotification.PaymentMethod"] = "Cash",
                ["ChargePage"] = "1",
                ["PaymentPage"] = "1"
            }));

        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Сумма платежа должна быть больше нуля", html, StringComparison.Ordinal);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            Assert.Empty(await dbContext.PaymentNotifications.ToListAsync());
            Assert.Empty(await dbContext.Payments.ToListAsync());
        });
    }

    [Fact]
    public async Task PostMemberPaymentNotification_InvalidPaymentMethod_IsRejected()
    {
        using var factory = new PortalWebApplicationFactory();
        const string memberUserId = "member-user";
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), cultureName: "ru-RU");
        var token = await GetAntiforgeryTokenAsync(client, "/Member");

        var response = await client.PostAsync(
            "/Member?handler=CreatePaymentNotification",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["PaymentNotification.Amount"] = "10",
                ["PaymentNotification.PaymentMethod"] = "InvalidMethod",
                ["ChargePage"] = "1",
                ["PaymentPage"] = "1"
            }));

        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Выберите способ оплаты", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostMemberPaymentNotification_ForAnonymousUser_DoesNotCreateNotification()
    {
        using var factory = new PortalWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false, cultureName: "ru-RU");

        var response = await client.PostAsync(
            "/Member?handler=CreatePaymentNotification",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["PaymentNotification.Amount"] = "10",
                ["PaymentNotification.PaymentMethod"] = "Cash"
            }));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            Assert.Empty(await dbContext.PaymentNotifications.ToListAsync());
        });
    }

    [Fact]
    public async Task GetAdministrationLayout_ForAdministrator_ShowsBellAndPendingCount()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberAsync(factory, 1, "member-user", "Member One");
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.PaymentNotifications.AddRange(
                new PaymentNotification { MemberId = 1, Amount = 10m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Pending },
                new PaymentNotification { MemberId = 1, Amount = 20m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Pending },
                new PaymentNotification { MemberId = 1, Amount = 30m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Confirmed, ReviewedAtUtc = DateTimeOffset.UtcNow, ReviewedByUserId = adminUserId });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");
        var response = await client.GetAsync("/Administration");
        var html = await response.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"payment-notification-bell\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"payment-notification-bell-count\"", html, StringComparison.Ordinal);
        Assert.Contains(">2<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationLayout_WhenNoPendingNotifications_HidesBellBadge_AndMemberDoesNotSeeBell()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const string memberUserId = "member-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");

        using var adminClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), cultureName: "ru-RU");
        var adminHtml = await (await adminClient.GetAsync("/Administration")).ReadDecodedHtmlAsync();
        Assert.Contains("id=\"payment-notification-bell\"", adminHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"payment-notification-bell-count\"", adminHtml, StringComparison.Ordinal);

        using var memberClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), cultureName: "ru-RU");
        var memberHtml = await (await memberClient.GetAsync("/Member")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain("id=\"payment-notification-bell\"", memberHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationPaymentNotifications_DefaultsToPending_AndMemberCannotAccess()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        const string memberUserId = "member-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberAsync(factory, 1, memberUserId, "Member One");
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.PaymentNotifications.AddRange(
                new PaymentNotification { MemberId = 1, Amount = 10m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Pending, Description = "Pending item", CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) },
                new PaymentNotification { MemberId = 1, Amount = 20m, PaymentMethod = PaymentMethod.Cash, Status = PaymentNotificationStatus.Confirmed, Description = "Confirmed item", CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1), ReviewedAtUtc = DateTimeOffset.UtcNow, ReviewedByUserId = adminUserId });
            await dbContext.SaveChangesAsync();
        });

        using var adminClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");
        var defaultResponse = await adminClient.GetAsync("/Administration/Finance/PaymentNotifications");
        var defaultHtml = await defaultResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, defaultResponse.StatusCode);
        Assert.Contains("Pending item", defaultHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Confirmed item", defaultHtml, StringComparison.Ordinal);

        var filteredResponse = await adminClient.GetAsync("/Administration/Finance/PaymentNotifications?status=Confirmed");
        var filteredHtml = await filteredResponse.ReadDecodedHtmlAsync();
        Assert.Contains("Confirmed item", filteredHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Pending item", filteredHtml, StringComparison.Ordinal);

        using var memberClient = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(memberUserId, RoleNames.Member), allowAutoRedirect: false, cultureName: "ru-RU");
        var memberResponse = await memberClient.GetAsync("/Administration/Finance/PaymentNotifications");
        Assert.Equal(HttpStatusCode.Found, memberResponse.StatusCode);
        Assert.StartsWith("http://localhost/Account/AccessDenied", memberResponse.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAdministrationPaymentNotifications_ReturnsOk_ForEmptyAndOptionalNotificationShapes()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberWithFinanceAsync(factory, 1, "member-with-plot", 101, 200m);
        await SeedMemberAsync(factory, 2, "member-without-plot", "Member Without Plot");

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var emptyResponse = await client.GetAsync("/Administration/Finance/PaymentNotifications");
        var emptyHtml = await emptyResponse.ReadDecodedHtmlAsync();

        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Contains("Для выбранного статуса уведомления отсутствуют.", emptyHtml, StringComparison.Ordinal);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.Payments.Add(new Payment
            {
                Id = 5001,
                PlotId = 101,
                PaymentDate = new DateOnly(2026, 8, 1),
                Amount = 50m,
                PaymentMethod = PaymentMethod.BankTransfer,
                CreatedByUserId = adminUserId,
                CreatedAtUtc = DateTime.UtcNow
            });

            dbContext.PaymentNotifications.AddRange(
                new PaymentNotification
                {
                    Id = 3001,
                    MemberId = 1,
                    Amount = 10m,
                    PaymentMethod = PaymentMethod.Cash,
                    Status = PaymentNotificationStatus.Pending,
                    Description = null,
                    PaymentId = null,
                    ReviewedAtUtc = null,
                    ReviewedByUserId = null,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
                },
                new PaymentNotification
                {
                    Id = 3002,
                    MemberId = 1,
                    Amount = 20m,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    Status = PaymentNotificationStatus.Confirmed,
                    Description = "Confirmed item",
                    PaymentId = 5001,
                    ReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                    ReviewedByUserId = adminUserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
                },
                new PaymentNotification
                {
                    Id = 3003,
                    MemberId = 2,
                    Amount = 30m,
                    PaymentMethod = PaymentMethod.Card,
                    Status = PaymentNotificationStatus.Rejected,
                    Description = "Rejected item",
                    AdministratorComment = "Причина отклонения",
                    ReviewedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                    ReviewedByUserId = adminUserId,
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30)
                });
            await dbContext.SaveChangesAsync();
        });

        var pendingResponse = await client.GetAsync("/Administration/Finance/PaymentNotifications?status=Pending");
        var pendingHtml = await pendingResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        Assert.Contains("Member 1", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("P-101", pendingHtml, StringComparison.Ordinal);
        Assert.Contains("—", pendingHtml, StringComparison.Ordinal);

        var confirmedResponse = await client.GetAsync("/Administration/Finance/PaymentNotifications?status=Confirmed");
        var confirmedHtml = await confirmedResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, confirmedResponse.StatusCode);
        Assert.Contains("Confirmed item", confirmedHtml, StringComparison.Ordinal);
        Assert.Contains("#5001", confirmedHtml, StringComparison.Ordinal);

        var rejectedResponse = await client.GetAsync("/Administration/Finance/PaymentNotifications?status=Rejected");
        var rejectedHtml = await rejectedResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, rejectedResponse.StatusCode);
        Assert.Contains("Member Without Plot", rejectedHtml, StringComparison.Ordinal);
        Assert.Contains("Причина отклонения", rejectedHtml, StringComparison.Ordinal);

        var invalidStatusResponse = await client.GetAsync("/Administration/Finance/PaymentNotifications?status=NotARealStatus");
        var invalidStatusHtml = await invalidStatusResponse.ReadDecodedHtmlAsync();
        Assert.Equal(HttpStatusCode.OK, invalidStatusResponse.StatusCode);
        Assert.Contains("Member 1", invalidStatusHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Confirmed item", invalidStatusHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAdministrationPaymentNotificationConfirm_CreatesPaymentAndProcessedNotificationsHideActions()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberWithFinanceAsync(factory, 1, "member-user", 101, 200m);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.PaymentNotifications.Add(new PaymentNotification
            {
                Id = 1001,
                MemberId = 1,
                Amount = 75m,
                PaymentMethod = PaymentMethod.Card,
                Description = "Подтвердить",
                Status = PaymentNotificationStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");
        var token = await GetAntiforgeryTokenAsync(client, "/Administration/Finance/PaymentNotifications");

        var response = await client.PostAsync(
            "/Administration/Finance/PaymentNotifications?handler=Confirm&status=Pending",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["notificationId"] = "1001"
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var notification = await dbContext.PaymentNotifications.SingleAsync(item => item.Id == 1001);
            var payments = await dbContext.Payments.ToListAsync();
            Assert.Equal(PaymentNotificationStatus.Confirmed, notification.Status);
            Assert.NotNull(notification.PaymentId);
            Assert.Single(payments);
        });

        var processedHtml = await (await client.GetAsync("/Administration/Finance/PaymentNotifications?status=Confirmed")).ReadDecodedHtmlAsync();
        Assert.DoesNotContain("data-bs-target=\"#confirm-notification-1001\"", processedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("data-bs-target=\"#reject-notification-1001\"", processedHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAdministrationPaymentNotificationReject_StoresReason_AndRequiresAntiforgery()
    {
        using var factory = new PortalWebApplicationFactory();
        const string adminUserId = "admin-user";
        await SeedUserAsync(factory, adminUserId, "admin@example.com");
        await SeedMemberWithFinanceAsync(factory, 1, "member-user", 101, 200m);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            dbContext.PaymentNotifications.Add(new PaymentNotification
            {
                Id = 1002,
                MemberId = 1,
                Amount = 40m,
                PaymentMethod = PaymentMethod.Cash,
                Status = PaymentNotificationStatus.Pending,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-1)
            });
            await dbContext.SaveChangesAsync();
        });

        using var client = factory.CreateAuthenticatedClient(new TestAuthenticatedUser(adminUserId, RoleNames.Administrator), allowAutoRedirect: false, cultureName: "ru-RU");

        var badResponse = await client.PostAsync(
            "/Administration/Finance/PaymentNotifications?handler=Reject&status=Pending",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["notificationId"] = "1002",
                ["administratorComment"] = "Без токена"
            }));
        Assert.Equal(HttpStatusCode.BadRequest, badResponse.StatusCode);

        var token = await GetAntiforgeryTokenAsync(client, "/Administration/Finance/PaymentNotifications");
        var response = await client.PostAsync(
            "/Administration/Finance/PaymentNotifications?handler=Reject&status=Pending",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["notificationId"] = "1002",
                ["administratorComment"] = "Неверная сумма"
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var notification = await dbContext.PaymentNotifications.SingleAsync(item => item.Id == 1002);
            Assert.Equal(PaymentNotificationStatus.Rejected, notification.Status);
            Assert.Equal("Неверная сумма", notification.AdministratorComment);
            Assert.Empty(await dbContext.Payments.ToListAsync());
        });
    }

    private static async Task SeedUserAsync(PortalWebApplicationFactory factory, string userId, string email)
    {
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            if (await dbContext.Users.AnyAsync(item => item.Id == userId))
            {
                return;
            }

            dbContext.Users.Add(CreateUser(userId, email));
            await dbContext.SaveChangesAsync();
        });
    }

    private static async Task SeedMemberAsync(PortalWebApplicationFactory factory, int memberId, string userId, string fullName)
    {
        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            if (!await dbContext.Users.AnyAsync(item => item.Id == userId))
            {
                dbContext.Users.Add(CreateUser(userId, $"{userId}@example.com"));
            }

            if (!await dbContext.Members.AnyAsync(item => item.Id == memberId))
            {
                dbContext.Members.Add(new Member
                {
                    Id = memberId,
                    ApplicationUserId = userId,
                    FullName = fullName,
                    IsActive = true
                });
            }

            await dbContext.SaveChangesAsync();
        });
    }

    private static async Task SeedMemberWithFinanceAsync(PortalWebApplicationFactory factory, int memberId, string userId, int plotId, decimal chargeAmount)
    {
        await SeedMemberAsync(factory, memberId, userId, $"Member {memberId}");

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            if (!await dbContext.ChargeTypes.AnyAsync(item => item.Id == 1))
            {
                dbContext.ChargeTypes.Add(new Neftyanik.Portal.Domain.Entities.ChargeType
                {
                    Id = 1,
                    Code = "TEST-CHARGE",
                    Name = "Test charge",
                    IsActive = true
                });
            }

            if (!await dbContext.Plots.AnyAsync(item => item.Id == plotId))
            {
                dbContext.Plots.Add(new Plot
                {
                    Id = plotId,
                    Number = $"P-{plotId}",
                    Address = $"Plot {plotId}",
                    IsActive = true
                });
            }

            if (!await dbContext.PlotOwnerships.AnyAsync(item => item.PlotId == plotId && item.MemberId == memberId))
            {
                dbContext.PlotOwnerships.Add(new PlotOwnership
                {
                    Id = plotId,
                    PlotId = plotId,
                    MemberId = memberId,
                    ValidFrom = new DateOnly(2020, 1, 1),
                    IsPrimaryContact = true
                });
            }

            if (!await dbContext.Charges.AnyAsync(item => item.PlotId == plotId))
            {
                dbContext.Charges.Add(new Charge
                {
                    Id = plotId,
                    PlotId = plotId,
                    ChargeTypeId = 1,
                    Amount = chargeAmount,
                    ChargeDate = new DateOnly(2026, 1, 1),
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync();
        });
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        Assert.True(match.Success, $"Antiforgery token not found in response for {url}.");
        return match.Groups[1].Value;
    }

    private static ApplicationUser CreateUser(string id, string email)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            FirstName = "Test",
            LastName = "User",
            DisplayName = email,
            MustChangePassword = false,
            IsActive = true
        };
    }
}
#endif
