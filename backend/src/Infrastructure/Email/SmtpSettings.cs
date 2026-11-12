namespace AiPlatform.Infrastructure.Email;

// appsettings "Smtp" section। Host খালি = SMTP নেই → LoggingEmailSender fallback (DI-তে ঠিক হয়)।
// Password কখনো appsettings-এ নয় — user-secrets/env (যেমন Gmail app password)।
public class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;   // STARTTLS
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = "AiPlatform";
}
