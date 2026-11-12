using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Register;

public class RegisterCommandHandler(IIdentityService identityService)
    : IRequestHandler<RegisterCommand, Result<string>>
{
    public Task<Result<string>> Handle(RegisterCommand request, CancellationToken cancellationToken) =>
        identityService.RegisterAsync(request.Email, request.Password, request.FullName, cancellationToken);
}
