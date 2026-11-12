using FluentValidation;

namespace AiPlatform.Application.Features.Agent;

public class RunAgentCommandValidator : AbstractValidator<RunAgentCommand>
{
    public RunAgentCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(4000);
    }
}
