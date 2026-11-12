using FluentValidation;

namespace AiPlatform.Application.Features.Auth.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(100);
        // detail policy (upper/digit/symbol) Identity enforce করে; এখানে বেসিক length gate
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}
