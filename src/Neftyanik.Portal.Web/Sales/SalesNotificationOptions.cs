using System.Net.Mail;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Infrastructure.Identity;

namespace Neftyanik.Portal.Web.Sales;

public sealed class SalesNotificationOptions
{
    public bool Enabled { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string InboxUrl { get; set; } = string.Empty;
}

public sealed class SalesNotificationSettings(
    IOptions<SalesNotificationOptions> notifications,
    IOptions<PlatformSmtpOptions> smtp)
{
    public SalesNotificationOptions Options => notifications.Value;
    public bool IsReady => Options.Enabled
        && MailAddress.TryCreate(Options.Recipient, out _)
        && MailAddress.TryCreate(smtp.Value.From, out _)
        && !string.IsNullOrWhiteSpace(smtp.Value.Host)
        && !string.IsNullOrWhiteSpace(smtp.Value.UserName)
        && !string.IsNullOrWhiteSpace(smtp.Value.Password)
        && smtp.Value.Port is >= 1 and <= 65535;
}
