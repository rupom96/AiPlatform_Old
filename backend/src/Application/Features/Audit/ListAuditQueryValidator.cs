using FluentValidation;

namespace AiPlatform.Application.Features.Audit;

public class ListAuditQueryValidator : AbstractValidator<ListAuditQuery>
{
    public ListAuditQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
