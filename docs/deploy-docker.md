# Deploy runbook — Windows সার্ভার, Docker, source ছাড়া

এই প্রজেক্টের আসল সেটআপ। মান গুলো এখানে **হুবহু** লেখা — অনুমান করতে হবে না।

```
💻 DEV    = তোমার মেশিন, D:\Saiful\AiPlatform (source এখানেই থাকে)
🖥️ SERVER = Windows Server 2022, LAN IP 192.168.70.178 (source কখনো যাবে না)
```

> ⚠️ **Windows Server-এ Docker Desktop চলে না** — Docker-এর নিজের docs তাই বলে। তাই
> সার্ভারে **WSL2 + ভেতরে Docker CE**। `scripts\server-setup.ps1` পুরোটা বসিয়ে দেয়।
> ফল: docker Linux-এর ভেতরে চলে, আর `aip.cmd` wrapper দিয়ে Windows থেকে কমান্ড দেওয়া যায়।

| | মান |
|---|---|
| GitHub user | `Saifulpavel` |
| Registry | `ghcr.io/saifulpavel` |
| `PUBLIC_HOST` | `192.168.70.178` |
| App URL | `https://192.168.70.178` |
| সার্ভারের ফোল্ডার | `C:\aiplatform` |
| TLS | Caddy `tls internal` (নিজের CA) |

**সার্ভারে শেষ পর্যন্ত যা থাকবে:** ৩টা ফাইল + ৫টা container। কোনো `.cs`, `.tsx`,
`.csproj`, SDK, dotnet বা node নয়।

> ⚠️ তবু: .NET DLL decompile করা যায় (ILSpy)। PDB ও SourceLink বাদ দেওয়ায় সহজ পথটা
> বন্ধ, কিন্তু logic পড়া ঠেকানো যায় না। আসল সুরক্ষা = secret কোডের বাইরে।
> শেষে "কতটুকু লুকানো আছে" দেখো।

---

# পর্ব ক — 💻 DEV, একবারের কাজ

- [ ] **ক১. repo private করো**

  সবচেয়ে জরুরি ধাপ। public থাকলে বাকি সব অর্থহীন — repo নিজেই খোলা।

  ```powershell
  gh repo edit Saifulpavel/AiPlatform --visibility private --accept-visibility-change-consequences
  ```

- [ ] **ক২. `deploy\.env` আছে ও ভরা কি না দেখো**

  ```powershell
  Get-Content D:\Saiful\AiPlatform\deploy\.env | ForEach-Object { ($_ -split '=', 2)[0] }
  ```

  `SA_PASSWORD`, `JWT_SECRET`, `GEMINI_API_KEY`, `GROQ_API_KEY`, `PUBLIC_HOST` — সব থাকা চাই।
  `PUBLIC_HOST=192.168.70.178` হতে হবে।

  > ⚠️ dev-এর JWT key ও SQL password (`dev-only-signing-key...`, `Your_strong_Pass123!`)
  > public repo-তে ছিল — আর গোপন নয়। `.env`-এ নতুন মান আছে, ওগুলো বদলাবে না।
  > `.env` git-ignored; email/chat-এও পাঠাবে না।

- [ ] **ক৩. GitHub token বানাও**

  Settings → Developer settings → Personal access tokens → **Tokens (classic)** →
  Generate new token (classic)

  - Note: `aiplatform-ghcr`, Expiration: 90 days
  - scope: **`write:packages`** + **`delete:packages`** (বাকি কিছু নয়)

  ⚠️ **Fine-grained token নয়** — GHCR-এ classic নিশ্চিতভাবে চলে।

- [ ] **ক৪. GHCR-এ login**

  ```powershell
  $env:CR_PAT = 'ghp_...'; $env:CR_PAT | docker login ghcr.io -u Saifulpavel --password-stdin
  ```

  `Login Succeeded` আসা চাই।

---

# পর্ব খ — 💻 DEV, image বানাও ও পাঠাও

Docker Desktop চালু থাকতে হবে।

- [ ] **খ১. `release.bat` double-click** — hostname চাইলে `192.168.70.178` দাও

  অথবা কমান্ডে:

  ```powershell
  cd D:\Saiful\AiPlatform; .\scripts\release.ps1 -PublicHost 192.168.70.178 -Tag 1.0.0
  ```

  প্রথমবার ৫-১০ মিনিট (SDK image নামবে)। script নিজে পরীক্ষা করে image-এ `.pdb`
  ঢুকেছে কি না — ঢুকলে push আটকে দেয়।

  শেষে দেখবে:

  ```
  Done.
    ghcr.io/saifulpavel/aiplatform-api:1.0.0
    ghcr.io/saifulpavel/aiplatform-web:1.0.0
  ```

  > ⚠️ hostname frontend-এ **বেক** হয়। `.env`-এর `PUBLIC_HOST`-এর সাথে হুবহু মিলতে হবে।

---

# পর্ব গ — 🖥️ SERVER, একবারের কাজ

- [ ] **গ১. `scripts\server-setup.ps1` সার্ভারে কপি করো**, **Administrator PowerShell**-এ চালাও

  ```powershell
  powershell -ExecutionPolicy Bypass -File .\server-setup.ps1
  ```

  যা করে: WSL2 + Virtual Machine Platform → Ubuntu → Docker CE → firewall 80/443 →
  `C:\aiplatform` → `aip.cmd` wrapper → boot-এ auto-start task + WSL portproxy →
  শেষে `docker run hello-world` দিয়ে যাচাই।

  **প্রথমবার reboot চাইবে** — restart করে আবার চালাও, বাকি ধাপ থেকে শুরু হবে।
  idempotent, বারবার চালালেও ক্ষতি নেই।

- [ ] **গ২. GHCR-এ login** (ক৩-এর একই token)

  ⚠️ docker **WSL-এর ভেতরে** চলে, তাই login-ও ওখানেই করতে হবে — Windows-এ নয়:

  ```powershell
  wsl -d Ubuntu-22.04 -u root -e bash -lc "echo ghp_... | docker login ghcr.io -u Saifulpavel --password-stdin"
  ```

- [ ] **গ৩. ৩টা ফাইল কপি করো** — 💻 DEV থেকে 🖥️ SERVER-এ (RDP copy-paste বা পেনড্রাইভ)

  | কোথা থেকে | কোথায় |
  |---|---|
  | `D:\Saiful\AiPlatform\deploy\docker-compose.prod.yml` | `C:\aiplatform\` |
  | `D:\Saiful\AiPlatform\deploy\Caddyfile` | `C:\aiplatform\` |
  | `D:\Saiful\AiPlatform\deploy\.env` | `C:\aiplatform\` |

  **এই তিনটেই। আর কিছু না।** `.env`-এ secret আছে — email/chat-এ পাঠাবে না।

- [ ] **গ৪. সার্ভারের IP স্থির করো**

  `192.168.70.178` DHCP থেকে পাওয়া। router বদলে দিলে app বন্ধ হবে — router-এ MAC-এর
  বিপরীতে reserve করো, বা Windows-এ static IP বসাও।

  ```powershell
  Get-NetIPConfiguration | Select-Object InterfaceAlias, IPv4Address, IPv4DefaultGateway
  ```

---

# পর্ব ঘ — 🖥️ SERVER, চালু করো

`aip.cmd` wrapper কমান্ডগুলো WSL-এর docker-এ পাঠায় — সব `C:\aiplatform` থেকে চালাও।

- [ ] **ঘ১. image নামাও**

  ```powershell
  cd C:\aiplatform; .\aip.cmd pull
  ```

- [ ] **ঘ২. চালু করো**

  ```powershell
  cd C:\aiplatform; .\aip.cmd up
  ```

  (`up` শেষে নিজেই portproxy ঠিক করে দেয়।)

  প্রথমবার **২-৫ মিনিট** — SQL Server নিজেকে তৈরি করে, তারপর API migration চালায় +
  ERP sample data সিড করে।

- [ ] **ঘ৩. container গুলো দেখো**

  ```powershell
  cd C:\aiplatform; .\aip.cmd ps
  ```

  ৫টা container `running`; sqlserver ও redis `healthy`।

- [ ] **ঘ৪. health**

  ```powershell
  curl.exe -k https://192.168.70.178/health
  ```

  `Healthy` আসা চাই। না এলে:

  ```powershell
  cd C:\aiplatform; .\aip.cmd logs api --tail 50
  ```

---

# পর্ব ঙ — ব্রাউজারে যাচাই

অফিসের যেকোনো মেশিন থেকে `https://192.168.70.178`

**প্রথমবার সার্টিফিকেট warning আসবে** — "Advanced → Proceed to 192.168.70.178 (unsafe)"।
স্বাভাবিক; Caddy-র নিজের CA বলে। স্থায়ীভাবে সারানো নিচে।

এই ক্রমে পরীক্ষা করো — প্রতিটা আলাদা জিনিস প্রমাণ করে:

| # | কী করবে | পাস মানে |
|---|---|---|
| ১ | পাতা খোলে | Caddy + TLS + frontend ঠিক |
| ২ | register → login | DB + migration ঠিক |
| ৩ | **F5 refresh** → logged in থাকে | HTTPS + refresh cookie ঠিক |
| ৪ | Chat-এ একটা প্রশ্ন | LLM key + outbound internet ঠিক |
| ৫ | Agent: `আজকের বিক্রি কত?` | `AiPlatformErp` DB migrate হয়েছে |
| ৬ | Documents: ফাইল upload → chunk বাড়ে | Redis Streams + BackgroundService + Qdrant ঠিক |
| ৭ | RAG-এ একটা প্রশ্ন | embedding key + vector search ঠিক |

৩ ফেল → HTTPS সমস্যা (`logs caddy`)। ৭ প্রথমবার ৩০-৬০ সেকেন্ড নিতে পারে — seed chunk
embed হয়, বাগ নয়।

---

# সার্টিফিকেট warning স্থায়ীভাবে সারানো

🖥️ SERVER-এ, stack চালু থাকা অবস্থায়:

```powershell
cd C:\aiplatform; .\aip.cmd cp caddy:/data/caddy/pki/authorities/local/root.crt /mnt/c/aiplatform/caddy-root.crt
```

```powershell
Import-Certificate -FilePath C:\aiplatform\caddy-root.crt -CertStoreLocation Cert:\LocalMachine\Root
```

`caddy-root.crt` ফাইলটা অন্য ব্যবহারকারীর মেশিনেও কপি করে দ্বিতীয় কমান্ডটা চালাও
(Administrator PowerShell লাগবে)। এটা secret নয় — public certificate, শেয়ার করা নিরাপদ।

---

# পরের বার update

কোড বদলালে:

**💻 DEV** — `release.bat`, নতুন tag (`1.0.1`)

**🖥️ SERVER** — `C:\aiplatform\.env`-এ `TAG=1.0.1`, তারপর:

```powershell
cd C:\aiplatform; .\aip.cmd pull; .\aip.cmd up
```

ডেটা হারাবে না — SQL/Redis/Qdrant সব volume-এ।

---

# রোজকার কমান্ড (🖥️ SERVER)

সব কমান্ডের আগে `cd C:\aiplatform`।

| কাজ | কমান্ড |
|---|---|
| অবস্থা | `.\aip.cmd ps` |
| API log | `.\aip.cmd logs api --tail 50` |
| সব log, লাইভ | `.\aip.cmd logs -f` |
| restart | `.\aip.cmd restart api` |
| বন্ধ (data থাকে) | `.\aip.cmd down` |
| **সব মুছে ফেলা** | `.\aip.cmd down -v` ⚠️ DB সহ সব যাবে |
| WSL/docker অবস্থা | `wsl -d Ubuntu-22.04 -u root -e systemctl status docker` |
| portproxy ঠিক করা | `powershell -File C:\aiplatform\wsl-portproxy.ps1` |

> **LAN থেকে ধরা যাচ্ছে না?** WSL2-র IP প্রতি boot-এ বদলায়। startup task আপনাআপনি
> ঠিক করে দেয়, কিন্তু আগেভাগে লাগলে উপরের `wsl-portproxy.ps1` চালাও।
> দেখতে: `netsh interface portproxy show v4tov4`

---

# যে ফাঁদগুলো ইচ্ছে করে ধরা হয়েছে

| | কেন |
|---|---|
| **HTTPS বাধ্যতামূলক** | refresh cookie-তে `Secure=true` ([AuthController.cs:136](../backend/src/Api/Controllers/AuthController.cs:136))। HTTP-তে login হবে, refresh-এ logout। তাই Caddy। |
| **HSTS header নেই** | self-signed cert + HSTS হলে Chrome "Proceed anyway" বাটনই দেয় না। Let's Encrypt-এ গেলে যোগ করো। |
| **`Erp__SeedOnStartup=true`** | ERP migration ওই flag-এর ভেতরে ([Program.cs:174](../backend/src/Api/Program.cs:174))। false দিলে `AiPlatformErp` DB-ই তৈরি হয় না → agent-এর tool সব ফাটে। |
| **`Llm__Embedding__ApiKey` আলাদা** | embedding provider-এর key পড়ে না ([LlmSettings.cs:40](../backend/src/Infrastructure/Llm/LlmSettings.cs:40))। না দিলে chat চলে কিন্তু ingest ও RAG ফাটে। |
| **API এক replica** | দুটো হলে দুজনেই startup-এ `MigrateAsync()` চালায় — EF migration concurrent-safe নয়। |
| **`/sse` বন্ধ** | MCP endpoint auth ছাড়া, আর tool গুলো ERP DB পড়ে/লেখে। এখন `Mcp:Enabled` flag লাগে, Caddy-ও route করে না। |
| **SQL port publish করা হয়নি** | DB শুধু compose network-এ। SSMS লাগলে সাময়িকভাবে `ports: ["1433:1433"]` যোগ করো। |
| **`caddy-data` volume** | সার্টিফিকেট ওখানে। মুছলে নতুন CA তৈরি হবে → সব মেশিনে আবার root cert install করতে হবে। |
| **Dockerfile-এ `--no-restore` নেই** | ওটা দিলে `CSC : error CS5001` — csproj-only restore layer-এর পর entry point খুঁজে পায় না। |

---

# পরে বাইরে থেকেও ঢুকতে চাইলে

এখন শুধু অফিসের LAN থেকে চলে। internet-এ খুলতে হলে:

১. router-এ port forward: বাইরের 80 → `192.168.70.178:80`, 443 → `:443`
২. domain বা [DuckDNS](https://duckdns.org)-এ সার্ভারের public IP (`103.12.205.244`) বসাও
৩. `Caddyfile` থেকে `tls internal` লাইনটা মুছে দাও, `header` ব্লকে
   `Strict-Transport-Security "max-age=31536000"` যোগ করো
৪. `.env`-এ নতুন `PUBLIC_HOST`, আর **`release.bat` আবার চালাও** — hostname frontend-এ বেক হয়

⚠️ বাংলাদেশে অনেক ISP inbound port 80 ব্লক করে — আগে পরীক্ষা করে নিও।

---

# কতটুকু লুকানো আছে

| | অবস্থা |
|---|---|
| `.cs` / `.tsx` / `.csproj` image-এ | ❌ নেই (যাচাই করা) |
| `.pdb` (SourceLink → GitHub source) | ❌ নেই (release.ps1 প্রতিবার পরীক্ষা করে) |
| frontend sourcemap | ❌ নেই |
| `appsettings.Development.json` | ❌ নেই |
| API key / connection string image-এ | ❌ নেই — runtime env var |
| **IL → C# decompile** | ⚠️ **সম্ভব।** .NET-এ ঠেকানো যায় না (NativeAOT এখানে অচল — EF Core AOT-এ model building সমর্থন করে না) |

**উপসংহার:** কেউ image পেলে logic পড়তে পারবে, কিন্তু **ঢুকতে পারবে না** — key, password
আর certificate সব image-এর বাইরে।
