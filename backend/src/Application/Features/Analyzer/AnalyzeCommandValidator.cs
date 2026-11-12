using FluentValidation;

namespace AiPlatform.Application.Features.Analyzer;

public class AnalyzeCommandValidator : AbstractValidator<AnalyzeCommand>
{
    public AnalyzeCommandValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MaximumLength(8000);
    }
}
