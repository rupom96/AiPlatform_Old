using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.ResetPassword;

public record ResetPasswordCommand(string Email, string Token, string NewPassword) : IRequest<Result>;
