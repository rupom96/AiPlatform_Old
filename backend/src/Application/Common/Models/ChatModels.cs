namespace AiPlatform.Application.Common.Models;

// Provider-agnostic chat message। role: "system" | "user" | "assistant" | "tool"।
// tool-calling (agent): assistant message-এ ToolCalls থাকতে পারে; "tool" message-এ ToolCallId (কোন call-এর ফল)।
public record ChatMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    IReadOnlyList<ToolCall>? ToolCalls = null);

// এক provider call-এর ফল (token count observability-র জন্য, default 0 → backward-compatible)।
public record ChatResult(string Content, string Provider, string Model, int InputTokens = 0, int OutputTokens = 0);

// API-তে ফেরত চ্যাট উত্তর। Cached=true হলে semantic cache থেকে (LLM call হয়নি)।
// ConversationId সেট থাকলে client পরের turn-এ এটা পাঠিয়ে memory ধরে রাখতে পারে।
// LatencyMs = শুধু provider call-এর সময় (embedding/cache/DB বাদ) — model তুলনার জন্য এটাই সঠিক;
// client-এর মাপা মোট সময়ে network+queue মেশানো থাকে। cache hit-এ 0 (LLM call হয়নি)।
public record ChatResponse(
    string Answer,
    string Provider,
    string Model,
    bool Cached = false,
    string? ConversationId = null,
    long LatencyMs = 0);
