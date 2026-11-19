using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Llm;

// Local model server-এর installed/served model তালিকা (UI model-picker)। configure না থাকলে বা down থাকলে
// খালি তালিকা (dev convenience — throw করে না, UI শুধু কম option দেখায়)।
//   Engine "ollama" → /api/tags (+ chat-capable ফিল্টার)   Engine "vllm" → OpenAI-style /v1/models
public class OllamaModelCatalog(
    IHttpClientFactory httpClientFactory,
    IOptions<LlmSettings> options,
    ILogger<OllamaModelCatalog> logger)
    : ILocalModelCatalog
{
    private readonly LlmSettings _settings = options.Value;

    // configure করা নামই কেবল গ্রহণ করি — caller-এর পাঠানো নাম সরাসরি URL হয় না, তাই
    // ইচ্ছেমতো host ডাকানো (SSRF) সম্ভব নয়। অচেনা নাম = খালি তালিকা।
    private const string DefaultProvider = "ollama";
    private const string VllmEngine = "vllm";

    // "llm" HttpClient-এর timeout ৫ মিনিট (ধীর local model generate করার জন্য)। কিন্তু এটা
    // শুধু একটা তালিকা — host অফ থাকলে dropdown ৫ মিনিট ঝুলে থাকা অর্থহীন। তাই আলাদা, ছোট বাজেট।
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<string>> ListModelsAsync(string? provider, CancellationToken cancellationToken)
    {
        var providerName = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider;

        if (!_settings.Providers.TryGetValue(providerName, out var config) || string.IsNullOrWhiteSpace(config.BaseUrl))
            return [];

        // chat BaseUrl (…:11434/v1/chat/completions) থেকে authority নিয়ে listing endpoint বানাই
        string authority;
        try
        {
            authority = new Uri(config.BaseUrl).GetLeftPart(UriPartial.Authority);
        }
        catch (UriFormatException)
        {
            return [];
        }

        // request বাতিল হলেও বাতিল, ৫ সেকেন্ড পেরোলেও — যেটা আগে আসে
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ListTimeout);
        var ct = timeout.Token;

        try
        {
            var http = httpClientFactory.CreateClient("llm");
            return config.Engine.Equals(VllmEngine, StringComparison.OrdinalIgnoreCase)
                ? await ListVllmModelsAsync(http, authority, ct)
                : await ListOllamaModelsAsync(http, authority, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // server down / timeout / বাজে response — খালি তালিকা, feature ভাঙে না
            logger.LogWarning(ex, "Local model list fetch failed ({Provider}, {Authority})", providerName, authority);
            return [];
        }
    }

    private static async Task<IReadOnlyList<string>> ListOllamaModelsAsync(
        HttpClient http, string authority, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{authority}/api/tags", ct);
        if (!response.IsSuccessStatusCode)
            return [];

        var raw = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        var names = ReadNames(doc.RootElement, "models", "name");

        // chat model-ই রাখি — embedding model (nomic, bge-m3…) chat dropdown-এ আসবে না।
        // capability /api/show থেকে (নাম-heuristic অনির্ভরযোগ্য — bge-m3-এ "embed" নেই)।
        var checks = names.Select(async name => (name, isChat: await IsChatModelAsync(http, authority, name, ct)));
        var results = await Task.WhenAll(checks);
        return results.Where(r => r.isChat).Select(r => r.name).ToList();
    }

    // vLLM: `--served-model-name` যা দিয়েছ সেটাই id; সাধারণত একটাই model চলে। embedding ফিল্টার লাগে না
    // (vLLM প্রতি instance-এ একটা কাজ করে)।
    private static async Task<IReadOnlyList<string>> ListVllmModelsAsync(
        HttpClient http, string authority, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{authority}/v1/models", ct);
        if (!response.IsSuccessStatusCode)
            return [];

        var raw = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        return ReadNames(doc.RootElement, "data", "id");
    }

    private static List<string> ReadNames(JsonElement root, string arrayProperty, string nameProperty)
    {
        if (!root.TryGetProperty(arrayProperty, out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        return items.EnumerateArray()
            .Select(item => item.TryGetProperty(nameProperty, out var n) ? n.GetString() : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
    }

    // /api/show → capabilities-এ "completion" থাকলে chat model। fail হলে true (valid chat model
    // লুকানোর চেয়ে মাঝে মাঝে embed model দেখানো কম ক্ষতিকর — বিরল)।
    private static async Task<bool> IsChatModelAsync(HttpClient http, string authority, string model, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{authority}/api/show")
            {
                Content = JsonContent.Create(new { model })
            };
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return true;

            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("capabilities", out var caps) || caps.ValueKind != JsonValueKind.Array)
                return true;

            return caps.EnumerateArray().Any(c => c.GetString() == "completion");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return true;
        }
    }
}
