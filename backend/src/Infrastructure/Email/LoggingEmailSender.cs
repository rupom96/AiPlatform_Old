using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Infrastructure.Email;

// Dev impl — email না পাঠিয়ে log করে (reset token dev console-এ দেখা যায়)।
// ponytail: real SMTP/provider পরে, যখন সত্যি email পাঠানো দরকার।
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        logger.LogInformation("EMAIL (dev, not sent) → {To} | {Subject}\n{Body}", to, subject, body);
        return Task.CompletedTask;
    }
}
