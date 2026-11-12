using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.ForgotPassword;

public record ForgotPasswordCommand(string Email) : IRequest<Result>;
