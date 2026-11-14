using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Extatic.Api.Notifications;

public class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken ct = default)
    {
        var config = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromDisplayName, config.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.To));
        if (!string.IsNullOrEmpty(email.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        message.Subject = email.Subject;
        var body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody };
        message.Body = body.ToMessageBody();

        var smtp = config.Smtp;
        var host = smtp.Host ?? throw new InvalidOperationException("Email:Smtp:Host is not configured");
        var socketOptions = smtp.UseStartTls
            ? SecureSocketOptions.StartTls
            : smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None;

        using var client = new SmtpClient();
        await client.ConnectAsync(host, smtp.Port, socketOptions, ct);
        if (!string.IsNullOrEmpty(smtp.Username))
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, ct);
        await client.SendAsync(new SendRequest(message), ct);
        await client.DisconnectAsync(true, ct);
    }
}
