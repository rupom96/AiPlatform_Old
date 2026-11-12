namespace AiPlatform.Api.Contracts;

// Provider null/খালি হলে config-এর default (tool-capable হতে হবে)।
// Model = ollama model-picker override; null হলে provider-এর configured default model।
public record AgentRequest(string Message, string? Provider, string? Model = null);
