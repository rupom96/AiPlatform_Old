using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Evals;

// dataset-এর সব case চালায়। Provider null হলে config default LLM।
public record RunEvalsCommand(string? Provider) : IRequest<Result<EvalReport>>;
