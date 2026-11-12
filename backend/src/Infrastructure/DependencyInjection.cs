using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Infrastructure.Auth;
using AiPlatform.Infrastructure.Identity;
using AiPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace AiPlatform.Infrastructure;

// Infrastructure layer-এর সব service (EF Core, Identity, Redis, Qdrant, LLM providers) এখানে register হয়।
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null)));

        // Sample ERP — আলাদা database (পরে আসল ERP-তে swap করা যাবে)।
        // AI tool গুলো শুধু পড়বে, তাই NoTracking default — কম মেমরি, দ্রুত query।
        services.AddDbContext<ErpDbContext>(options =>
            options
                .UseSqlServer(
                    configuration.GetConnectionString("Erp"),
                    sql => sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));

        services.AddScoped<Erp.ErpStockService>();

        services
            .AddIdentityCore<AppUser>(options =>
            {
                // Password policy — FAANG-level baseline
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // Account lockout — brute-force রোধ
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();   // forgot/reset-password token (Phase 1e)

        // JWT + refresh token settings
        services.Configure<Auth.JwtSettings>(configuration.GetSection(Auth.JwtSettings.SectionName));

        // Redis — refresh-token store + (পরে) cache/rate-limit
        var redisConnection = configuration.GetSection("Redis")["Connection"]
            ?? throw new InvalidOperationException("Redis:Connection সেট নেই।");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnection));

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        // Email — SMTP configure থাকলে আসল email (MailKit), নইলে dev fallback: log-এ ছাপে
        var smtpSettings = configuration.GetSection(Email.SmtpSettings.SectionName).Get<Email.SmtpSettings>()
            ?? new Email.SmtpSettings();
        if (string.IsNullOrEmpty(smtpSettings.Host))
        {
            services.AddSingleton<IEmailSender, Email.LoggingEmailSender>();
        }
        else
        {
            services.AddSingleton(smtpSettings);
            services.AddSingleton<IEmailSender, Email.SmtpEmailSender>();
        }

        // Google Sign-In — ID token validation (ClientId env/user-secrets-এ)
        services.Configure<Auth.GoogleAuthSettings>(configuration.GetSection(Auth.GoogleAuthSettings.SectionName));
        services.AddSingleton<IGoogleIdTokenValidator, Auth.GoogleIdTokenValidator>();

        // reset-password email link-এর SPA base URL
        services.AddSingleton(
            configuration.GetSection(Application.Common.Settings.FrontendSettings.SectionName)
                .Get<Application.Common.Settings.FrontendSettings>() ?? new Application.Common.Settings.FrontendSettings());

        // LLM observability — usage/cost record (non-blocking queue → background persist)
        services.AddSingleton<Observability.UsageQueue>();
        services.AddSingleton<ILlmUsageRecorder, Observability.UsageRecorder>();
        services.AddHostedService<Observability.UsagePersistenceService>();
        services.AddScoped<IUsageReader, Observability.UsageReader>();

        // Semantic cache (Qdrant) — near-duplicate প্রশ্নে LLM call এড়ায়
        services.Configure<Cache.SemanticCacheSettings>(configuration.GetSection(Cache.SemanticCacheSettings.SectionName));
        services.AddSingleton<ISemanticCache, Cache.QdrantSemanticCache>();

        // LLM providers — config-driven (key user-secrets), HttpClient factory দিয়ে।
        // Timeout 5min: local model (ollama, CPU) ধীর — বিশেষত qwen-এর মতো thinking model লম্বা
        // reasoning চালায়; default 100s-এ 500 হতো। hosted দ্রুত, তাই এই বড় timeout ওদের ক্ষতি করে না।
        services.Configure<Llm.LlmSettings>(configuration.GetSection(Llm.LlmSettings.SectionName));
        services.AddHttpClient("llm", c => c.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<IChatProviderResolver, Llm.ChatProviderResolver>();
        services.AddSingleton<ILocalModelCatalog, Llm.OllamaModelCatalog>();

        // Embedding provider — config-driven (default gemini)। ollama = local, key ছাড়া।
        // ⚠️ embedding বদলালে vector space বদলায় → RAG/cache collection isolate হয় (নিচে suffix)।
        var embeddingProvider = configuration.GetSection(Llm.LlmSettings.SectionName)
            .GetSection("Embedding")["Provider"] ?? "gemini";
        if (embeddingProvider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IEmbeddingProvider, Llm.OllamaEmbeddingProvider>();
        else
            services.AddSingleton<IEmbeddingProvider, Llm.GeminiEmbeddingProvider>();

        // Model routing — auto mode-এ complexity দেখে cheap/strong provider select
        services.Configure<Llm.RoutingSettings>(configuration.GetSection(Llm.RoutingSettings.SectionName));
        services.Configure<Llm.FallbackSettings>(configuration.GetSection(Llm.FallbackSettings.SectionName));
        services.AddSingleton<IModelRouter, Llm.HeuristicModelRouter>();

        // Guardrails — LLM input-এ prompt-injection block + PII redact (trust boundary)
        services.Configure<Guardrails.GuardrailsSettings>(configuration.GetSection(Guardrails.GuardrailsSettings.SectionName));
        services.AddSingleton<IPromptGuard, Guardrails.RegexPromptGuard>();

        // Conversation memory — Redis-এ history + long হলে summarize (multi-turn chat)
        services.Configure<Conversation.ConversationSettings>(configuration.GetSection(Conversation.ConversationSettings.SectionName));
        services.AddSingleton<IConversationStore, Conversation.RedisConversationStore>();

        // Evals — RAG grounding eval dataset (app-specific/demo seed)
        services.AddSingleton<IEvalDataset, Evals.RagEvalDataset>();

        // Idempotency — mutating endpoint-এ duplicate request dedup (Redis slot)
        services.Configure<Idempotency.IdempotencySettings>(configuration.GetSection(Idempotency.IdempotencySettings.SectionName));
        services.AddSingleton<IIdempotencyStore, Idempotency.RedisIdempotencyStore>();

        // Agent — tool-calling loop + demo tools (feature-toggle, MaxIterations safety)
        services.AddSingleton(
            configuration.GetSection(Application.Features.Agent.AgentSettings.SectionName)
                .Get<Application.Features.Agent.AgentSettings>() ?? new Application.Features.Agent.AgentSettings());
        services.AddSingleton<ITool, Agent.CurrentTimeTool>();
        services.AddSingleton<ITool, Agent.KnowledgeBaseSearchTool>();
        // Scoped — ErpDbContext scoped, singleton tool ধরে রাখলে captive dependency হতো।
        services.AddScoped<ITool, Agent.GetSalesTool>();
        services.AddScoped<ITool, Agent.GetStockTool>();
        services.AddScoped<ITool, Agent.GetWarrantyTool>();
        // একমাত্র write tool — খসড়া order বানায়, approve করে না (stock অক্ষত)।
        services.AddScoped<ITool, Agent.CreateSalesOrderTool>();
        // Tool RAG — descriptions → আলাদা Qdrant collection; startup + agent sync
        services.AddSingleton<IToolCatalog, Agent.QdrantToolCatalog>();
        services.AddHostedService<Agent.ToolCatalogIndexer>();
        // AsyncLocal per-request doc scores (KnowledgeBaseSearchTool → handler → response)
        services.AddSingleton<IAgentRetrievalTrace, Agent.AgentRetrievalTrace>();

        // RAG retrievers (keyword + embedding + qdrant)
        services.Configure<Rag.QdrantSettings>(configuration.GetSection(Rag.QdrantSettings.SectionName));
        services.AddSingleton<IVectorStore, Rag.QdrantVectorStore>();
        // keyword + embedding concrete-ও রাখি → hybrid এদের সরাসরি inject করে (IRetriever collection-এ
        // hybrid নিজেও থাকে, তাই IEnumerable<IRetriever> নিলে circular; concrete inject সেটা এড়ায়)।
        services.AddSingleton<Rag.KeywordRetriever>();
        services.AddSingleton<Rag.EmbeddingRetriever>();
        services.AddSingleton<IRetriever>(sp => sp.GetRequiredService<Rag.KeywordRetriever>());
        services.AddSingleton<IRetriever>(sp => sp.GetRequiredService<Rag.EmbeddingRetriever>());
        services.AddSingleton<IRetriever, Rag.QdrantRetriever>();
        services.AddSingleton<IRetriever, Rag.HybridRetriever>();
        services.AddSingleton<IRetrieverResolver, Rag.RetrieverResolver>();

        // Audit log — mutating command trail (tenant-scoped)
        services.AddScoped<IAuditLog, Persistence.AuditLog>();

        // Document ingestion (upload → background chunk→embed→upsert)
        services.AddScoped<IDocumentRepository, Persistence.DocumentRepository>();
        services.Configure<Ingestion.IngestionQueueSettings>(configuration.GetSection(Ingestion.IngestionQueueSettings.SectionName));
        services.AddSingleton<IIngestionQueue, Ingestion.RedisStreamIngestionQueue>();
        services.AddHostedService<Ingestion.IngestionBackgroundService>();

        return services;
    }
}
