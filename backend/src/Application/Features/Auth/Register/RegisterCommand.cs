using AiPlatform.Application.Common.Results;
using MediatR;

namespace AiPlatform.Application.Features.Auth.Register;

public record RegisterCommand(string Email, string Password, string FullName) : IRequest<Result<string>>;
