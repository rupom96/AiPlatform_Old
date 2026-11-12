# CI (GitHub Actions) — beginner-friendly (বাংলা)

> এই project-এর CI কী করে, কেন করে, কীভাবে দেখবে ও ঠিক করবে।
> Workflow ফাইল: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml)

---

## ১. CI আসলে কী (intuition)

তোমার এখনকার অভ্যাস:

```
কোড লিখলে → নিজে dotnet build → নিজে dotnet test → সবুজ দেখলে → commit
```

এখানে **তুমিই** যন্ত্র। তিনটা সমস্যা, তিনটাই আসল:

| সমস্যা | কেন ঘটে |
|---|---|
| **ভুলে যাওয়া** | রাত ২টায় ছোট fix → টেস্ট না চালিয়ে commit → ভাঙা কোড main-এ। শৃঙ্খলার অভাব নয়, মানুষের স্বভাব। |
| **"আমার মেশিনে তো চলে"** | তোমার মেশিনে SQL Server চলছে, `.env` আছে, user-secrets-এ key আছে, NuGet cache ভরা। এগুলোর একটাও git-এ নেই। নতুন মেশিনে clone করলে বিল্ডই হবে না — তুমি টেরও পাবে না। |
| **repo-র অবস্থা অজানা** | main এই মুহূর্তে সবুজ না লাল? না চালানো পর্যন্ত জানার উপায় নেই। |

**CI (Continuous Integration) = প্রতিবার কোড push হলে একটা সম্পূর্ণ খালি মেশিন নিজে থেকে clone করে, build করে, টেস্ট চালায়, ফল দেখায়।**

Analogy: CI = **নির্ঘুম QA কর্মচারী** যে প্রতিটা commit-এ একই checklist মেলায়, কখনো ক্লান্ত হয় না, কখনো "এটুকু তো ঠিকই আছে" বলে এড়ায় না।

> **সবচেয়ে বড় উপকারটা টেস্ট চালানো নয় — "খালি মেশিন"।** তোমার ল্যাপটপের কোনো ময়লা ওখানে নেই। কোনো ফাইল commit করতে ভুলে গেলে, বা কোনো dependency শুধু তোমার মেশিনে থাকলে, CI সাথে সাথে ধরে।

---

## ২. ৬টা শব্দ জানলেই শুরু

| শব্দ | মানে | analogy |
|---|---|---|
| **Workflow** | একটা YAML ফাইল = এক সেট অটোমেশন। থাকে `.github/workflows/`-এ | রেসিপি বই |
| **Trigger** (`on:`) | কোন ঘটনায় চলবে (push, PR, বোতাম চাপা) | রান্না কখন শুরু |
| **Job** | স্বাধীনভাবে চলা এক গুচ্ছ কাজ। নিজের আলাদা মেশিন পায় | এক রাঁধুনি |
| **Step** | job-এর ভেতরের এক ধাপ (এক command বা এক action) | রেসিপির এক লাইন |
| **Runner** | যে মেশিনে job চলে (GitHub-এর ভাড়া করা VM) | রান্নাঘর |
| **Action** | অন্যের লেখা পুনঃব্যবহারযোগ্য step (`uses:` দিয়ে ডাকি) | রেডিমেড মসলা |

সম্পর্ক: **Workflow → একাধিক Job (পাশাপাশি) → প্রতিটায় একাধিক Step (একের পর এক)।**

---

## ৩. push করলে ঠিক কী ঘটে

```
তুমি git push করলে
   ↓
GitHub দেখে .github/workflows/ci.yml আছে, আর তার trigger এই push-এর সাথে মেলে
   ↓
GitHub দুটো একদম নতুন Ubuntu VM ভাড়া করে (backend + frontend, পাশাপাশি)
   ↓  ← এই মেশিনে তোমার কিচ্ছু নেই। খালি। Docker + সাধারণ tool ছাড়া।
VM: repo clone → .NET/Node বসায় → cache ফেরায় → build → test
   ↓
কোনো step ফেল (exit code ≠ 0) → পুরো job লাল ❌ → বাকি step বাতিল → email
সব পাস → সবুজ ✅
   ↓
VM ধ্বংস। পরের run আবার নতুন খালি VM।
```

> **মূল নিয়ম: step সফল কিনা ঠিক হয় exit code দিয়ে।** `0` = সফল, বাকি সব = ফেল। তাই `dotnet test` ফেল করলে CI নিজে থেকেই লাল — আলাদা করে কিছু বলতে হয় না।

---

## ৪. আমাদের `ci.yml` — লাইন ধরে

### Trigger

```yaml
on:
  push:
    branches: [main, 'feature/**']
  workflow_dispatch:
```

- `feature/**` — এই repo-তে কাজ হয় feature branch-এ, তারপর লোকালি main-এ merge। তাই **merge-এর আগেই** খবর দরকার। শুধু `main` রাখলে CI হতো merge-এর পরের অ্যালার্ম, গেট নয়।
- `workflow_dispatch` — GitHub UI-তে "Run workflow" বোতাম, হাতে চালানোর জন্য।
- **`pull_request:` নেই** — কারণ এখানে PR ব্যবহার হয় না। PR ধরলে যোগ করবে, **কিন্তু তখন push trigger থেকে `feature/**` বাদ দিও** — নইলে এক কাজে দুবার run, দ্বিগুণ CI মিনিট।

### Permissions

```yaml
permissions:
  contents: read
```

প্রতি run-এ GitHub একটা অস্থায়ী token দেয়। আমাদের CI-র কিছু **লেখার** দরকার নেই, শুধু কোড পড়ে। তাই token-কে read-only করে দিই। কোনোদিন কোনো compromised dependency ওই token চুরি করলেও দিয়ে কিছু করতে পারবে না। **নিরাপত্তার সবচেয়ে সস্তা ২ লাইন।**

### Concurrency

```yaml
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true
```

একই branch-এ দ্রুত পরপর তিনটা push করলে তিনটা run চলত। পুরনো দুটোর ফল কেউ পড়বে না — বাতিল করে মিনিট বাঁচাই।

### Backend job

```yaml
backend:
  runs-on: ubuntu-latest
  timeout-minutes: 15
```
- `ubuntu-latest` — **Docker preinstalled**। এই কারণেই আমাদের Testcontainers-এর কোড এক অক্ষর না বদলে CI-তে চলে।
- `timeout-minutes` — container ঝুলে গেলে run ৬ ঘণ্টা ধরে মিনিট গিলবে না।

| Step | কেন |
|---|---|
| `actions/checkout@v4` | কোড আনে। এটা না দিলে VM খালিই থাকে। |
| `actions/setup-dotnet@v4` (10.0.x) | .NET 10 SDK বসায় |
| `actions/cache@v4` | NuGet package cache। key = সব csproj-এর hash → csproj বদলালেই কেবল নতুন করে নামায় |
| `dotnet restore` | package নামায় (cache hit হলে দ্রুত) |
| `dotnet build --no-restore` | restore আগেই হয়েছে, দুবার করার মানে নেই |
| `dotnet test --no-build` | ৯১টা টেস্ট — unit + integration একসাথে |

> **CI-তে কোনো secret লাগে না।** integration test নিজেই SQL Server + Redis container তোলে, আর LLM provider কখনো ছোঁয় না (Jwt key টেস্ট নিজে দেয়)। এটা ভালো টেস্ট ডিজাইনের লক্ষণ — টেস্ট যদি আসল API key চাইত, তাহলে সেটা ধীর, খরচওয়ালা, আর নেট ডাউন হলেই লাল হতো।

### Frontend job

```yaml
defaults:
  run:
    working-directory: frontend
```
প্রতি step-এ `cd frontend` লেখার বদলে একবার বলে দেওয়া।

| Step | কেন |
|---|---|
| `setup-node` (24) + `cache: npm` | Node বসায় + npm cache |
| `npm ci` | `npm install` নয় — **`ci` package-lock.json হুবহু মানে**, version drift করে না। CI-র জন্যই বানানো। |
| `npm run lint` | oxlint |
| `npm run build` | `tsc -b && vite build` — **type error-ও এখানেই ধরা পড়ে**, আলাদা typecheck step লাগে না |

---

## ৫. কীভাবে দেখব

**github.com/Saifulpavel/AiPlatform → `Actions` ট্যাব**

| কোথায় | কী দেখবে |
|---|---|
| Actions ট্যাব | বাঁয়ে workflow-এর নাম **CI**; মাঝে প্রতিটা run (commit message-এর নামে) |
| run-এ ক্লিক | **দুটো box পাশাপাশি: `backend` আর `frontend`** — parallel চলার প্রমাণ |
| job-এ ক্লিক | প্রতিটা step আলাদা, ক্লিক করলে পুরো log |
| Code ট্যাব | commit-এর পাশে ছোট ✅ / ❌ / 🟡 |
| Email | ফেল করলে GitHub নিজে থেকে পাঠায় |

**backend job-এ যা খুঁজবে** — `dotnet test` step-এর log-এ:
```
Passed!  - Failed: 0, Passed: 75 ...   ← unit
Passed!  - Failed: 0, Passed: 16 ...   ← integration
```
১৬টা integration সবুজ মানে GitHub-এর খালি মেশিনে SQL Server + Redis container উঠেছে, migration চলেছে, tenant isolation যাচাই হয়েছে — তোমার ল্যাপটপের কোনো সাহায্য ছাড়া।

**সময়:** প্রথম run backend ~৫-৮ মিনিট (SQL Server image ~১.৫ GB নামে), frontend ~১-২ মিনিট। পরে cache-এ দ্রুত।

---

## ৬. CI-র দাঁত আছে প্রমাণ করো

> সবুজ দেখা মানেই CI কাজ করছে নয় — যে workflow কিছুই চেক করে না, সেটাও সবুজ। **ভাঙলে লাল হয় কিনা** — সেটাই আসল প্রশ্ন।

সস্তা উপায়: **frontend ভাঙো** (~১ মিনিটে ফল, backend-এর ৭ মিনিট লাগে না)।

`frontend/src/features/api/baseApi.ts`-এর উপরে:
```ts
const ciSmokeTest: number = "this should not compile";
```
```powershell
git commit -am "temp: verify CI catches type errors"
git push
```

আশা: **frontend ❌ লাল, backend ✅ সবুজ**। লাল job → `npm run build` step → ঠিক ওই লাইন ধরিয়ে দেবে।

ফিরিয়ে আনো:
```powershell
git revert --no-edit HEAD
git push
```

> `revert` ব্যবহার করছি `reset`-এর বদলে — push করা commit ইতিহাসে থাক, force-push-এর দরকার নেই।

এই অভ্যাসটা ধরে রাখো: **নতুন কোনো পাহারা (test/CI/lint) বসালেই প্রথমে ভেঙে দেখো ধরে কিনা।**

---

## ৭. সাধারণ সমস্যা

| লক্ষণ | কারণ | সমাধান |
|---|---|---|
| Actions ট্যাবে কিছুই নেই | branch trigger-এ মেলেনি | branch নাম `feature/...` কিনা দেখো; বা "Run workflow" বোতাম |
| লোকালে সবুজ, CI-তে লাল | **ঠিক এই কারণেই CI** — কোনো ফাইল commit হয়নি, বা শুধু তোমার মেশিনে আছে | log পড়ো; `git status` দেখো untracked কিছু বাকি আছে কিনা |
| `npm ci` ফেল | package-lock.json আর package.json অমিল | লোকালে `npm install` → lock commit করো |
| Testcontainers timeout | runner ধীর/image বড় | `timeout-minutes` বাড়াও, বা image pull কমাও |
| YAML error | tab ব্যবহার, বা ভুল indent | **YAML-এ tab নিষিদ্ধ, শুধু space** |
| run হঠাৎ বাতিল | নতুন push এসেছে | `cancel-in-progress` — ইচ্ছাকৃত, সমস্যা নয় |

---

## ৮. খরচ

Private repo → GitHub free tier-এ **মাসে ২০০০ minute**। আমাদের run ~৭-১০ minute (দুই job মিলে) → মাসে ~২০০+ run সম্ভব। যথেষ্টের অনেক বেশি।

মিনিট বাঁচানোর যা আছে: cache, `cancel-in-progress`, `timeout-minutes`, feature branch-এ push (প্রতি commit-এ নয়)।

---

## ৯. যা এখনো নেই (ইচ্ছাকৃত)

| জিনিস | কেন নেই |
|---|---|
| **CD (auto-deploy)** | deploy করার জায়গাই নেই (Azure/AWS/VPS নেই)। কোথায় যাবে না জেনে pipeline লেখা = ফেলে দেওয়ার কোড। hosting ঠিক হলে যোগ হবে — CI-র ভিত্তিটাই CD-র ভিত্তি। |
| **Branch protection** | "CI সবুজ না হলে merge করা যাবে না" — এটা GitHub UI-র সেটিং (Settings → Branches), কোড নয়। PR flow ধরলে অর্থবহ। |
| **Code coverage report** | coverlet আছে, কিন্তু coverage % একটা সংখ্যা মাত্র — টেস্টের মান নয়। দরকার হলে পরে। |
| **E2E job** | Phase 8e — Playwright যোগ হলে। |

---

## ১০. Quick reference

| কাজ | কীভাবে |
|---|---|
| CI চালাও | `git push` (branch `main` বা `feature/**`) |
| হাতে চালাও | Actions → CI → **Run workflow** |
| ফল দেখো | Actions ট্যাব → run → job → step → log |
| লোকালে CI যা করে তাই করো | `dotnet restore backend/AiPlatform.slnx` → `dotnet build backend/AiPlatform.slnx --no-restore` → `dotnet test backend/AiPlatform.slnx --no-build`; আর `cd frontend; npm ci; npm run lint; npm run build` |
| workflow বদলাও | `.github/workflows/ci.yml` — বদলটা push করলেই কার্যকর |

> **মনে রেখো:** CI কোনো জাদু নয় — এটা শুধু তোমার নিজের কমান্ডগুলো একটা খালি মেশিনে চালায়। তাই CI লাল হলে প্রথম প্রশ্ন: "এই কমান্ডটা খালি মেশিনে কেন ফেল করবে?" উত্তর প্রায় সবসময়: **এমন কিছুর উপর নির্ভর করছি যা commit করিনি।**
