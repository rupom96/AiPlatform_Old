namespace AiPlatform.Infrastructure.Guardrails;

// appsettings "Guardrails"। injection pattern config-driven (evolve করে); PII regex code-এ fixed।
public class GuardrailsSettings
{
    public const string SectionName = "Guardrails";

    public bool Enabled { get; init; }

    // PII (email/phone/credit-card) input থেকে redact করবে কি না।
    public bool RedactPii { get; init; } = true;

    // এই regex-গুলোর যেকোনোটা match করলে input block (prompt-injection signal)।
    public List<string> InjectionPatterns { get; init; } = [];
}
