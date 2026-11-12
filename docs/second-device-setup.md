# দ্বিতীয় device-এ setup (বাংলা)

> একই project দুই মেশিন থেকে চালানো। git কোড আনে — **কোড ছাড়া বাকি সব হাতে বসাতে হয়**।
>
> এই guide ধরে নেয় device 1 ইতিমধ্যে চলছে, আর device 2 একদম খালি।

---

## git যা আনে না

| জিনিস | কেন আনে না | ধাপ |
|---|---|---|
| user-secrets (১২টা API key) | repo-র বাইরের ফাইল — এটাই user-secrets-এর কাজ | §৩ |
| `.env`, `frontend/.env` | `.gitignore`-এ | §৪ |
| Claude Code memory | মেশিনের personal ফোল্ডারে, repo-তে নয় | §৭ |
| Docker volume (DB-র data) | container-এর নিজস্ব | কিছু লাগে না — seeder ভরে দেয় |
| `node_modules`, `bin/`, `obj/` | `.gitignore`-এ | §৬ |

---

## ১. আগে যা থাকতে হবে

```powershell
dotnet --version    # 10.x
node --version
docker --version
git --version
```

সাথে Docker Desktop **চালু** থাকতে হবে, আর Claude Code ইনস্টল থাকতে হবে।

---

## ২. Clone — ⚠️ একই path-এ

Claude Code-এর memory ফোল্ডারের নাম **working directory থেকে বানানো হয়**
(`D:\Saiful` → `D--Saiful`)। অন্য path-এ clone করলে §৭-এর memory Claude খুঁজে পাবে না।

```powershell
New-Item -ItemType Directory -Force D:\Saiful
cd D:\Saiful
git clone https://github.com/Saifulpavel/AiPlatform.git
```

---

## ৩. user-secrets কপি

`<UserSecretsId>` আছে `backend/src/Api/AiPlatform.Api.csproj`-এ:
`553d5adb-9d3a-4a49-98a4-d72b03a78c5f`

**device 1-এর ফাইল** (`Roaming`, `Local` নয়):

```
C:\Users\<you>\AppData\Roaming\Microsoft\UserSecrets\553d5adb-9d3a-4a49-98a4-d72b03a78c5f\secrets.json
```

**device 2-এ** আগে ফোল্ডার বানাও, তারপর ফাইলটা ওখানে রাখো:

```powershell
New-Item -ItemType Directory -Force "$env:APPDATA\Microsoft\UserSecrets\553d5adb-9d3a-4a49-98a4-d72b03a78c5f"
```

যাচাই — ১২টা key আসা উচিত (Smtp ৪, Llm provider ৬, Embedding ১, Google ClientId ১):

```powershell
cd D:\Saiful\AiPlatform
dotnet user-secrets list --project backend/src/Api
```

> ⚠️ **এই ফাইল git/email/chat-এ পাঠাবে না।** USB বা password manager দিয়ে নাও।
> বিস্তারিত: `docs/user-secrets-guide.md`।

---

## ৪. দুটো `.env` বানাও

দুটোই `.gitignore`-এ, তাই clone-এ আসে না। repo-তে `.example` আছে:

```powershell
cd D:\Saiful\AiPlatform
Copy-Item .env.example .env
Copy-Item frontend\.env.example frontend\.env
```

তারপর মান বসাও:

| ফাইল | key |
|---|---|
| `.env` | `SA_PASSWORD`, `JWT_SECRET` |
| `frontend/.env` | `VITE_API_BASE_URL`, `VITE_GOOGLE_CLIENT_ID` |

`SA_PASSWORD` device 1-এর সাথে এক হতে হবে না, কিন্তু
`backend/src/Api/appsettings.Development.json`-এর connection string-এর সাথে **মিলতেই হবে**।

---

## ৫. Infra তোলো

```powershell
cd D:\Saiful\AiPlatform
docker compose up -d
docker ps
```

> ⚠️ **নতুন মেশিনে port সমস্যা হওয়া স্বাভাবিক।** `ports are not available ... forbidden by
> its access permissions` এলে বুঝবে Windows নিজেই port-টা reserve করে রেখেছে — Hyper-V-র
> dynamic range প্রতি মেশিনে আলাদা। ফিক্স: `docs/troubleshooting.md` §১ (SQL Server 1433) ও
> §৫ (Qdrant 6333+6334) — দুটোতেই পদ্ধতি এক, শুধু port নম্বর বদলায়।

---

## ৬. Build ও চালু

```powershell
cd D:\Saiful\AiPlatform
dotnet build backend/AiPlatform.slnx
npm --prefix frontend install
```

```powershell
cd D:\Saiful\AiPlatform
.\scripts\dev.ps1
```

| কী | কোথায় |
|---|---|
| API | http://localhost:5076 (`/swagger`, `/health`) |
| Frontend | http://localhost:5173 |
| Aspire dashboard | http://localhost:18888 |

প্রথম startup-এ ERP seeder DB ভরবে (`Erp:SeedOnStartup=true`, Development-এ)। seed
deterministic, তাই device 1-এর সাথে হুবহু একই সংখ্যা আসবে।

---

## ৭. Claude Code-এর memory

`CLAUDE.md` repo-তে আছে, clone-এর সাথেই আসে। **Personal memory আসে না** — ওটা এখানে:

```
C:\Users\<you>\.claude\projects\D--Saiful\memory\
```

পুরো ফোল্ডারটা device 2-এর একই জায়গায় কপি করো (`MEMORY.md` + বাকি সব `.md`)।
এতে git workflow, quality bar, prompt convention — সব নিয়ম দ্বিতীয় device-এও চালু থাকবে।

> **Chat history কপি হয় না** — ওটা per-device। নতুন device-এ নতুন session, কিন্তু
> memory + `CLAUDE.md` থাকায় context হারায় না।

---

## ৮. দৈনন্দিন নিয়ম

দুই device-এর একমাত্র আসল ফাঁদ divergence। তিনটা নিয়ম:

1. **কাজ শুরুর আগে** — `git pull origin main`
2. **device ছাড়ার আগে** — commit + push। অসম্পূর্ণ হলেও WIP commit করে push করো; নইলে
   অন্য device-এ গিয়ে আটকে যাবে, আর ফিরে এসে conflict।
3. **একই সময়ে দুই device-এ কাজ কোরো না।**

Secret বদলালে (নতুন API key) — git সেটা জানে না। দুই মেশিনে হাতে বসাতে হবে, বা §৩-এর
ফাইলটা আবার কপি করতে হবে।

---

## ৯. যা sync হয় **না**, জেনে রাখো

- **DB-র data** — প্রতি device-এর নিজের SQL Server container। seed data এক, কিন্তু তুমি
  device 1-এ যে খসড়া order বা upload করা document বানাবে সেটা device 2-এ থাকবে না।
- **Qdrant-এর vector** — একই কারণ। প্রথম query-তে KB আবার index হয়ে যাবে।
- **Redis cache** — খালি থেকেই শুরু, সমস্যা নয়।

---

## ১০. সতর্কতা

`backend/src/Api/appsettings.Development.json`-এ SA password **আগে থেকেই commit করা**।
local dev container-এর জন্য ঠিক আছে, কিন্তু repo public হলে ওই password পোড়া — অন্য
কোথাও ব্যবহার কোরো না, আর production-এ কখনো এই pattern নয় (`CLAUDE.md` §Don'ts)।
