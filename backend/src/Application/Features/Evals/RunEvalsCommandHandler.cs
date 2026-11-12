using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Features.Rag;
using MediatR;

namespace AiPlatform.Application.Features.Evals;

// প্রতিটা case RagQueryCommand দিয়ে চালায়, EvalScorer দিয়ে score করে, report বানায়।
// RAG pipeline-ই reuse করে — eval আলাদা code path বানায় না (drift এড়ায়)।
public class RunEvalsCommandHandler(IEvalDataset dataset, IMediator mediator)
    : IRequestHandler<RunEvalsCommand, Result<EvalReport>>
{
    public async Task<Result<EvalReport>> Handle(RunEvalsCommand request, CancellationToken cancellationToken)
    {
        var results = new List<EvalCaseResult>(dataset.Cases.Count);

        foreach (var evalCase in dataset.Cases)
        {
            var query = new RagQueryCommand(evalCase.Question, evalCase.Method, request.Provider);
            var response = await mediator.Send(query, cancellationToken);

            results.Add(response.IsSuccess
                ? EvalScorer.Score(evalCase, response.Value.Answer)
                : new EvalCaseResult(evalCase.Id, false, string.Empty, response.Error.Description));
        }

        return EvalScorer.Aggregate(results);
    }
}
