# AiPlatform

Production-grade full-stack AI platform — .NET 10 (Clean Architecture) backend + React/TS frontend.

Chat (SSE streaming, conversation memory), structured analyzer, RAG (keyword/embedding/Qdrant/hybrid+RRF, citations),
document ingestion (Redis Streams, at-least-once), multi-provider LLM (routing, semantic cache, guardrails, cost tracking),
JWT auth (refresh rotation + reuse detection), multi-tenancy + audit log, tool-calling agent + MCP server,
OpenTelemetry, Docker load balancing.

> Status: **Phase 8 — Quality/DevEx** done (Phase 9 packaging বাকি)। Roadmap: [PLAN.md](PLAN.md) · progress: [TODO.md](TODO.md)

## Prerequisites
- .NET 10 SDK · Node 22+ · Docker Desktop

## Quick start

```powershell
.\scripts\dev.ps1        # backing services + API + frontend — সব এক কমান্ডে
```

বা হাতে:
```powershell
docker compose up -d                          # sqlserver + redis + qdrant + otel dashboard
dotnet run --project backend/src/Api          # API → http://localhost:5076 (swagger: /swagger)
cd frontend; npm run dev                      # app → http://localhost:5173
```

Full stack (nginx LB + api×2): `docker compose --profile full up -d --build` → http://localhost:8080

## Test

```powershell
.\scripts\test.ps1        # backend unit+integration + frontend lint/build
.\scripts\test.ps1 -E2E   # + Playwright E2E (আসল ব্রাউজার)
```

| স্তর | কী | চালাতে |
|---|---|---|
| Unit (75) | handler logic, mocked deps | `dotnet test backend/AiPlatform.slnx` |
| Integration (16) | আসল SQL Server+Redis container-এ পুরো HTTP পথ | ঐ একই কমান্ডে চলে (Docker লাগে) |
| E2E (3) | আসল ব্রাউজার: auth journey, silent refresh | `cd frontend; npm run e2e` |

CI: [.github/workflows/ci.yml](.github/workflows/ci.yml) — প্রতি push-এ (main + feature/**)। গাইড: [docs/ci-guide.md](docs/ci-guide.md)

## Structure

```
backend/
  src/Api             — controllers, filters, JWT/Swagger/OTel/MCP wiring
  src/Application     — CQRS features, validators, interfaces (এক type = এক ফাইল)
  src/Domain          — entities, শূন্য নির্ভরতা
  src/Infrastructure  — EF Core, Identity, Redis, Qdrant, LLM providers
  tests/              — Application.Tests (unit) · Api.IntegrationTests (Testcontainers)
frontend/
  src/features/       — auth, chat, rag, documents, dashboard, settings (backend আয়নায়)
  e2e/                — Playwright specs
docs/                 — architecture, auth-guide, ci-guide, troubleshooting, docker, secrets
scripts/              — dev.ps1, test.ps1
```

Architecture diagram + request flows: [docs/architecture.md](docs/architecture.md)

## Observability

- **Traces/metrics/logs:** Aspire dashboard → http://localhost:18888 (compose-এ ওঠে; toggle: `Otel:Enabled`)
- **LLM cost/usage:** `GET /api/v1/usage` + in-app dashboard (`/dashboard`)

## Config & secrets

- Local: `dotnet user-secrets` (API keys, connection strings) — **কখনো কোডে/appsettings-এ নয়**। গাইড: [docs/user-secrets-guide.md](docs/user-secrets-guide.md)
- Auth setup/টেস্ট (Google login, remember me, reset email/SMTP): [docs/auth-guide.md](docs/auth-guide.md)
- Copy `.env.example` → `.env` (docker compose + frontend values)
- API test-kit: [backend/src/Api/AiPlatform.http](backend/src/Api/AiPlatform.http) — প্রতিটা endpoint-এর valid/invalid payload + expected response
