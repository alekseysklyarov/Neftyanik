using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Neftyanik.Portal.Application.Identity;

namespace Neftyanik.Portal.Infrastructure.Identity;

public sealed class PlatformSmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public sealed class SmtpPlatformEmailSender(IOptions<PlatformSmtpOptions> options) : IPlatformEmailSender
{
    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.UserName)
            || string.IsNullOrWhiteSpace(settings.Password) || string.IsNullOrWhiteSpace(settings.From)
            || settings.Port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Platform SMTP delivery is not configured.");
        }
        using var message = new MailMessage(settings.From, recipient, subject, body);
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(settings.UserName, settings.Password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await client.SendMailAsync(message, timeout.Token);
    }
}
