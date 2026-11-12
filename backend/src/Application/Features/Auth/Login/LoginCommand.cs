using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Login;

// RememberMe: true = persistent refresh cookie (browser বন্ধেও টেকে), false = session cookie।
public record LoginCommand(string Email, string Password, bool RememberMe = false) : IRequest<Result<AuthResponse>>;
