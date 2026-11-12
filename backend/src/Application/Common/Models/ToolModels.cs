namespace AiPlatform.Application.Common.Models;

// LLM-কে দেওয়া tool-এর সংজ্ঞা (function-calling)। ParametersSchema = JSON-schema object।
public record ToolDefinition(string Name, string Description, object ParametersSchema);

// LLM যে tool call করতে চায় (Id = provider-এর reference, result ফেরানোর সময় লাগে)।
public record ToolCall(string Id, string Name, string ArgumentsJson);

// tool-calling সহ এক LLM turn-এর ফল: হয় Content (final answer) নয়তো ToolCalls (execute করতে হবে)।
public record ChatToolResult(
    string? Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string Provider,
    string Model,
    int InputTokens = 0,
    int OutputTokens = 0);
