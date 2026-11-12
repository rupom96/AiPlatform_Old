# MyApp

<!-- এটা নতুন project-এর starter README। ব্যবহার: template-guide.md-র rename+strip শেষে
     এই ফাইলকে README.md বানাও (পুরনোটা মুছে), MyApp → নিজের নাম, TODO গুলো ভরো। -->

**MyApp** — production-grade AI platform: .NET 10 Clean Architecture backend + React/TS frontend.
Built on the AiPlatform boilerplate — auth (JWT + refresh rotation), multi-provider LLM (routing,
semantic cache, guardrails, cost tracking), RAG (Qdrant, hybrid retrieval, citations), document
ingestion (Redis Streams), multi-tenancy + audit, tool-calling agent + MCP server, OpenTelemetry.

> TODO: এক লাইনে লেখো এই app-টা আসলে কী করে — boilerplate কী দেয় তা নয়।

## Prerequisites
- .NET 10 SDK · Node 22+ · Docker Desktop

## Quick start

```powershell
# 1. secrets (একবার)
cd backend/src/Api
dotnet user-secrets set "Jwt:SecretKey" "<64+ char random>"
dotnet user-secrets set "Llm:Providers:gemini:ApiKey" "<key>"
cd ../../..
copy .env.example .env    # SA_PASSWORD, JWT_SECRET ভরো

# 2. সব চালু
.\scripts\dev.ps1
```

- App: http://localhost:5173 · Swagger: http://localhost:5076/swagger · Telemetry: http://localhost:18888

## Test

```powershell
.\scripts\test.ps1        # unit + integration (Docker লাগে) + frontend lint/build
.\scripts\test.ps1 -E2E   # + Playwright (আসল ব্রাউজার)
```

## Docs

| ফাইল | কী |
|---|---|
| [docs/architecture.md](docs/architecture.md) | স্তর, নির্ভরতার দিক, request flow (mermaid) |
| [docs/conventions.md](docs/conventions.md) | নতুন feature কীভাবে যোগ করবে — রেসিপি + নিয়ম |
| [docs/ci-guide.md](docs/ci-guide.md) | CI কীভাবে কাজ করে |
| [docs/troubleshooting.md](docs/troubleshooting.md) | env/machine সমস্যার ফিক্স |
| [backend/src/Api/*.http](backend/src/Api/) | জীবন্ত API test-kit — প্রতি endpoint-এর valid/invalid + expected |

## Feature toggles

`appsettings.json` — section-প্রতি `Enabled`: `Otel`, `Llm:Routing`, `SemanticCache`,
`Idempotency`, `Agent`, `Conversation`, `Guardrails`। কোনটা কী: [docs/conventions.md](docs/conventions.md) §৫।

> TODO: যে toggle গুলো এই app-এ ব্যবহার করছ না, বন্ধ করে দাও — চালু-কিন্তু-অব্যবহৃত feature মানে অকারণ attack surface + খরচ।
