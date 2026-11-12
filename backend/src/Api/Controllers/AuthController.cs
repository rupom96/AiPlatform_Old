using System.Security.Claims;
using Asp.Versioning;
using AiPlatform.Api.Common;
using AiPlatform.Api.Contracts;
using AiPlatform.Application.Features.Auth.ForgotPassword;
using AiPlatform.Application.Features.Auth.GoogleLogin;
using AiPlatform.Application.Features.Auth.Login;
using AiPlatform.Application.Features.Auth.Logout;
using AiPlatform.Application.Features.Auth.Refresh;
using AiPlatform.Application.Features.Auth.Register;
using AiPlatform.Application.Features.Auth.ResetPassword;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiPlatform.Api.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
public class AuthController(IMediator mediator, IWebHostEnvironment environment) : ApiControllerBase
{
    private const string RefreshCookieName = "refreshToken";

    [RateLimit(5, 60)]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RegisterCommand(request.Email, request.Password, request.FullName), cancellationToken);

        return result.IsSuccess
            ? Created(string.Empty, new { userId = result.Value })
            : ToProblem(result.Error);
    }

    [RateLimit(10, 60)]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new LoginCommand(request.Email, request.Password, request.RememberMe), cancellationToken);
        if (result.IsFailure)
            return ToProblem(result.Error);

        var auth = result.Value;
        SetRefreshCookie(auth.RefreshToken, auth.RefreshTokenExpiresAtUtc, auth.RememberMe);

        // access token শুধু body-তে (frontend memory-তে রাখবে, localStorage নয়)
        return Ok(new { accessToken = auth.AccessToken, expiresAtUtc = auth.AccessTokenExpiresAtUtc });
    }

    // Google Sign-In: GIS-এর ID token → যাচাই → নিজস্ব access token + refresh cookie (login-এর মতোই)
    [RateLimit(10, 60)]
    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(GoogleLoginRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GoogleLoginCommand(request.IdToken), cancellationToken);
        if (result.IsFailure)
            return ToProblem(result.Error);

        var auth = result.Value;
        SetRefreshCookie(auth.RefreshToken, auth.RefreshTokenExpiresAtUtc, auth.RememberMe);
        return Ok(new { accessToken = auth.AccessToken, expiresAtUtc = auth.AccessTokenExpiresAtUtc });
    }

    [RateLimit(5, 60)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        await mediator.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        // সবসময় একই response — email আছে কিনা reveal করি না
        return Ok(new { message = "If the email exists, a password reset link has been sent." });
    }

    [RateLimit(10, 60)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new ResetPasswordCommand(request.Email, request.Token, request.NewPassword), cancellationToken);

        return result.IsSuccess
            ? Ok(new { message = "Password has been reset. You can now log in." })
            : ToProblem(result.Error);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) || string.IsNullOrEmpty(refreshToken))
            return Unauthorized();

        var result = await mediator.Send(new RefreshCommand(refreshToken), cancellationToken);
        if (result.IsFailure)
        {
            ClearRefreshCookie();   // invalid/reused → cookie মুছে দাও
            return ToProblem(result.Error);
        }

        var auth = result.Value;
        SetRefreshCookie(auth.RefreshToken, auth.RefreshTokenExpiresAtUtc, auth.RememberMe);
        return Ok(new { accessToken = auth.AccessToken, expiresAtUtc = auth.AccessTokenExpiresAtUtc });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var refreshToken) && !string.IsNullOrEmpty(refreshToken))
            await mediator.Send(new LogoutCommand(refreshToken), cancellationToken);

        ClearRefreshCookie();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            email = User.FindFirstValue(ClaimTypes.Email),
            fullName = User.FindFirstValue("fullName"),
            roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value)
        });
    }

    // refresh token: httpOnly + SameSite=Strict → JS পড়তে পারে না (XSS-safe)।
    // rememberMe=true → persistent cookie (Expires সেট, browser বন্ধেও টেকে);
    // false → session cookie (Expires নেই, browser বন্ধে মুছে যায়) — tab-এর মধ্যে silent refresh তবু চলে।
    private void SetRefreshCookie(string token, DateTime expiresAtUtc, bool rememberMe)
    {
        Response.Cookies.Append(RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),   // dev http-এ Secure=false, নইলে cookie drop হয়
            SameSite = SameSiteMode.Strict,
            Expires = rememberMe ? expiresAtUtc : null,
            Path = "/api/v1/auth"
        });
    }

    // delete করতে হলে একই attribute (Path/Secure/SameSite) দিয়ে করতে হয়
    private void ClearRefreshCookie()
    {
        Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment(),
            SameSite = SameSiteMode.Strict,
            Path = "/api/v1/auth"
        });
    }
}
