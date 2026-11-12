using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Llm;

// config-এর provider গুলো থেকে (usable এমন) instance বানায়, নাম দিয়ে resolve করে।
// model override দিলে (ollama model-picker) ওই provider-এর config clone করে নতুন instance —
// provider instance stateless/সস্তা, তাই per-request বানানো নিরাপদ; IChatProvider interface অক্ষত।
public class ChatProviderResolver : IChatProviderResolver
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Dictionary<string, IChatProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProviderConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultProvider;
    private readonly IChatProvider? _fallbackChain;

    public ChatProviderResolver(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmSettings> options,
        IOptions<FallbackSettings> fallbackOptions,
        ILogger<FallbackChatProvider> fallbackLogger)
    {
        _httpClientFactory = httpClientFactory;
        var settings = options.Value;
        _defaultProvider = settings.DefaultProvider;

        foreach (var (name, config) in settings.Providers)
        {
            // hosted provider key ছাড়া unusable — skip। local (RequiresApiKey=false) key ছাড়াই চলে।
            if (config.RequiresApiKey && string.IsNullOrWhiteSpace(config.ApiKey))
                continue;

            _configs[name] = config;
            _providers[name] = Build(name, config);
        }

        _fallbackChain = BuildFallbackChain(fallbackOptions.Value, fallbackLogger);
    }

    // chain-এর যেসব provider usable (key আছে) কেবল তাদের নিয়ে। একটাও না থাকলে null →
    // fallback নীরবে বন্ধ, স্বাভাবিক resolution চলে।
    private IChatProvider? BuildFallbackChain(FallbackSettings settings, ILogger<FallbackChatProvider> logger)
    {
        if (!settings.Enabled)
            return null;

        var chain = settings.Chain
            .Select(name => _providers.GetValueOrDefault(name))
            .Where(provider => provider is not null)
            .Select(provider => provider!)
            .ToList();

        return chain.Count > 1 ? new FallbackChatProvider(chain, logger) : null;
    }

    public IChatProvider? Resolve(string? providerName, string? model = null)
    {
        // caller provider বলেনি → fallback chain (থাকলে)। explicit নাম দিলে chain bypass —
        // "ollama দিয়ে চালাও" বললে সত্যিই ollama-তেই যাওয়া উচিত।
        if (string.IsNullOrWhiteSpace(providerName) && string.IsNullOrWhiteSpace(model) && _fallbackChain is not null)
            return _fallbackChain;

        var name = string.IsNullOrWhiteSpace(providerName) ? _defaultProvider : providerName;

        // model override নেই → configured default-model instance (cached)।
        if (string.IsNullOrWhiteSpace(model))
            return _providers.GetValueOrDefault(name);

        // override আছে → ওই provider-এর config clone করে ওই model দিয়ে fresh instance।
        if (!_configs.TryGetValue(name, out var config))
            return null;

        var overridden = new ProviderConfig
        {
            Type = config.Type,
            BaseUrl = config.BaseUrl,
            Model = model,
            ApiKey = config.ApiKey,
            RequiresApiKey = config.RequiresApiKey
        };
        return Build(name, overridden);
    }

    private IChatProvider Build(string name, ProviderConfig config)
        => config.Type.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            ? new GeminiChatProvider(_httpClientFactory, name, config)
            : new OpenAiCompatibleChatProvider(_httpClientFactory, name, config);
}
