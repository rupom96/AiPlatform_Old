using FluentValidation;

namespace AiPlatform.Application.Features.Documents.List;

public class ListDocumentsQueryValidator : AbstractValidator<ListDocumentsQuery>
{
    public ListDocumentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
