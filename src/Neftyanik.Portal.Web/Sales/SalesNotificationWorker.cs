using Microsoft.EntityFrameworkCore;
using Neftyanik.Portal.Application.Identity;
using Neftyanik.Portal.Infrastructure.Data;

namespace Neftyanik.Portal.Web.Sales;

// The saved request is the durable queue. SMTP failure never affects form submission.
public sealed class SalesNotificationWorker(
    IServiceScopeFactory scopes,
    ILogger<SalesNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                try { await DeliverBatchAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception) { logger.LogWarning("Sales notification delivery is temporarily unavailable; saved requests remain queued."); }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task DeliverBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SalesNotificationSettings>();
        if (!settings.IsReady) return;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IPlatformEmailSender>();
        var now = DateTime.UtcNow;
        var ids = await db.DemoRequests.AsNoTracking()
            .Where(x => x.NotificationSentAtUtc == null && x.ContactedAtUtc == null
                && (x.NotificationNextAttemptAtUtc == null || x.NotificationNextAttemptAtUtc <= now))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => x.Id).Take(5)
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            now = DateTime.UtcNow;
            var leaseUntil = now.AddMinutes(5);
            // Atomic claim prevents multiple app instances sending the same queued item concurrently.
            var claimed = await db.DemoRequests.Where(x => x.Id == id && x.NotificationSentAtUtc == null
                && x.ContactedAtUtc == null
                && (x.NotificationNextAttemptAtUtc == null || x.NotificationNextAttemptAtUtc <= now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(x => x.NotificationNextAttemptAtUtc, leaseUntil)
                    .SetProperty(x => x.NotificationAttemptCount, x => x.NotificationAttemptCount + 1), cancellationToken);
            if (claimed == 0) continue;

            var inbox = Uri.TryCreate(settings.Options.InboxUrl, UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(url.UserInfo)
                ? url.AbsoluteUri : "/Platform/Requests";
            try
            {
                // Personal details stay in the authenticated inbox, not in email notifications.
                await sender.SendAsync(settings.Options.Recipient, $"DachaHub: новая заявка №{id}",
                    $"Поступила новая заявка на подключение товарищества, №{id}.\n\nОткройте кабинет администратора платформы → Заявки на подключение:\n{inbox}\n\nСвяжитесь с клиентом и отметьте заявку как обработанную. Это уведомление не является ответом клиенту.", cancellationToken);
                await db.DemoRequests.Where(x => x.Id == id && x.NotificationNextAttemptAtUtc == leaseUntil)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.NotificationSentAtUtc, DateTime.UtcNow)
                        .SetProperty(x => x.NotificationNextAttemptAtUtc, (DateTime?)null), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                logger.LogWarning("Email notification for sales request {RequestId} was not confirmed; it will be retried.", id);
                await db.DemoRequests.Where(x => x.Id == id && x.NotificationNextAttemptAtUtc == leaseUntil)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.NotificationNextAttemptAtUtc,
                        DateTime.UtcNow.AddMinutes(10)), cancellationToken);
            }
        }
    }
}
