using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Analyzer;

public record AnalyzeCommand(string Text, string? Provider) : IRequest<Result<MessageAnalysis>>;
