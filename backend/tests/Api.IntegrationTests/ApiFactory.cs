using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace AiPlatform.Api.IntegrationTests;

// পুরো API আসল SQL Server + Redis container-এ বুট করে (Program.cs যেমন আছে — startup migration সহ)।
// fake নয় বলে routing/auth/validation/EF/Redis একসাথে সত্যিকারভাবে যাচাই হয়।
// Qdrant + LLM provider ইচ্ছে করে বাদ: health/ping/auth path এগুলো ছোঁয় না (lazy singleton)।
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // container ephemeral — এই key দিয়ে কিছু sign হয় না, তাই commit করা নিরাপদ (32+ byte দরকার HMAC-SHA256-এ)
    private const string TestJwtSecret = "integration-test-signing-key-0123456789abcdef0123456789abcdef";

    // Identity policy মেটায় (8+, upper, lower, digit, non-alphanumeric)
    public const string TestPassword = "Str0ng!Passw0rd";

    // image tag docker-compose.yml-এর সাথে মেলানো → test আর dev এক version-এ চলে
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development → refresh cookie Secure=false, নইলে TestServer-এর http:// origin-এ cookie drop হয়
        builder.UseEnvironment(Environments.Development);
    }

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();
        await _redis.StartAsync();

        // container address অবশ্যই environment variable দিয়ে দিতে হবে, ConfigureAppConfiguration দিয়ে নয়:
        // Program.cs startup-এ Jwt/Redis সরাসরি builder.Configuration থেকে পড়ে, আর WebApplicationFactory-র
        // config delegate চলে তারও পরে → override দেরিতে পৌঁছে (token test-key-তে sign, dev-key-তে validate → 401;
        // Redis নীরবে localhost-এ পড়ে)। env var host তৈরির আগেই কনফিগে থাকে, তাই eager read-ও সঠিক মান পায়।
        // `__` = config section separator।
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _sql.GetConnectionString());
        Environment.SetEnvironmentVariable("Redis__Connection", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__SecretKey", TestJwtSecret);

        // টেস্টে কোনো OTLP collector নেই — চালু থাকলে exporter প্রতি export-এ retry করে log ভাসাত
        Environment.SetEnvironmentVariable("Otel__Enabled", "false");

        // ⚠️ Development env → appsettings.Development.json পড়া হয়, যেখানে Erp:SeedOnStartup=true এবং
        // Erp connection developer-এর localhost SQL Server-এ। বন্ধ না করলে test run ডেভেলপারের
        // আসল ERP database-এ migrate+seed চালাত। এই test গুলো ERP ছোঁয় না, তাই সম্পূর্ণ বন্ধ।
        Environment.SetEnvironmentVariable("Erp__SeedOnStartup", "false");
    }

    // register+login করা client — যেসব test-এর বিষয় auth নয়, সেগুলোর জন্য।
    // প্রতি call-এ নতুন user = নতুন tenant (signup-প্রতি tenant), তাই দুবার ডাকলেই tenant isolation টেস্ট করা যায়।
    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        await ResetRateLimitsAsync();

        var client = CreateClient();
        var email = $"user-{Guid.NewGuid():N}@test.local";

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = TestPassword, fullName = "Test User" });
        register.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = TestPassword });
        login.EnsureSuccessStatusCode();

        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // /auth/register-এ [RateLimit(5, 60)] — key = path + client IP। TestServer-এ IP নেই ("unknown"),
    // তাই পুরো suite-এর সব register এক counter শেয়ার করে → ৬ষ্ঠটা 429, টেস্ট rate limiter-এর সাথে যুদ্ধ করে।
    // আসল client হলে IP আলাদা হতো; কৃত্রিম সংঘর্ষটাই সরাই — production rate limiter অক্ষত থাকে।
    public async Task ResetRateLimitsAsync()
    {
        var redis = Services.GetRequiredService<IConnectionMultiplexer>();
        var db = redis.GetDatabase();
        var server = redis.GetServer(redis.GetEndPoints().First());

        foreach (var key in server.Keys(pattern: "ratelimit:*"))
            await db.KeyDeleteAsync(key);
    }

    // xunit v2 IAsyncLifetime.DisposeAsync (Task) vs WebApplicationFactory.DisposeAsync (ValueTask) —
    // signature আলাদা, তাই `new` দিয়ে ঢেকে ভেতরে base-টাও dispose করি।
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

// এক collection = container একবারই ওঠে, সব test class শেয়ার করে (প্রতি class-এ SQL Server তোলা অসহ্য ধীর)
[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
