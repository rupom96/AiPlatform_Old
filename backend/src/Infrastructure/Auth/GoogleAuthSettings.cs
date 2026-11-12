namespace AiPlatform.Infrastructure.Auth;

// appsettings "Authentication:Google" section। ClientId secret নয় (frontend-এও যায়),
// তবু env/user-secrets-এ রাখা সুবিধাজনক — machine-প্রতি আলাদা হয়।
public class GoogleAuthSettings
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; init; } = string.Empty;
}
