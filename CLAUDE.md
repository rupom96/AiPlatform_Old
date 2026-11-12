# CLAUDE.md — AiPlatform

## Stack
- Backend: .NET 10 Web API, Clean Architecture (Api → Application → Domain; Infrastructure → Application/Domain).
- CQRS + MediatR, FluentValidation, Serilog, ProblemDetails, Swagger, API versioning (`/api/v{n}`).
- Frontend (Phase 4+): React + TS + Vite, Tailwind + shadcn/ui, react-hook-form + zod, framer-motion, sonner, recharts. **State/data: Redux Toolkit + RTK Query** (server-state + client-state). Silent refresh via RTK Query custom baseQuery (re-auth wrapper); access token in memory (Redux slice), never localStorage.

## Frontend design system (premium overhaul, 2026-07)
- **Theme**: violet/blue soft-gradient tokens `index.css`-এ (light+dark আলাদা মান); radius 1rem = rounded-2xl default।
- **Glassmorphism**: সব shadcn `Card` স্বয়ংক্রিয়ভাবে glass — `[data-slot=card]` global rule (translucent `--card` + backdrop-blur)। নতুন surface-এ আলাদা glass class লাগে না, Card ব্যবহার করলেই হয়।
- **Layout**: `AppShell` = glass sidebar (lg+, icon nav + active pill + user footer) / mobile header+nav strip; `AnimatedBackground` = gradient mesh + particles (CSS-only, reduced-motion-safe) — AppShell-এ একবারই রাখা।
- **Motion**: page transition AppShell-এ (AnimatePresence, pathname key); card entrance = ছোট stagger fade+rise। নতুন page-এ আলাদা transition লাগবে না।
- **Charts (recharts)**: রং সবসময় `var(--chart-1..4)` (dataviz-validated, light+dark আলাদা) — নতুন hex বানাবে না; সব chart-এ `isAnimationActive={false}` (rAF-throttled env-এ আটকায়, troubleshooting §৩); chart-এর পাশে table/legend রাখো (accessibility relief)।
- **Loading**: spinner নয় — `ui/skeleton.tsx` দিয়ে layout-ধরে-রাখা skeleton।
- নতুন UI বানালে এই নিয়মগুলোই ধরো — নিজে নতুন style উদ্ভাবন করবে না।
- Data: SQL Server + EF Core, Redis, Qdrant. Solution file is `AiPlatform.slnx` (not .sln).

## Repo structure (from Phase 4)
- `backend/` — .NET solution (`src/`, `tests/`, `AiPlatform.slnx`). Run: `dotnet run --project backend/src/Api`.
- `frontend/` — React app, feature-based (`src/features/{auth,chat,rag,documents}`), mirrors backend feature organization.
- Root: `docs/`, `docker-compose.yml`, `PLAN.md`, `TODO.md`, `CLAUDE.md`.

## Run
- Build: `dotnet build backend/AiPlatform.slnx`
- API: `dotnet run --project backend/src/Api` → http://localhost:5076 (swagger `/swagger`, health `/health`)
- Test: `dotnet test backend/AiPlatform.slnx` (unit + integration; integration-এ Docker লাগে — Testcontainers)
- E2E: `cd frontend && npm run e2e` (Playwright; stack নিজে তোলে)
- Frontend: `cd frontend && npm run dev` → http://localhost:5173
- EF migration: `dotnet ef ... --project backend/src/Infrastructure --startup-project backend/src/Api`
- সব একসাথে: `.\scripts\dev.ps1` (চালু) / `.\scripts\test.ps1 [-E2E]` (টেস্ট)
- সার্ভার bootstrap (Windows Server 2022 — Docker Desktop চলে না, তাই WSL2+Docker CE): `scripts/server-setup.ps1`
- Deploy (সার্ভারে source ছাড়া, Docker): `release.bat` double-click → দুই image GHCR-এ push; সার্ভারে শুধু `deploy/` এর ৩টা ফাইল। **ধাপে ধাপে runbook: `docs/deploy-docker.md`** (DEV vs SERVER আলাদা করে লেখা, সব মান হুবহু)
- Deploy artifact (IIS, Docker ছাড়া): `publish.bat` double-click (বা `.\scripts\publish.ps1 -ApiBaseUrl https://... [-NoPause]`) → `publish\api` (IIS-ready backend) + `publish\web` (frontend dist + SPA-fallback web.config)। ⚠️ `VITE_API_BASE_URL` build-time, তাই URL বদলালে আবার publish করতে হবে।
- Agent mode হাতে পরীক্ষা করতে ২০টা প্রশ্ন (ছয়টা tool-ই ঢাকে, প্রত্যাশিত ফল সহ): `docs/agent-test-questions.txt`
- RAG page-এর ৩৮টা প্রশ্ন (তথ্য + abstention + RAG/ERP সীমানা + কঠিন কেস): `docs/rag-test-questions.txt`
- ERP sample data বাড়াতে (ErpSeeder জানুয়ারি→জুলাই ২০২৬-এ থেমে আছে): `dotnet run backend/tools/seed-erp-range.cs [from yyyy-MM] [to yyyy-MM] [--dry-run]` — মাস-ভিত্তিক idempotent, stock `ErpStockService.Approve*` দিয়েই বদলায়
- Telemetry: Aspire dashboard http://localhost:18888 (toggle `Otel:Enabled`)
- Env/machine সমস্যা (port 1433, locked exe…): আগে `docs/troubleshooting.md` দেখো; নতুন env hack বেরোলে ওখানেই entry যোগ করো
- UI থেকে সাময়িকভাবে লুকানো জিনিস (Dashboard, cerebras/mistral provider) ও ফেরানোর নিয়ম: `docs/hidden-features.md` — নতুন কিছু লুকালে ওখানেই entry যোগ করো
- নতুন মেশিনে এই project দাঁড় করাতে: `docs/second-device-setup.md` (secrets, `.env`, Claude memory — git যা আনে না)

## Conventions
- প্রতিটা capability = আলাদা, স্বাধীনভাবে-testable feature (`Application/Features/<Name>`)।
- **এক type = এক ফাইল।** Command/Query, Validator, Handler আলাদা ফাইলে (এক ফাইলে জড়ো নয়)।
- **LLM prompt = hybrid:** instruction/structure **English** (format/JSON বেশি reliable), কিন্তু explicit বলো output **বাংলায়** দিতে (free-text field: summary, answer ইত্যাদি বাংলায়)। enum value English। analyzer/RAG/future সব prompt এই নিয়মে।
- Generic reusable code ও app-specific/demo code আলাদা রাখো (boilerplate goal — PLAN §4)।
- Interface Application-এ, implementation Infrastructure-এ (IChatProvider, IVectorStore…)।
- নতুন external service DI: `Infrastructure/DependencyInjection.cs`।
- **নতুন API endpoint বানালেই `src/Api/AiPlatform.http` update করো** — প্রতিটার জন্য valid + invalid payload + `# Expected:` comment (কী response ও কেন)। এটাই live test-kit + doc।
- **বাংলা লেখা `.ps1` ফাইল অবশ্যই UTF-8 **BOM সহ**।** BOM ছাড়া Windows PowerShell 5.1 ফাইলটাকে ANSI ভেবে পড়ে → বাংলা ভেঙে যায় → `Unexpected token '}'` parser error (pwsh 7-এ ধরা পড়ে না)। `.bat` wrapper-এ `chcp 65001` + pwsh থাকলে pwsh।

## Error handling — layered, no try/catch spam
- Expected/business failure (user-not-found, bad-credentials) → **Result pattern**, throw করবে না।
- Invalid input → MediatR `ValidationBehavior` (FluentValidation) → 400, handler-এর আগেই।
- Unexpected (bug/infra) → global `IExceptionHandler` → ProblemDetails; type→status map (Validation→400, NotFound→404, else 500 + log, internal detail leak না)।
- try/catch শুধু সত্যিকার recover-able জায়গায় (LLM/DB call retry) — প্রতি block-এ নয়।

## Don'ts
- কোনো API key/secret কোডে বা appsettings-এ hardcode করবে না — user-secrets / env only.
- Domain কোনো package/layer-এর উপর নির্ভর করবে না।
- over-engineer না — সবচেয়ে সরল যেটা কাজ করে (PLAN থিম)। phase-এর বাইরে scope নেবে না।

## Quality bar — FAANG-level, non-negotiable
সব code/query/function industry-standard, production-grade হতে হবে:
- সরল ও readable > clever; boring code জেতে। over-engineer = junior signal।
- সঠিক naming, single responsibility, small functions, testable units।
- Error handling + input validation trust boundary-তে; edge case ভাবা।
- Security ও performance সচেতন, কিন্তু premature optimization না।
- প্রতি non-trivial logic-এ test। magic number/string না।
- এটা "simplest thing that works" থিমের সাথে সাংঘর্ষিক নয় — একই জিনিস।

## Guide style
- ব্যাখ্যা বাংলায়, beginner-friendly; কোড/identifier/commit ইংরেজিতে।
- ছোট increment, প্রতি phase checkpoint-এ থেমে user-এর "go" নাও।

## Plan
পূর্ণ roadmap: `PLAN.md`, অগ্রগতি: `TODO.md`. এখন শেষ: **Phase 8 (Quality/DevEx)** — বাকি Phase 9 (boilerplate packaging).
