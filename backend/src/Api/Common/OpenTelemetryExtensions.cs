using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace AiPlatform.Api.Common;

// OpenTelemetry — এক request = এক trace, ভেতরে প্রতিটা DB query/outbound HTTP call = আলাদা span।
// Serilog বলে "chat request-টা ৪ সেকেন্ড লেগেছে"; trace বলে ওই ৪ সেকেন্ডের কোনটা LLM, কোনটা SQL, কোনটা Qdrant।
//
// Phase 6a-র LlmUsage (token/cost) এর সাথে সংঘর্ষ নেই — ওটা business metric (কত খরচ হলো),
// এটা technical telemetry (কোথায় সময় গেল)। দুটো আলাদা প্রশ্নের উত্তর।
public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(OtelSettings.SectionName).Get<OtelSettings>() ?? new OtelSettings();
        if (!settings.Enabled)
            return services;   // collector না থাকলে (test/CI) নীরবে বন্ধ — export retry-র noise নেই

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(settings.ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    // health check প্রতি ১০ সেকেন্ডে আসে — trace-এ শুধু আবর্জনা বাড়ায়
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()   // LLM + Qdrant call — সাধারণত সবচেয়ে ধীর অংশ
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()   // request rate, duration, status
                .AddHttpClientInstrumentation())
            .WithLogging()                        // ILogger → OTLP, trace-এর সাথে জোড়া লেগে
            .UseOtlpExporter();                   // endpoint: OTEL_EXPORTER_OTLP_ENDPOINT (default http://localhost:4317)

        return services;
    }
}
