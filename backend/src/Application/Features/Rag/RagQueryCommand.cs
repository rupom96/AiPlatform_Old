using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Rag;

// Method: keyword | embedding | qdrant (null হলে keyword)। Provider: LLM (null হলে default)।
// Model: ওই provider ওই model-এ (ollama model-picker; null = config default)।
public record RagQueryCommand(string Question, string? Method, string? Provider, string? Model = null)
    : IRequest<Result<RagResponse>>;
