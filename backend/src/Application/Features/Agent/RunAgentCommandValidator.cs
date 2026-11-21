using AiPlatform.Application.Common.Models;
using FluentValidation;

namespace AiPlatform.Application.Features.Agent;

public class RunAgentCommandValidator : AbstractValidator<RunAgentCommand>
{
    public RunAgentCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);

        // অজানা নাম নীরবে উপেক্ষা না করে ৪০০ — নইলে টাইপো করলে "rerank চলছে" ভেবে ভুল তুলনা হতো।
        RuleFor(x => x.RetrievalMethod)
            .Must(RetrievalMethods.IsValid)
            .WithMessage($"RetrievalMethod must be one of: {string.Join(", ", RetrievalMethods.Selectable)}.");
    }
}
