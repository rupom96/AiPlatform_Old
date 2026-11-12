using FluentValidation;

namespace AiPlatform.Application.Features.Chat;

public class ChatCommandValidator : AbstractValidator<ChatCommand>
{
    public ChatCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(8000);
    }
}
