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

    // local model server-এর ধরন — UI model-picker তালিকা কোন endpoint থেকে আনবে তা ঠিক করে:
    // "ollama" (default, /api/tags) | "vllm" (OpenAI-style /v1/models)। chat call দুটোতেই একই।
    public string Engine { get; init; } = "ollama";

    // optional sampling temperature। null = request-এ পাঠানো হয় না, তখন server-এর নিজস্ব default চলে
    // (vLLM model-এর generation_config.json থেকে — Gemma-র ক্ষেত্রে 1.0, তাই উত্তর/tool-call এলোমেলো হতে পারে)।
    public double? Temperature { get; init; }

    // optional: "none" | "low" | "medium" | "high"। thinking-capable local model (gemma4, qwen3)
    // default-এ "ভাবে" → ধীর। "none" দিলে thinking বন্ধ। null = request-এ পাঠানোই হয় না (hosted-এ অক্ষত)।
    public string? ReasoningEffort { get; init; }
}

public class EmbeddingConfig
{
    // "gemini" (hosted, default) | "ollama" (local, key ছাড়া)। embedding = deploy-level সিদ্ধান্ত
    // (per-request না) — কারণ vector space collection-এর সাথে বাঁধা, চললে collection isolate লাগে।
    public string Provider { get; init; } = "gemini";
    public string Model { get; init; } = "gemini-embedding-001";
    public string? ApiKey { get; init; }
    public string? BaseUrl { get; init; }   // ollama: http://localhost:11434 (native /api/embed)

    // ollama: খোঁজার প্রশ্নের (document-এর নয়) কাজের বর্ণনা — Qwen3-embedding ছাঁচে "Instruct: {task}\nQuery: {প্রশ্ন}"
    // হয়ে যায়। এটা ছাড়া ২-৩ শব্দের ছোট query ("অফিসের সময়") ভুল chunk টানে। খালি = কিছু বসে না
    // (nomic-embed-text ইত্যাদির আগের আচরণ)। উদাহরণ: "Given a user question, retrieve passages from company documents that answer it"
    public string? QueryTask { get; init; }
}
