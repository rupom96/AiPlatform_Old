namespace AiPlatform.Infrastructure.Llm;

// appsettings "Llm" section। ApiKey গুলো user-secrets/env থেকে (appsettings-এ নয়)।
public class LlmSettings
{
    public const string SectionName = "Llm";

    public string DefaultProvider { get; init; } = "gemini";
    public Dictionary<string, ProviderConfig> Providers { get; init; } = new();
    public EmbeddingConfig Embedding { get; init; } = new();

    // model → দাম (USD/মিলিয়ন token)। unknown model = 0 cost।
    public Dictionary<string, ModelPricing> Pricing { get; init; } = new();
}

public class ModelPricing
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
}

public class ProviderConfig
{
    public string Type { get; init; } = "openai";   // "gemini" | "openai"
    public string? BaseUrl { get; init; }            // openai-compatible-এর endpoint
    public string Model { get; init; } = string.Empty;
    public string? ApiKey { get; init; }             // secrets থেকে

    // local provider (Ollama) key ছাড়াই চলে — false দিলে resolver key না থাকলেও skip করে না।
    // default true = hosted provider (key না থাকলে unusable, skip)।
    public bool RequiresApiKey { get; init; } = true;
}

public class EmbeddingConfig
{
    // "gemini" (hosted, default) | "ollama" (local, key ছাড়া)। embedding = deploy-level সিদ্ধান্ত
    // (per-request না) — কারণ vector space collection-এর সাথে বাঁধা, চললে collection isolate লাগে।
    public string Provider { get; init; } = "gemini";
    public string Model { get; init; } = "gemini-embedding-001";
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }   // ollama: http://localhost:11434 (native /api/embed)
}
