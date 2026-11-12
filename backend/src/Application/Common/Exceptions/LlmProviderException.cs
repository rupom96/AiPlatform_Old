namespace AiPlatform.Application.Common.Exceptions;

// LLM provider upstream fail (429/404/403…)। global handler এটাকে clean 502-তে map করে,
// raw upstream body user-কে না দিয়ে (leak রোধ) — শুধু log-এ যায়।
public class LlmProviderException(string provider, int statusCode, string detail)
    : Exception($"{provider} provider error {statusCode}: {detail}")
{
    public string Provider { get; } = provider;
    public int StatusCode { get; } = statusCode;
}
