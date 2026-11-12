# PLAN.md — Production-Grade Full-Stack AI Platform

> Solution root: `D:\Saiful\AiPlatform` (নামটা placeholder — boilerplate হিসেবে rename করা যাবে, দেখো §7)
> Backend: **.NET 10** Web API (Clean Architecture + CQRS/MediatR)
> Frontend: **React + TypeScript (Vite)** — আলাদা project
> ভাষা: গাইড বাংলায়, কোড/identifier/commit ইংরেজিতে।

---

## North Star — কোথায় যাচ্ছি
এখন **very basic** দিয়ে শুরু (hosted LLM API, in-memory/simple)। কিন্তু লক্ষ্য একটা **production-level product**। ধাপে ধাপে যোগ হবে: **local/self-hosted LLM** (Ollama/vLLM ইত্যাদি), **MCP server + tool-agent**, এবং প্রয়োজনমতো আরও infra। তাই প্রতিটা abstraction (`IChatProvider`, `IVectorStore`, feature toggle) এমনভাবে বানাব যেন পরে hosted→local, বা নতুন capability plug করা সহজ — কোড rewrite ছাড়া। "basic এখন" মানে "সরল", "স্থায়ীভাবে ছোট" নয়।

## 0. এই ডকুমেন্ট কী

তিনটা জিনিস এক জায়গায়:
1. **Requirements** — 4টা md ডকুমেন্ট + তোমার prompt থেকে সার করা।
2. **Migration map** — demo-র কোন feature নতুন architecture-এ কোথায় যাবে।
3. **Phased build plan** — checkpoint সহ, প্রতিটা phase-এ তোমার "go" লাগবে।

এটা approve করার আগে **কোনো কোড লিখব না।**

---

## 1. Requirements — কোথা থেকে এলো

### 1a. তোমার prompt (primary app-spec)
নীচের feature-গুলো বাধ্যতামূলক (তোমার লেখা থেকে সরাসরি):
- Chat (multi-provider), structured analyzer, RAG (keyword/embedding/chunking/Qdrant)।
- Auth: Identity + JWT access (in-memory) + refresh cookie (rotation, Redis, reuse-detection)।
- SQL Server + EF Core, Redis (cache/rate-limit/refresh store), Qdrant (interface-এর পিছনে)।
- Cross-cutting: Serilog+correlation-id, FluentValidation, ProblemDetails, Swagger+JWT, CORS, health checks, API versioning।
- Docker + docker-compose (api ×2, nginx LB, redis, qdrant, sqlserver)।
- Premium React auth UI (Tailwind+shadcn/rhf+zod/framer/sonner)।
- Reusable boilerplate + feature toggles।

### 1b. 4টা md — architecture দর্শন (source of truth, কিন্তু concept-level)
এগুলো "এই feature বানাও" বলে না; এগুলো **কীভাবে ভাবতে হবে** শেখায়। প্রতিটা থেকে যে নীতি আমরা কোডে প্রয়োগ করব:

| ডকুমেন্ট | মূল নীতি | আমাদের app-এ প্রয়োগ |
|---|---|---|
| 01 AI Orchestration | "constrain the non-determinism"; workflow > agent; observability precondition; cost first-class; semantic cache; model routing; guardrails; idempotency on side-effects | LLM abstraction, semantic cache, model routing, cost/token logging, guardrails, citations — সব আলাদা toggleable feature |
| 02 Distributed Systems | idempotency key, at-least-once + idempotent processing, cache-aside, circuit breaker, stateless service + external state, outbox | stateless API + Redis state → LB replicas; idempotency keys; circuit breaker on provider calls; retry+backoff |
| 03 System Architecture | coupling/cohesion, DDD boundary, "simplest thing that works", evolutionary architecture | Clean Architecture layer boundary; generic vs app-specific split; feature toggles |
| 04 Product Thinking | premature complexity = debt; build only what earns value; phase by value | phase order: core যা value দেয় আগে; advanced পরে; প্রতি phase checkpoint |

> **মূল থিম (চারটাতেই মেলে):** সবচেয়ে সরল system যেটা কাজ করে সেটাই সঠিক। তাই আমরা over-engineer করব না — phase-by-phase, প্রতিটা feature আলাদা ও testable।

---

## 2. Migration map — demo → production

| Demo (`Program.cs`) | নতুন জায়গা | পরিবর্তন |
|---|---|---|
| `/ask` multi-provider | `Application/Features/Chat` + `IChatProvider` | provider config-driven, key secrets-এ, streaming যোগ |
| `/analyze` JSON→object | `Application/Features/Analyzer` | typed result, FluentValidation, DB-তে save option |
| `/rag` keyword | `Application/Features/Rag/Keyword` | feature হিসেবে আলাদা |
| `/rag-embed`, `/rag-chunk` | `Application/Features/Rag/Embedding` + chunking service | `IEmbeddingProvider` abstraction |
| `/rag-qdrant` | `Infrastructure/Vector/QdrantStore : IVectorStore` | interface-এর পিছনে, upsert background job-এ |
| hardcoded KB/chunks | seed data (isolated, removable — §7) | boilerplate-এ সহজে মুছে ফেলা যায় |
| **hardcoded API keys** | user-secrets / env only | ⚠️ পুরনো key সব revoke করতে হবে |
| `wwwroot/index.html` | আলাদা React app | premium UI |

---

## 3. Target architecture (backend)

```
AiPlatform.sln
├─ src/
│  ├─ Api            → controllers, middleware, DI wiring, Swagger, versioning
│  ├─ Application    → CQRS handlers, MediatR, validators, interfaces (IChatProvider…)
│  ├─ Domain         → entities, value objects, enums (no dependencies)
│  └─ Infrastructure → EF Core, Identity, Redis, Qdrant, LLM providers, Serilog
├─ tests/
│  └─ Application.Tests (xUnit)
└─ (backend/ ও frontend/ — দুই top-level folder, এক repo)
```
Dependency rule: Api → Application → Domain; Infrastructure → Application/Domain। Domain কিছুর উপর নির্ভর করে না।

---

## 4. Generic-reusable vs App-specific (boilerplate-এর জন্য day-1 split)

| Generic (reusable skeleton) | App-specific (demo business) |
|---|---|
| Auth (Identity+JWT+refresh rotation) | customer-message analyzer prompt |
| Redis cache/rate-limit/session | Bangla knowledge-base seed data |
| Serilog + correlation + ProblemDetails | RAG document content |
| LLM provider abstraction (chat+embed) | analyzer output schema (ReviewAnalysis) |
| RAG/Qdrant infrastructure | app-specific UI copy |
| Docker/LB/compose | — |
| Frontend auth + app shell | feature pages content |

App-specific সব এক জায়গায় (`*.Demo` namespace + `seed/` folder) → নতুন project-এ সহজে মুছবে।

---

## 5. Phased build plan (প্রতিটা phase = checkpoint, তোমার "go" লাগবে)

### Phase 0 — Foundation (skeleton, no features)
- Solution + 4 layer + tests project। .gitignore, `.env.example`, README sk, Serilog, ProblemDetails, Swagger, health check, versioning, CORS।
- **Checkpoint:** খালি API run হয়, `/health` + Swagger খোলে।

### Phase 1 — Auth (priority feature)
- Identity + EF Core + SQL Server migration। JWT access + refresh (Redis, rotation, reuse-detection)। endpoints: register/login/refresh/logout/me/forgot/reset। rate-limit auth। xUnit handler test।
- **Checkpoint:** Swagger-এ register→login→protected call→refresh কাজ করে।

### Phase 2 — LLM core (chat + analyzer)
- `IChatProvider`/`IEmbeddingProvider`, config-driven providers (Gemini + OpenAI-compat)। Chat endpoint (+ SSE streaming)। Analyzer feature। secrets-এ key।
- **Checkpoint:** chat + analyze কাজ করে, provider switch হয়।

### Phase 3 — RAG + Qdrant
- keyword / embedding / chunking / Qdrant (`IVectorStore`)। document upload endpoint + background ingestion (chunk→embed→upsert)। citations।
- **Checkpoint:** upload করা doc-এ RAG উত্তর + source chunks দেখায়।

### Phase 4 — Frontend core (`frontend/` folder)
- Vite+TS+Tailwind+shadcn, react-hook-form+zod, framer-motion, sonner। **Redux Toolkit + RTK Query** (state/data)। premium login/register (split-screen, dark mode, Google button placeholder)। silent-refresh via RTK Query custom baseQuery (re-auth), protected routes। chat + RAG + upload UI। feature-based (`src/features/{auth,chat,rag,documents}`), backend feature-এর mirror।
- Repo restructure: `src/`,`tests/`,`.slnx` → `backend/`; frontend → `frontend/`।
- **Checkpoint:** browser থেকে login → chat → RAG পুরো flow।

### Phase 5 — Docker + Load balancing
- Dockerize api। compose: api×2 + nginx LB + redis + qdrant + sqlserver। health checks, graceful shutdown, compose.override (local)।
- **Checkpoint:** `docker compose up` → LB দিয়ে দুই replica-তে request balance।

--- core শেষ। নিচেরগুলো তোমার career-goal (AI Systems Engineer) অনুযায়ী ---

### Phase 6 — AI-specific
semantic cache (Redis, embedding-similarity) → model routing → LLM observability (token/cost/latency + dashboard) → guardrails (prompt-injection/PII) → hybrid search + rerank → conversation memory → evals harness।

### Phase 7 — Infra/product advanced
multi-tenancy + audit log → async queue (Redis Streams) → idempotency keys/pagination/soft-delete → chat history sidebar → admin dashboard → settings/i18n → MCP server + tool-agent।

### Phase 8 — Quality/DevEx
xUnit + integration + Playwright E2E → CI/CD (GitHub Actions) → OpenTelemetry + Seq/Grafana → architecture diagram + run scripts।

### Phase 9 — Boilerplate packaging
`README-TEMPLATE.md`, `docs/conventions`, feature-toggle config, `dotnet new` template, seed strip guide।

---

## 6. Extras — যোগ করছি, কারণ
refresh-rotation, per-user rate-limit, Redis response cache, request/response logging, Swagger JWT, seed data, `.env.example`, README, compose.override — **কারণ:** md 01/02 এগুলোকে production precondition বলছে (observability, cost, idempotency, cache), আর boilerplate-এ এগুলো ছাড়া "production-grade" দাবি ফাঁকা।

## 7. Boilerplate rules (day-1 থেকে)
- Generic vs app-specific §4 split।
- Feature toggle: `appsettings` → `Features:{ Rag, Agent, MultiTenancy }` bool।
- Seed/demo data → এক folder, easily deletable।
- `README-TEMPLATE.md`: কী rename (sln/namespace), নতুন feature কীভাবে add, demo কীভাবে strip।
- Secrets কখনো commit না।

## 7a. Documentation plan (doc = debt, phase অনুযায়ী বাড়ে)
- **Phase 0:** `CLAUDE.md` (আমার instruction, ছোট ২০-৪০ লাইন — stack/run/convention/don'ts), `README.md` (bare), `.env.example`।
- **Phase 0-3 জুড়ে:** `docs/architecture.md` (layer diagram + request flow) feature আসার সাথে বাড়বে; সত্যিকার trade-off সিদ্ধান্তে `docs/adr/` (এক ফাইল = এক decision)।
- **Phase 9:** `README-TEMPLATE.md`, `docs/conventions.md` (structure স্থির হলে)।
- `CHANGELOG.md` optional/পরে। নিয়ম: ভুল doc-এর চেয়ে doc না-থাকা ভালো — feature-এর সাথেই doc।

---

## ⚠️ Action তোমার দিকে (approval-এর আগেই)
demo-র সব leaked API key **revoke/rotate** করো (Gemini, Groq, Cerebras, OpenRouter, Mistral, GitHub PAT)।

## এরপর কী
এই PLAN.md পড়ে বলো — **"go phase 0"** বা কী বদলাতে চাও। Approve না করা পর্যন্ত কোড লিখছি না।
```
