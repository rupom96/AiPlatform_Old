using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.GoogleLogin;

// Google Sign-In: frontend GIS থেকে পাওয়া ID token → যাচাই → আমাদের নিজস্ব token জোড়া।
// Google login সবসময় persistent session (rememberMe) — Google-এ ঢোকা মানেই "এই device আমার"।
public record GoogleLoginCommand(string IdToken) : IRequest<Result<AuthResponse>>;
