using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Logout;

public record LogoutCommand(string RefreshToken) : IRequest<Result>;
