using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Refresh;

// RefreshToken cookie থেকে আসে (controller পাঠায়)।
public record RefreshCommand(string RefreshToken) : IRequest<Result<AuthResponse>>;
