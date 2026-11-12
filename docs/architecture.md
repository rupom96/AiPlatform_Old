# Architecture (বাংলা)

> এক পাতায় পুরো সিস্টেম — স্তর, নির্ভরতার দিক, আর দুটো গুরুত্বপূর্ণ request-এর যাত্রা।
> Diagram গুলো mermaid — GitHub-এ নিজে থেকেই রেন্ডার হয়।

---

## ১. Big picture — কে কার সাথে কথা বলে

```mermaid
flowchart LR
    subgraph client["Client"]
        FE["React SPA<br/>Vite + Redux Toolkit + RTK Query<br/>:5173"]
        MCPC["MCP client<br/>(Claude Desktop / Inspector)"]
    end

    subgraph host["Backend (.NET 10)"]
        API["AiPlatform.Api<br/>:5076 (dev) / nginx :8080 (full)"]
        BG["Background workers<br/>Ingestion (Redis Streams) + Usage persist"]
    end

    subgraph data["State"]
        SQL[("SQL Server<br/>users · documents · usage · audit")]
        REDIS[("Redis<br/>refresh tokens · rate limit ·<br/>idempotency · conversations · streams")]
        QDRANT[("Qdrant<br/>RAG vectors · semantic cache")]
    end

    subgraph external["External"]
        LLM["LLM providers<br/>gemini / groq / mistral"]
        OTEL["Aspire dashboard<br/>OTLP :4317 → UI :18888"]
    end

    FE -->|"REST + SSE"| API
    MCPC -->|"/sse (MCP)"| API
    API --> SQL & REDIS & QDRANT
    API -->|HTTPS| LLM
    BG --> SQL & REDIS & QDRANT
    BG -->|embeddings| LLM
    API & BG -.->|traces·metrics·logs| OTEL
```

- **dev মোড:** `docker compose up -d` → শুধু state/external container; API আর frontend তুমি চালাও।
- **full মোড:** `docker compose --profile full up -d --build` → + api×2 + nginx LB (:8080)। API stateless — সব state SQL/Redis/Qdrant-এ, তাই যেকোনো replica যেকোনো request নিতে পারে।

---

## ২. Clean Architecture — নির্ভরতার দিক

```mermaid
flowchart TD
    API["<b>Api</b><br/>controllers · filters (rate-limit, idempotency) ·<br/>JWT setup · Swagger · OTel · MCP server"]
    APP["<b>Application</b><br/>features (CQRS: Command/Query + Validator + Handler) ·<br/>interfaces (IChatProvider, IVectorStore, IRetriever…) ·<br/>MediatR pipeline: Validation → Audit"]
    DOM["<b>Domain</b><br/>entities (Document, LlmUsage, AuditEntry…) ·<br/>কোনো package নির্ভরতা নেই"]
    INF["<b>Infrastructure</b><br/>EF Core · Identity · Redis · Qdrant ·<br/>LLM providers · queues · implementations"]

    API --> APP --> DOM
    INF --> APP
    INF --> DOM
```

নিয়ম দুটোই: **ভেতরের স্তর বাইরের কাউকে চেনে না** (Domain কাউকে না, Application শুধু Domain-কে), আর **interface Application-এ, implementation Infrastructure-এ** — তাই hosted LLM → local LLM বদলানো মানে শুধু নতুন implementation, কোনো handler বদলায় না (north star)।

---

## ৩. Request flow — RAG query (synchronous পথ)

```mermaid
sequenceDiagram
    participant U as React SPA
    participant A as Api (controller)
    participant P as MediatR pipeline
    participant H as RagQueryHandler
    participant R as Retriever (hybrid)
    participant Q as Qdrant
    participant L as LLM provider

    U->>A: POST /api/v1/rag/query (Bearer JWT)
    A->>A: authn (JWT) · rate limit (Redis)
    A->>P: RagQueryCommand
    P->>P: ValidationBehavior (FluentValidation)
    P->>H: handle
    H->>R: retrieve(question)
    R->>Q: vector search (+ keyword, RRF merge)
    Q-->>R: top-k chunks
    H->>L: grounded prompt (English structure, বাংলা উত্তর)
    L-->>H: answer + citations [n]
    H-->>U: answer + sources
    Note over H,L: প্রতি LLM call → UsageQueue → DB (cost/token)<br/>পুরো পথটাই এক OTel trace — span-এ Qdrant/LLM আলাদা দেখা যায়
```

---

## ৪. Request flow — document upload → ingestion (asynchronous পথ)

```mermaid
sequenceDiagram
    participant U as Client
    participant A as Api
    participant S as SQL Server
    participant RS as Redis Stream
    participant W as Ingestion worker
    participant L as Embedding (gemini)
    participant Q as Qdrant

    U->>A: POST /documents (.txt/.md, Idempotency-Key)
    A->>S: Document row (Pending, TenantId stamp)
    A->>RS: XADD documentId
    A-->>U: 202 Accepted + documentId
    Note over U,A: client এখানেই মুক্ত — polling: GET /documents/{id}

    RS->>W: XREADGROUP (consumer group)
    W->>S: status = Processing
    loop প্রতি chunk
        W->>L: embed(chunk)
        W->>Q: upsert (deterministic point-id)
        W->>S: ProcessedChunks++
    end
    W->>S: status = Completed
    W->>RS: XACK
    Note over RS,W: crash হলে ack হয় না → XAUTOCLAIM reclaim →<br/>idempotent reprocess (একই point-id overwrite) = at-least-once
```

---

## ৫. Auth — token দুটো কোথায় থাকে

| Token | কোথায় | আয়ু | কেন |
|---|---|---|---|
| **Access (JWT)** | frontend মেমোরি (Redux) — localStorage **নয়** | ১৫ মিনিট | XSS চুরি করতে পারে না; ছোট আয়ু = ফাঁস হলেও ক্ষতি সীমিত |
| **Refresh** | httpOnly cookie (`Path=/api/v1/auth`) | ৭ দিন, প্রতি ব্যবহারে rotate | JS পড়তেই পারে না; reuse ধরা পড়লে পুরো family বাতিল |

Page reload → মেমোরি খালি → `AppBootstrap` চুপচাপ `/auth/refresh` (cookie সহ) → নতুন access token। এই chain E2E টেস্টে পাহারা দেওয়া (`frontend/e2e/auth.spec.ts`)।

---

## ৬. Test pyramid

| স্তর | কোথায় | কতগুলো | কী প্রমাণ করে |
|---|---|---|---|
| Unit | `backend/tests/Application.Tests` | 75 | handler/লজিক, সব dependency নকল |
| Integration | `backend/tests/Api.IntegrationTests` | 16 | আসল SQL Server+Redis container-এ পুরো HTTP পথ — query filter, tenant isolation, idempotency |
| E2E | `frontend/e2e` | 3 | আসল ব্রাউজার — CORS, refresh cookie, redirect |

CI (`.github/workflows/ci.yml`): প্রতি push-এ প্রথম দুই স্তর + frontend lint/build। বিস্তারিত: [ci-guide.md](ci-guide.md)।
