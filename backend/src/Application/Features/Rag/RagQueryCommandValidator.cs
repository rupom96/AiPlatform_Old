using FluentValidation;

namespace AiPlatform.Application.Features.Rag;

public class RagQueryCommandValidator : AbstractValidator<RagQueryCommand>
{
    public RagQueryCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(4000);
    }
}
