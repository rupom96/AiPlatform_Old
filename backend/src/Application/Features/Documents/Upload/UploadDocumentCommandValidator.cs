using FluentValidation;

namespace AiPlatform.Application.Features.Documents.Upload;

public class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.Content).NotEmpty().MaximumLength(200_000);
    }
}
