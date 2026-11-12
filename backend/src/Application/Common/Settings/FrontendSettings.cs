namespace AiPlatform.Application.Common.Settings;

// appsettings "Frontend" section — email-এ reset link বানাতে SPA-র base URL লাগে।
public class FrontendSettings
{
    public const string SectionName = "Frontend";

    public string BaseUrl { get; init; } = "http://localhost:5173";
}
