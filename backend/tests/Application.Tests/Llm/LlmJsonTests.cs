using System.Text.Json;
using AiPlatform.Application.Common.Exceptions;
using Xunit;

namespace AiPlatform.Application.Tests.Llm;

// Provider 200 দিয়েও JSON ছাড়া কিছু পাঠাতে পারে (GitHub Models: `200 text/plain "OK"`)।
// তখন আমাদের 500 নয়, "ঐ provider অচল" বলা উচিত — নইলে উপরের service-এর দোষ
// আমাদের bug-এর মতো দেখায়।
public class LlmJsonTests
{
    [Fact]
    public void ValidJson_ParsesNormally()
    {
        using var doc = InvokeParse("groq", """{"choices":[{"message":{"content":"hi"}}]}""", 200);

        Assert.Equal("hi", doc.RootElement
            .GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public void NonJsonBody_ThrowsProviderException_NotJsonException()
    {
        var ex = Assert.Throws<LlmProviderException>(() => InvokeParse("github", "OK", 200));

        Assert.Contains("github", ex.Message);
        Assert.Contains("not JSON", ex.Message);
        Assert.Contains("OK", ex.Message);
    }

    [Fact]
    public void LongNonJsonBody_IsTruncatedSoHtmlPagesDoNotFloodTheMessage()
    {
        var html = new string('x', 5000);

        var ex = Assert.Throws<LlmProviderException>(() => InvokeParse("openrouter", html, 200));

        Assert.True(ex.Message.Length < 400, $"message was {ex.Message.Length} chars");
        Assert.Contains("…", ex.Message);
    }

    // LlmJson internal (Infrastructure-এর ভেতরের helper) — test project থেকে reflection-এ ডাকি,
    // শুধু test-এর জন্য public করার চেয়ে এটাই কম ক্ষতিকর।
    private static JsonDocument InvokeParse(string provider, string raw, int statusCode)
    {
        var type = typeof(AiPlatform.Infrastructure.Llm.OpenAiCompatibleChatProvider).Assembly
            .GetType("AiPlatform.Infrastructure.Llm.LlmJson")!;
        var method = type.GetMethod("Parse")!;

        try
        {
            return (JsonDocument)method.Invoke(null, [provider, raw, statusCode])!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }
}
