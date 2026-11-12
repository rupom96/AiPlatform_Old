namespace AiPlatform.Api.Common;

// appsettings "Otel" section। Endpoint ইচ্ছে করে এখানে নেই — OTLP-র standard env var
// `OTEL_EXPORTER_OTLP_ENDPOINT` exporter নিজেই পড়ে (default http://localhost:4317)।
public class OtelSettings
{
    public const string SectionName = "Otel";

    public bool Enabled { get; init; }
    public string ServiceName { get; init; } = "AiPlatform.Api";
}
