using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Results;
using AiPlatform.Application.Common.Settings;
using MediatR;

namespace AiPlatform.Application.Features.Auth.ForgotPassword;

public class ForgotPasswordCommandHandler(
    IIdentityService identityService,
    IEmailSender emailSender,
    FrontendSettings frontendSettings)
    : IRequestHandler<ForgotPasswordCommand, Result>
{
    public async Task<Result> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var token = await identityService.GeneratePasswordResetTokenAsync(request.Email, cancellationToken);

        // user থাকলে email; না থাকলেও একই response — enumeration রোধ
        if (token is not null)
        {
            // token/email URL-encode — Identity token-এ +, / জাতীয় অক্ষর থাকে
            var resetLink = $"{frontendSettings.BaseUrl.TrimEnd('/')}/reset-password" +
                            $"?email={Uri.EscapeDataString(request.Email)}&token={Uri.EscapeDataString(token)}";

            await emailSender.SendAsync(
                request.Email,
                "Reset your password",
                $"Click the link to reset your password:\n{resetLink}\n\nIf you didn't request this, ignore this email.",
                cancellationToken);
        }

        return Result.Success();
    }
}
