namespace AiPlatform.Application.Common.Interfaces;

// Email পাঠানোর abstraction। dev-এ log করা impl; পরে SMTP/provider।
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}
