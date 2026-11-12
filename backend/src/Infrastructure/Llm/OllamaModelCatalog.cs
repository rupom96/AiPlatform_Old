using System.Net.Http.Json;
using System.Text.Json;
using AiPlatform.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiPlatform.Infrastructure.Llm;

// Ollama /api/tags থেকে installed model তালিকা। ollama configure না থাকলে বা down থাকলে
// খালি তালিকা (dev convenience — throw করে না, UI শুধু কম option দেখায়)।
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

    // "llm" HttpClient-এর timeout ৫ মিনিট (ধীর local model generate করার জন্য)। কিন্তু এটা
    // শুধু একটা তালিকা — host অফ থাকলে dropdown ৫ মিনিট ঝুলে থাকা অর্থহীন। তাই আলাদা, ছোট বাজেট।
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(5);

    public async Task<IReadOnlyList<string>> ListModelsAsync(string? provider, CancellationToken cancellationToken)
    {
        var providerName = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider;

        // chat BaseUrl (…:11434/v1/chat/completions) থেকে authority নিয়ে /api/tags বানাই
        if (!_settings.Providers.TryGetValue(providerName, out var ollama) || string.IsNullOrWhiteSpace(ollama.BaseUrl))
            return [];

        Uri tagsUri;
        try
        {
            var authority = new Uri(ollama.BaseUrl).GetLeftPart(UriPartial.Authority);
            tagsUri = new Uri($"{authority}/api/tags");
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
            using var response = await http.GetAsync(tagsUri, ct);
            if (!response.IsSuccessStatusCode)
                return [];

            var raw = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return [];

            var names = models.EnumerateArray()
                .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToList();

            // chat model-ই রাখি — embedding model (nomic, bge-m3…) chat dropdown-এ আসবে না।
            // capability /api/show থেকে (নাম-heuristic অনির্ভরযোগ্য — bge-m3-এ "embed" নেই)।
            var authority = new Uri(ollama.BaseUrl).GetLeftPart(UriPartial.Authority);
            var checks = names.Select(async name =>
                (name, isChat: await IsChatModelAsync(http, authority, name, ct)));
            var results = await Task.WhenAll(checks);
            return results.Where(r => r.isChat).Select(r => r.name).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // ollama down / timeout / বাজে response — খালি তালিকা, feature ভাঙে না
            logger.LogWarning(ex, "Ollama model list fetch failed ({Provider}, {Uri})", providerName, tagsUri);
            return [];
        }
    }

    // /api/show → capabilities-এ "completion" থাকলে chat model। fail হলে true (valid chat model
    // লুকানোর চেয়ে মাঝে মাঝে embed model দেখানো কম ক্ষতিকর — বিরল)।
    private async Task<bool> IsChatModelAsync(HttpClient http, string authority, string model, CancellationToken cancellationToken)
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
