using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AiPlatform.Api.IntegrationTests;

// register → login → me: unit test যা ধরতে পারে না সেটাই এখানে — Identity password policy, EF write,
// JWT sign→validate round-trip, [Authorize] pipeline, refresh cookie। সব আসল SQL Server + Redis-এ।
[Collection(nameof(ApiCollection))]
public class AuthFlowTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = ApiFactory.TestPassword;

    private readonly HttpClient _client = factory.CreateClient();

    // /auth/register-এ 5/min rate limit; TestServer-এ সব request এক "IP" → suite বড় হলে register 429 খেত।
    // প্রতি test-এর আগে counter পরিষ্কার — নইলে ফেল করত auth নয়, শুধু টেস্টের সংখ্যা বাড়ার কারণে।
    public Task InitializeAsync() => factory.ResetRateLimitsAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Register_Login_Me_RoundTrip()
    {
        // প্রতি run-এ ইউনিক email — RequireUniqueEmail, আর test order/rerun-এ collide করা চলবে না
        var email = $"user-{Guid.NewGuid():N}@test.local";

        var register = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = Password, fullName = "Integration User" });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // refresh token শুধু httpOnly cookie-তে যায় (XSS-safe) — body-তে নয়
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), c => c.Contains("refreshToken") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meResponse = await _client.SendAsync(me);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var body = await meResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("Integration User", body.GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401_AndSetsNoCookie()
    {
        var email = $"user-{Guid.NewGuid():N}@test.local";
        await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = Password, fullName = "Integration User" });

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Wr0ng!Passw0rd" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Register_WithWeakPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = $"user-{Guid.NewGuid():N}@test.local", password = "weak", fullName = "Integration User" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
