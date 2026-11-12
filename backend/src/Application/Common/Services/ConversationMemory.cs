using System.Diagnostics;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Services;

// conversation memory: build (history→LLM) + persist (turn save + overflow summarize)।
// chat ও stream handler একই instance ব্যবহার করে — behavior divergent হয় না।
public class ConversationMemory(IConversationStore store, ILlmUsageRecorder usageRecorder) : IConversationMemory
{
    // পুরনো turn compress করার instruction (English), summary content বাংলায়।
    private const string SummarizePrompt =
        "You maintain a running summary of a conversation. Given the previous summary (may be empty) and the " +
        "new messages below, produce an updated concise summary that preserves key facts, names, and decisions. " +
        "Return only the summary text. Write it in Bengali.";

    public bool IsActive(string? conversationId, string? userId) =>
        store.IsEnabled
        && !string.IsNullOrWhiteSpace(conversationId)
        && !string.IsNullOrWhiteSpace(userId);

    public async Task<IReadOnlyList<ChatMessage>> BuildMessagesAsync(
        string userId, string conversationId, string userMessage, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(userId, conversationId, cancellationToken);

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(state.Summary))
            messages.Add(new ChatMessage("system", $"Summary of the conversation so far: {state.Summary}"));
        messages.AddRange(state.Recent);
        messages.Add(new ChatMessage("user", userMessage));
        return messages;
    }

    public async Task PersistTurnAsync(
        string userId, string conversationId, string userMessage, string assistantReply,
        IChatProvider provider, CancellationToken cancellationToken)
    {
        var state = await store.GetAsync(userId, conversationId, cancellationToken);

        var recent = new List<ChatMessage>(state.Recent)
        {
            new("user", userMessage),
            new("assistant", assistantReply),
        };

        var summary = state.Summary;
        var max = store.MaxRecentMessages;
        if (recent.Count > max)
        {
            var overflow = recent.Take(recent.Count - max).ToList();
            summary = await SummarizeAsync(provider, summary, overflow, cancellationToken);
            recent = recent.Skip(recent.Count - max).ToList();
        }

        await store.SaveAsync(userId, conversationId, new ConversationState(summary, recent), cancellationToken);
    }

    private async Task<string> SummarizeAsync(
        IChatProvider provider, string? previousSummary, IReadOnlyList<ChatMessage> overflow, CancellationToken cancellationToken)
    {
        var block = string.Join("\n", overflow.Select(m => $"{m.Role}: {m.Content}"));
        var user = $"Previous summary:\n{previousSummary ?? "(none)"}\n\nNew messages:\n{block}";

        var stopwatch = Stopwatch.StartNew();
        var result = await provider.CompleteAsync(
            [new ChatMessage("system", SummarizePrompt), new ChatMessage("user", user)], cancellationToken);
        stopwatch.Stop();

        usageRecorder.Record(new LlmUsageRecord(
            "chat-summary", result.Provider, result.Model, result.InputTokens, result.OutputTokens, stopwatch.ElapsedMilliseconds));

        return result.Content;
    }
}
