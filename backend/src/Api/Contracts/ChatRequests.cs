namespace AiPlatform.Api.Contracts;

// Provider null/খালি হলে config-এর default। Model দিলে ওই provider ওই model-এ (ollama model-picker)।
// ConversationId দিলে multi-turn memory চালু।
public record ChatRequest(string Message, string? Provider, string? Model = null, string? ConversationId = null);
