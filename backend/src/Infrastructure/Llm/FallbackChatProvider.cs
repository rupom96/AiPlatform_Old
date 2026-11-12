using AiPlatform.Application.Common.Exceptions;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace AiPlatform.Infrastructure.Llm;

// এক provider ব্যর্থ হলে পরেরটায় যায় (Cerebras → groq → gemini → ollama)।
// কারণ: প্রতিটা free tier-এই কোনো না কোনো সীমা/hiccup আছে; কিন্তু সব একসাথে মরে না।
// শেষে local রাখলে chain কখনো পুরো ব্যর্থ হয় না — ধীর, কিন্তু উত্তর আসে।
//
// ⚠️ শুধু **transient/quota** ব্যর্থতায় fallback করে। ভুল request (400) সব provider-এই
// ব্যর্থ হবে, তাই ওখানে চেষ্টা করা মানে শুধু দেরি — সঙ্গে সঙ্গে throw করি।
// ব্যতিক্রম: 400 প্রথম provider-এর নিজস্ব বিধিনিষেধ (schema quirk) থেকেও আসতে পারে,
// তাই একে retryable ধরা হয় — বাস্তবে groq/cerebras দুটোই মাঝেমধ্যে 400 দেয় overload-এ।
public class FallbackChatProvider(
    IReadOnlyList<IChatProvider> providers,
    ILogger<FallbackChatProvider> logger)
    : IChatProvider, IToolCallingProvider
{
    public string Name => providers[0].Name;

    public Task<ChatResult> CompleteAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => TryEachAsync(
            provider => provider.CompleteAsync(messages, cancellationToken),
            _ => true,
            cancellationToken);

    public Task<string> CompleteJsonAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => TryEachAsync(
            provider => provider.CompleteJsonAsync(messages, cancellationToken),
            _ => true,
            cancellationToken);

    // ⚠️ stream-এ fallback নেই। প্রথম token client-এ চলে গেলে মাঝপথে provider বদলানো যায় না
    // (আধা উত্তর + নতুন শুরু = জগাখিচুড়ি)। তাই প্রথমটাই চালাই; ব্যর্থ হলে caller দেখবে।
    // ponytail: stream-এ fallback চাইলে প্রথম token আসার আগ পর্যন্ত buffer করতে হবে — তখন করা যাবে।
    public IAsyncEnumerable<string> StreamAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
        => providers[0].StreamAsync(messages, cancellationToken);

    public Task<ChatToolResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
        => TryEachAsync(
            provider => ((IToolCallingProvider)provider).CompleteWithToolsAsync(messages, tools, cancellationToken),
            // tool-calling সবাই পারে না — যারা পারে না তাদের বাদ দিয়ে এগোই।
            provider => provider is IToolCallingProvider,
            cancellationToken);

    private async Task<TResult> TryEachAsync<TResult>(
        Func<IChatProvider, Task<TResult>> call,
        Func<IChatProvider, bool> canHandle,
        CancellationToken cancellationToken)
    {
        var usable = providers.Where(canHandle).ToList();
        if (usable.Count == 0)
            throw new InvalidOperationException("No provider in the fallback chain supports this call.");

        for (var i = 0; i < usable.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await call(usable[i]);
            }
            catch (Exception ex) when (i < usable.Count - 1 && IsRetryable(ex))
            {
                // শেষ provider-এর ব্যর্থতা চাপা দিই না — তখন আসল error-ই উপরে যায়।
                logger.LogWarning(ex, "LLM provider {Provider} failed, falling back to {Next}",
                    usable[i].Name, usable[i + 1].Name);
            }
        }

        throw new UnreachableException();
    }

    // upstream সমস্যা (429 quota, 5xx, 400 overload) বা network/timeout → পরেরটায় যাওয়ার মানে আছে।
    // আমাদের নিজেদের bug (NullReference ইত্যাদি) → fallback করে লাভ নেই, সব provider-এ একই হবে।
    private static bool IsRetryable(Exception ex) => ex switch
    {
        LlmProviderException => true,
        HttpRequestException => true,
        TaskCanceledException => true,   // HttpClient timeout
        _ => false
    };

    private sealed class UnreachableException()
        : InvalidOperationException("Fallback loop ended without a result — this should never happen.");
}
