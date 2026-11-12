using System.Text;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using AiPlatform.Api;
using AiPlatform.Api.Common;
using AiPlatform.Application;
using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure;
using AiPlatform.Infrastructure.Auth;
using AiPlatform.Infrastructure.Persistence;

// --- Bootstrap logger: startup crash-ও যেন log হয় ---
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // default console provider সরাও — নইলে writeToProviders (নিচে) ওটাতেও লেখে আর প্রতি log দুবার ছাপে।
    // এর পরে যোগ হওয়া provider (Phase 8d-এর OpenTelemetry) টিকে থাকে।
    builder.Logging.ClearProviders();

    // Serilog — appsettings থেকে config পড়ে (structured + correlation, Phase 0 বেসিক)।
    // writeToProviders: true → Serilog অন্য ILogger provider-দেরও লেখে (Phase 8d-এ OpenTelemetry);
    // default false মানে OTel logging provider নীরবে উপেক্ষিত হতো, log আর trace জোড়া লাগত না।
    builder.Host.UseSerilog((context, services, config) => config
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext(), writeToProviders: true);

    // graceful shutdown — SIGTERM এ in-flight request শেষ করার সময় দেয় (container stop/replica rollout)
    builder.Host.ConfigureHostOptions(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));

    // Layer wiring
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // OpenTelemetry — traces + metrics + logs → OTLP (feature-toggle: Otel:Enabled)
    builder.Services.AddObservability(builder.Configuration);

    // current user/tenant (claim → ICurrentUser); DbContext tenant filter + upload-এ লাগে
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();

    // --- JWT authentication ---
    var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
        ?? throw new InvalidOperationException("Jwt config অনুপস্থিত।");
    if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
        throw new InvalidOperationException(
            $"Jwt:SecretKey সেট নেই। Current environment = '{builder.Environment.EnvironmentName}'. " +
            "Development হলে appsettings.Development.json/user-secrets দেখো; না হলে VS profile Development-এ দাও।");

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidAudience = jwtSettings.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
                ClockSkew = TimeSpan.Zero   // exp ঠিক ১৫ মিনিটেই শেষ, default ৫ মিনিট ছাড় না
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddControllers();

    // MCP server — app-এর tool গুলো Model Context Protocol-এ expose (HTTP transport)।
    // WithToolsFromAssembly() এই assembly-র [McpServerToolType] scan করে।
    builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();

    // ProblemDetails + global exception handler — unexpected error consistent RFC 7807
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    // API versioning — সব endpoint /api/v{n}; version না দিলে v1 ধরে
    builder.Services
        .AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        })
        .AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";     // v1, v2...
            options.SubstituteApiVersionInUrl = true;
        });

    // Swagger — প্রতি version-এর জন্য আলাদা doc (ConfigureSwaggerOptions)
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.ConfigureOptions<ConfigureSwaggerOptions>();

    // CORS — শুধু React app-এর origin (appsettings: Cors:AllowedOrigins)
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));   // refresh cookie-র জন্য (Phase 1)

    // Health checks
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // nginx LB-এর পেছনে আসল client IP/scheme (rate-limit ঠিক কাজ করতে)
    // ponytail: নিজের compose network-এ সব proxy trust; public edge হলে KnownProxies সীমিত করো
    var forwardedHeaders = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    };
    forwardedHeaders.KnownNetworks.Clear();
    forwardedHeaders.KnownProxies.Clear();
    app.UseForwardedHeaders(forwardedHeaders);

    // কোন replica সাড়া দিল (LB verify করতে সহজ)
    app.Use(async (context, next) =>
    {
        context.Response.Headers["X-Instance"] = Environment.MachineName;
        await next();
    });

    app.UseSerilogRequestLogging();     // প্রতি request এক লাইনে log (method, path, status, ms)
    app.UseExceptionHandler();          // unhandled → ProblemDetails

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
            foreach (var desc in provider.ApiVersionDescriptions)
                options.SwaggerEndpoint($"/swagger/{desc.GroupName}/swagger.json", desc.GroupName.ToUpperInvariant());
        });

        // dev-এ root "/" খুললে সরাসরি swagger UI দেখাও
        app.MapGet("/", () => Results.Redirect("/swagger"));
    }

    app.UseHttpsRedirection();
    app.UseCors("frontend");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    // MCP endpoints (/sse, /messages) — external MCP client-এর জন্য।
    // ⚠️ এগুলো auth ছাড়া খোলা, আর tool গুলো ERP database পড়ে/লেখে — তাই internet-এ
    // খোলা রাখা মানে যে কেউ ডেটা দেখতে ও sales order বসাতে পারবে।
    // তাই Development-এ ডিফল্টে চালু, Production-এ শুধু Mcp:Enabled=true দিলে।
    // ponytail: flag-ই যথেষ্ট; আসল OAuth দরকার হলে তখন বসবে (deploy-এ Caddy /sse route-ও বন্ধ)।
    if (app.Configuration.GetValue("Mcp:Enabled", app.Environment.IsDevelopment()))
        app.MapMcp();

    // startup-এ migration apply + base role seed (fresh container/CI-তে DB নিজে তৈরি হয়)
    // ponytail: auto-migrate boilerplate/demo-র সুবিধা; কড়া prod-এ controlled migration ভালো
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);

        // Sample ERP — flag দিয়ে আলাদা করা। integration test আর production যেন ভুল করে
        // developer-এর ERP database-এ migrate/seed না চালায়। seeder নিজে idempotent।
        if (app.Configuration.GetValue<bool>("Erp:SeedOnStartup"))
        {
            var erpDb = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            await erpDb.Database.MigrateAsync();
            await ErpSeeder.SeedAsync(erpDb);
        }
    }

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// integration test (Phase 8) WebApplicationFactory<Program> ধরতে পারে
public partial class Program;
