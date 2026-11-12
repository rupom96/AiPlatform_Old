using AiPlatform.Application.Features.Evals;

namespace AiPlatform.Application.Common.Interfaces;

// eval case-এর উৎস। impl app-specific/demo (seed) — নতুন project-এ বদলে যাবে।
public interface IEvalDataset
{
    IReadOnlyList<EvalCase> Cases { get; }
}
