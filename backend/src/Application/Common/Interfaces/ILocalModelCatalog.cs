namespace AiPlatform.Application.Common.Interfaces;

// Local LLM runtime (Ollama) থেকে installed model-এর তালিকা।
// Impl Infrastructure-এ (Ollama /api/tags); ollama configure না থাকলে খালি তালিকা।
public interface ILocalModelCatalog
{
    // provider = configured ollama provider-এর নাম ("ollama" = এই মেশিন, "ollama-server" = GPU সার্ভার)।
    // null হলে "ollama"। অচেনা নাম দিলে খালি তালিকা — ইচ্ছেমতো URL ডাকা যায় না।
    Task<IReadOnlyList<string>> ListModelsAsync(string? provider, CancellationToken cancellationToken);
}
