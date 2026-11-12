using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// function-calling সমর্থন করে এমন provider (OpenAI-compatible: groq/mistral…)।
// সব provider পারে না (যেমন এখন Gemini আলাদা format) — তাই IChatProvider থেকে আলাদা।
public interface IToolCallingProvider
{
    Task<ChatToolResult> CompleteWithToolsAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken);
}
