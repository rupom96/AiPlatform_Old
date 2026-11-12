# Conventions — এই codebase-এ কাজ করার নিয়ম (বাংলা)

> Boilerplate হিসেবে ব্যবহার করলে এটাই তোমার হাতবই। প্রতিটা নিয়মের **কেন** সহ।
> Architecture-এর ছবি: [architecture.md](architecture.md) · নতুন project বানাতে: [template-guide.md](template-guide.md)

---

## ১. দর্শন — দুটো নিয়ম, আসলে একটাই

1. **সবচেয়ে সরল যেটা কাজ করে।** Speculative abstraction না — এক implementation-এর interface না (LLM/VectorStore-এর মতো সত্যিকার swap-point ছাড়া), "পরে লাগবে" scaffolding না।
2. **Production-grade, নেগোসিয়েবল না।** Trust boundary-তে validation, ব্যর্থতার কথা ভাবা, non-trivial লজিকে টেস্ট।

এ দুটো সাংঘর্ষিক নয় — boring, ছোট, testable কোডই দুটোই দেয়। Over-engineering junior-এর লক্ষণ, sloppiness-ও তাই।

---

## ২. Backend feature যোগ করার রেসিপি

প্রতিটা capability = `Application/Features/<Name>/` এ একটা স্বাধীন feature। ধাপগুলো (উদাহরণ: `Documents`):

```
Application/Features/Documents/
├── Upload/
│   ├── UploadDocumentCommand.cs        ← record, IRequest<Result<T>>
│   ├── UploadDocumentCommandValidator.cs
│   └── UploadDocumentCommandHandler.cs
└── DocumentStatusResponse.cs           ← shared response shape
```

**ধাপ ১ — Command/Query** (এক লাইনের record):
```csharp
public record UploadDocumentCommand(string FileName, string Content) : IRequest<Result<Guid>>;
```

**ধাপ ২ — Validator** (FluentValidation; আলাদা ফাইল):
```csharp
public class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
    }
}
```

**ধাপ ৩ — Handler** (constructor-এ interface নাও, বাস্তবায়ন নয়):
```csharp
public class UploadDocumentCommandHandler(IDocumentRepository repository)
    : IRequestHandler<UploadDocumentCommand, Result<Guid>>
{ ... }
```

**Registration লাগে না** — `AddApplication()` assembly scan করে handler+validator নিজেই তোলে।

**ধাপ ৪ — Controller** (`ApiControllerBase` inherit; Result → HTTP ম্যাপিং ফ্রি):
```csharp
[Authorize]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents")]
public class DocumentsController(IMediator mediator) : ApiControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Upload(...)
        => (await mediator.Send(command, ct)) is { IsSuccess: true } r
            ? Accepted(r.Value) : ToProblem(r.Error);
}
```

**ধাপ ৫ — `src/Api/AiPlatform.http` আপডেট** (নিয়ম, ঐচ্ছিক না): নতুন endpoint-এর valid + invalid payload + `# Expected:` কমেন্ট। এটাই জীবন্ত doc + হাতে-চালানো test-kit।

**ধাপ ৬ — টেস্ট:** handler-এর unit test (`tests/Application.Tests/<Feature>/`) সব dependency NSubstitute দিয়ে; DB/query-filter/HTTP-pipeline ছোঁয়া আচরণ হলে integration test-ও (`tests/Api.IntegrationTests/`)। **নতুন টেস্ট লিখেই একবার ভেঙে দেখো ফেল করে কিনা** — না করলে টেস্টটা কিছু পাহারা দিচ্ছে না।

**নতুন external service লাগলে:** interface → `Application/Common/Interfaces/`, implementation → `Infrastructure/`, register → `Infrastructure/DependencyInjection.cs`। Settings লাগলে section-class (`XxxSettings` + `SectionName` const) + `services.Configure<XxxSettings>(...)`।

---

## ৩. File organization

| নিয়ম | কেন |
|---|---|
| **এক type = এক ফাইল** — Command, Validator, Handler কখনো এক ফাইলে না | diff ছোট থাকে, merge conflict কমে, নাম দেখেই ফাইল পাওয়া যায় |
| Interface `Application`-এ, implementation `Infrastructure`-এ | handler টেস্টযোগ্য থাকে; LLM/DB swap মানে নতুন impl, handler অক্ষত |
| `Domain` কারো উপর নির্ভর করে না — কোনো package-ও না | entity গুলো চিরকাল সরল থাকে |
| Generic কোড আর demo/app-specific কোড মেশাবে না | demo ফাইলে ⚠️ `APP-SPECIFIC DEMO` কমেন্ট — strip তালিকা template-guide.md-তে |

---

## ৪. Error handling — স্তরে স্তরে, try/catch-স্প্যাম না

| ব্যর্থতার ধরন | কে সামলায় | ফল |
|---|---|---|
| **Expected/business** (user-not-found, bad-credentials) | **Result pattern** — handler `Result.Failure(Error.X(...))` ফেরত দেয়, throw না | controller-এর `ToProblem()` → সঠিক status + ProblemDetails |
| **Invalid input** | `ValidationBehavior` (MediatR pipeline) — handler-এ পৌঁছানোর **আগেই** | 400 + field error |
| **Unexpected** (bug, infra down) | `GlobalExceptionHandler` (IExceptionHandler) | 500 + generic ProblemDetails; বিস্তারিত শুধু log-এ — internal leak না |

`try/catch` শুধু সত্যিকার recover-able জায়গায় (LLM call retry, best-effort logout) — প্রতি মেথডে নয়। **Exception হলো ব্যতিক্রমের জন্য; প্রত্যাশিত ব্যর্থতা data, control-flow নয়।**

---

## ৫. Feature toggle convention

Toggle = section-প্রতি `Enabled` bool, `IOptions<XxxSettings>` দিয়ে:

```json
"SemanticCache": { "Enabled": true, "SimilarityThreshold": 0.95 }
```

চালু আছে: `Otel`, `Llm:Routing`, `SemanticCache`, `Idempotency`, `Agent`, `Conversation`, `Guardrails`।

**কখন toggle দেবে না:**
- **Security path** (auth, tenant isolation) — toggle মানে security-তে branch; ভুল কনফিগে চুপচাপ দরজা খোলা। MultiTenancy তাই always-on।
- **Core plumbing** (Serilog, ProblemDetails) — বন্ধ করার বৈধ কারণই নেই।
- Audit-এর মতো per-use জিনিসে opt-in marker (command-এ `IAuditableRequest`) toggle-এর চেয়ে ভালো — কে audit হয় সেটা কোডে পড়া যায়।

---

## ৬. LLM prompt convention — hybrid

**Instruction/structure English, output বাংলা** (free-text field: summary, answer):

```
"Analyze the message and respond ONLY with JSON: {...}. Write the summary value in Bengali."
```

কেন: English instruction-এ JSON/format compliance বেশি reliable; ইউজার বাংলা পড়ে। **Enum value English** (`positive|neutral|negative`) — ওগুলো কোডে match হয়, প্রদর্শনে অনুবাদ frontend-এর কাজ। সব prompt-এ এক নিয়ম: analyzer, RAG, summarizer, agent।

---

## ৭. Frontend feature convention

Backend-এর আয়না — `frontend/src/features/<name>/`:

```
features/documents/
├── documentsApi.ts      ← RTK Query: baseApi.injectEndpoints (নিজস্ব fetch লিখো না)
├── DocumentsPage.tsx    ← route-এ বসা page component
└── components/          ← feature-এর নিজস্ব ছোট অংশ
```

- **সব server-state RTK Query দিয়ে** — `baseApi` re-auth wrapper সহ (401 → silent refresh → retry); নিজে টোকেন সামলিও না।
- Access token **শুধু Redux মেমোরিতে** — localStorage-এ কখনো না (XSS)।
- Form = react-hook-form + zod schema (`schemas.ts`)।
- নতুন page → `App.tsx`-এ route (protected হলে `ProtectedRoute` block-এ)।

---

## ৮. Test pyramid — কোন স্তরে কী

| স্তর | কী টেস্ট করবে | কী করবে না |
|---|---|---|
| **Unit** (`Application.Tests`) | handler লজিক, validator নিয়ম, pure helper (chunker, scorer, fusion) | DB/Redis/HTTP — mock করো |
| **Integration** (`Api.IntegrationTests`) | পুরো HTTP পথ আসল SQL Server+Redis container-এ: query filter, tenant isolation, auth pipeline, idempotency | LLM call (টেস্ট provider ছোঁয় না — ইচ্ছাকৃত: দ্রুত, ফ্রি, offline) |
| **E2E** (`frontend/e2e`) | ব্রাউজার-নির্ভর সত্য: cookie, CORS, redirect, silent refresh | business logic (নিচের স্তরে সস্তা) |

নিয়ম: **যে জিনিস নিচের স্তরে ধরা যায়, সেটা উপরের স্তরে টেস্ট কোরো না** — উপরের স্তর ধীর আর ভঙ্গুর, শুধু তার একচেটিয়া জিনিসের জন্য।

---

## ৯. Doc নিয়ম

- Doc feature-এর **সাথে** যায়, পরে না। ভুল doc-এর চেয়ে doc না-থাকা ভালো।
- Env/machine hack → `docs/troubleshooting.md`-এ entry (লক্ষণ→কারণ→ফিক্স→কেন)।
- Trade-off-ওয়ালা সিদ্ধান্ত → কোড কমেন্টে **কেন** লেখো (কী লেখা আছে কোডেই)।
- ইচ্ছাকৃত shortcut → `// ponytail:` কমেন্টে ceiling + upgrade path।

## ১০. Don'ts

- Secret কোডে/appsettings-এ না — user-secrets/env only। `.env` commit না।
- `Domain` কোনো layer/package-এর উপর নির্ভর করবে না।
- Auto-commit/push না — মানুষ চালায়।
- Phase-এর বাইরে scope না — ছোট increment, checkpoint-এ থামো।
