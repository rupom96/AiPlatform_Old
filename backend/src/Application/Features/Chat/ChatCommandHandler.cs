using System.Diagnostics;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Chat;

public class ChatCommandHandler(
    IChatProviderResolver resolver,
    ILlmUsageRecorder usageRecorder,
    IEmbeddingProvider embeddingProvider,
    ISemanticCache semanticCache,
    IModelRouter modelRouter,
    IPromptGuard promptGuard,
    IConversationMemory conversationMemory)
    : IRequestHandler<ChatCommand, Result<ChatResponse>>
{
    public async Task<Result<ChatResponse>> Handle(ChatCommand request, CancellationToken cancellationToken)
    {
        // guardrails — LLM-এ যাওয়ার আগে: injection block, PII redact
        var message = request.Message;
        if (promptGuard.IsEnabled)
        {
            var guard = promptGuard.Check(message);
            if (guard.Blocked)
                return Error.Validation("Chat.Blocked", guard.Reason!);
            message = guard.SafeInput;
        }

        // provider explicit না দিলে + routing on → complexity দেখে cheap/strong auto-select।
        var providerName = request.Provider;
        if (string.IsNullOrWhiteSpace(providerName) && modelRouter.IsEnabled)
            providerName = modelRouter.Route(message);

        var provider = resolver.Resolve(providerName, request.Model);
        if (provider is null)
        {
            var requested = string.IsNullOrWhiteSpace(request.Provider) ? "default" : request.Provider;
            return Error.Validation("Chat.UnknownProvider",
                $"No configured LLM provider available for '{requested}'. Set its API key in secrets.");
        }

        var useMemory = conversationMemory.IsActive(request.ConversationId, request.UserId);

        // semantic cache — কেবল single-turn-এ (memory থাকলে উত্তর context-নির্ভর, cache ভুল হবে)।
        float[]? queryEmbedding = null;
        if (semanticCache.IsEnabled && !useMemory)
        {
            queryEmbedding = await embeddingProvider.EmbedAsync(message, EmbeddingTaskType.RetrievalQuery, cancellationToken);
            var cached = await semanticCache.GetAsync(queryEmbedding, cancellationToken);
            if (cached is not null)
                return new ChatResponse(cached, "cache", "cache", Cached: true);
        }

        // conversation memory — summary + সাম্প্রতিক turn LLM-এ পাঠাই
        var messages = useMemory
            ? await conversationMemory.BuildMessagesAsync(request.UserId!, request.ConversationId!, message, cancellationToken)
            : [new ChatMessage("user", message)];

        var stopwatch = Stopwatch.StartNew();
        var result = await provider.CompleteAsync(messages, cancellationToken);
        stopwatch.Stop();

        usageRecorder.Record(new LlmUsageRecord(
            "chat", result.Provider, result.Model, result.InputTokens, result.OutputTokens, stopwatch.ElapsedMilliseconds));

        if (queryEmbedding is not null)
            await semanticCache.SetAsync(queryEmbedding, result.Content, cancellationToken);

        if (useMemory)
            await conversationMemory.PersistTurnAsync(
                request.UserId!, request.ConversationId!, message, result.Content, provider, cancellationToken);

        return new ChatResponse(result.Content, result.Provider, result.Model,
            ConversationId: useMemory ? request.ConversationId : null,
            LatencyMs: stopwatch.ElapsedMilliseconds);
    }
}
