using MediatR;

namespace AiPlatform.Application.Features.Llm.ListModels;

// Ollama-তে installed model-এর নাম তালিকা (frontend model-picker dropdown-এর জন্য)।
// Provider = কোন ollama — this machine ("ollama") নাকি GPU সার্ভার ("ollama-server")।
public record ListLocalModelsQuery(string? Provider = null) : IRequest<IReadOnlyList<string>>;
