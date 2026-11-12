using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// এক LLM chat provider (Gemini, Groq, … পরে Ollama/vLLM)। নতুন provider = নতুন impl, কোড rewrite নয়।
public interface IChatProvider
{
    string Name { get; }
    Task<ChatResult> CompleteAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);

    // token-by-token stream (SSE-এর জন্য)।
    IAsyncEnumerable<string> StreamAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);

    // JSON-mode — model শুধু valid JSON string ফেরত দেয় (structured output-এর জন্য)।
    Task<string> CompleteJsonAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
