# Template Guide — এই repo থেকে নতুন project (বাংলা)

> তিন ধাপ: **rename → demo strip → verify**। প্রতি ধাপের শেষে build+test সবুজ রেখে এগোও।
> `dotnet new` template দিয়ে ধাপ ১ স্বয়ংক্রিয় হয় — [নিচে §৫](#৫-dotnet-new-template-সবচেয়ে-সহজ-পথ)। হাতে করলে ধাপগুলো এই।

---

## ১. Rename checklist

`AiPlatform` নামটা কোথায় কোথায় আছে (ধরি নতুন নাম `MyApp`):

| কী | কোথায় | কীভাবে |
|---|---|---|
| **Namespace + project + slnx** | ১৮৮ ফাইলের `namespace AiPlatform.*`, csproj নাম, `AiPlatform.slnx` | পুরো repo-তে case-sensitive whole-word find-replace `AiPlatform` → `MyApp`, তারপর ফাইল/ফোল্ডার rename (`AiPlatform.Api.csproj` → `MyApp.Api.csproj` ইত্যাদি) |
| **UserSecretsId** | `src/Api/*.csproj` | **নতুন GUID দাও** — পুরনোটা রাখলে দুই project এক secrets store শেয়ার করবে (বিভ্রান্তির উৎস) |
| **JWT Issuer/Audience** | `appsettings.json` → `"Jwt"` | `MyApp` / `MyApp.Client` — পুরনো token নতুন app-এ চলবে না, সেটাই কাম্য |
| **DB নাম** | connection string (`Database=AiPlatform`) — appsettings.Development.json + compose | `Database=MyApp` |
| **Docker নাম** | `docker-compose.yml` — `container_name: aiplatform-*` ×৬ | `myapp-*` (না বদলালে দুই project-এর container ধাক্কা খাবে) |
| **Qdrant collection** | appsettings → `"Qdrant:Collection": "aiplatform_rag"` | `myapp_rag` — না বদলালে পুরনো project-এর vector-এর সাথে মিশবে |
| **Frontend brand** | `AppShell.tsx`, `AuthPage.tsx` (×২), tab title | নিজের নাম/কপি |
| **Git remote** | `.git` | নতুন repo বানাও; এই history রাখবে কি না তোমার সিদ্ধান্ত |

Rename-এর পরে: `dotnet build backend/MyApp.slnx` + `dotnet test` — **সবুজ না হলে demo strip-এ যেও না।**

---

## ২. Demo strip — কী মুছবে, কী বদলাবে, কী ভাঙবে

⚠️ চিহ্নিত সব ফাইলে `APP-SPECIFIC DEMO` কমেন্ট আছে — এই টেবিল সেগুলোরই কর্ম-তালিকা।

### মুছে ফেলো (একসাথে, একই commit-এ)

| ফাইল | সাথে যায় | আর কেউ ব্যবহার করে না — যাচাই করা |
|---|---|---|
| `Application/Features/Analyzer/` (৪ ফাইল) | `Api/Controllers/AnalyzerController.cs`, `Api/Contracts/AnalyzeRequests.cs`, `tests/.../Analyzer/AnalyzeCommandHandlerTests.cs`, `AiPlatform.http`-এর analyze section | ✔ self-contained |
| `Api/Controllers/PingController.cs` | `tests/Api.IntegrationTests/SmokeTests.cs`-এর `Ping_ReturnsPong` test | health check থাকছেই, ping শুধু Phase-0 demo |

### Content বদলাও (ফাইল মুছo না — অন্যরা নির্ভর করে)

| ফাইল | কে নির্ভর করে | কী করবে |
|---|---|---|
| `Infrastructure/Rag/KnowledgeBaseSeed.cs` | KeywordRetriever, EmbeddingRetriever, QdrantRetriever, KnowledgeBaseSearchTool, RagEvalDataset — **৫ জায়গা** | `Documents` list-এ নিজের domain-এর জ্ঞান বসাও; বা খালি list `[]` — তখন RAG শুধু আপলোড করা document থেকে উত্তর দেবে |
| `Infrastructure/Evals/RagEvalDataset.cs` | `RunEvalsCommandHandler` (IEvalDataset দিয়ে) | নিজের KB-র প্রশ্ন+expected keyword দিয়ে case লেখো — evals feature-টা generic, case-গুলোই শুধু demo |
| `Llm:Routing:ComplexKeywords` + `Llm:Pricing` (appsettings) | ModelRouter, UsageRecorder | নিজের ভাষা/model অনুযায়ী টিউন করো |

### সিদ্ধান্ত তোমার

| ফাইল | রাখলে | মুছলে |
|---|---|---|
| `Infrastructure/Agent/CurrentTimeTool.cs` | tool-calling-এর জীবন্ত উদাহরণ (generic) | `DependencyInjection.cs`-এর ১ লাইন + `Api/Mcp/AiPlatformMcpTools.cs`-এর method + `RunAgentCommandHandlerTests`-ও মোছো (test এটা new করে) |
| `Infrastructure/Agent/KnowledgeBaseSearchTool.cs` | নিজের KB-তে repoint করো | DI লাইন + MCP bridge method মোছো |

---

## ৩. নতুন project-এর secrets

```powershell
cd backend/src/Api
dotnet user-secrets set "Jwt:SecretKey" "<৬৪+ অক্ষরের random string>"
dotnet user-secrets set "Llm:Providers:gemini:ApiKey" "<key>"   # যে provider ব্যবহার করবে
```
আর `.env.example` → `.env` কপি করে `SA_PASSWORD`, `JWT_SECRET` ভরো (compose-এর জন্য)।
**কখনোই** কোডে/appsettings-এ না — নিয়ম: [conventions.md](conventions.md) §১০।

---

## ৪. Verify — strip-এর পরে প্রমাণ

```powershell
.\scripts\test.ps1          # build + 91−(মুছে দেওয়া test) সবুজ হতে হবে
.\scripts\test.ps1 -E2E     # auth journey অক্ষত (demo strip auth ছোঁয় না)
.\scripts\dev.ps1           # চালিয়ে swagger-এ দেখো: /analyze আর /ping নেই, বাকি সব আছে
```

মুছে দেওয়া test-এর হিসাব: Analyzer ~৩টা, Ping ১টা, (Agent tools মুছলে আরও ~৩) — সংখ্যা না মিললে কোথাও আধখানা strip হয়েছে।

---

## ৫. `dotnet new` template (সবচেয়ে সহজ পথ)

Repo root-এ `.template.config` থাকলে (Phase 9d):

```powershell
dotnet new install <এই repo-র path বা package>
dotnet new aiplatform -n MyApp
```

`-n MyApp` নিজেই §১-এর namespace/slnx/csproj rename করে দেয়। তারপরও §২ (demo strip), §৩ (secrets) হাতে — ওগুলো সিদ্ধান্ত, যান্ত্রিক কাজ নয়।
