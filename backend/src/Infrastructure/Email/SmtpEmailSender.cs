using AiPlatform.Application.Common.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace AiPlatform.Infrastructure.Email;

// আসল email — MailKit SMTP (Gmail app password / যেকোনো SMTP provider)।
// প্রতি send-এ নতুন connection — volume কম (শুধু password reset), pooling-এর দরকার নেই।
public class SmtpEmailSender(SmtpSettings settings, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromEmail));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, SecureSocketOptions.StartTls, cancellationToken);
            await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            logger.LogInformation("Email sent → {To} | {Subject}", to, subject);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}
