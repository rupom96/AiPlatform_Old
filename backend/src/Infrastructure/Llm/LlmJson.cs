using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;

namespace AiPlatform.Infrastructure.Llm;

// Provider 200 দিয়েও JSON ছাড়া কিছু ফেরত দিতে পারে — GitHub Models 2026-10-29-এ
// chat completion-এর বদলে `200 text/plain "OK"` পাঠাচ্ছিল। সরাসরি JsonDocument.Parse
// করলে JsonReaderException উঠে global handler-এ গিয়ে 500 "An unexpected error occurred"
// হতো, অর্থাৎ **উপরের service-এর দোষ আমাদের bug-এর মতো দেখাত**।
//
// status মিথ্যা বললেও এটা আসল অবস্থা ধরে: parse ব্যর্থ = provider অচল → LlmProviderException
// → 502 + কোন provider ও কী ফেরত দিয়েছে, দুটোই বার্তায়।
internal static class LlmJson
{
    public static JsonDocument Parse(string providerName, string raw, int statusCode)
    {
        try
        {
            return JsonDocument.Parse(raw);
        }
        catch (JsonException)
        {
            var preview = raw.Length <= 200 ? raw : raw[..200] + "…";
            throw new LlmProviderException(providerName, statusCode,
                $"Response was not JSON: {preview}");
        }
    }
}
