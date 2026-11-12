# Troubleshooting & Environment Hacks (বাংলা)

> মেশিন/env-এর সমস্যা আর তাদের ফিক্স এখানে জমা হয়। কোড-এর bug নয় — **পরিবেশের** সমস্যা
> (port, Docker, Windows, driver ইত্যাদি) যেগুলো একবার সমাধান করলেও পরে আবার ভোগায়।
>
> নিয়ম: নতুন এমন কোনো hack/ফিক্স বের হলেই এই ফাইলে নতুন entry যোগ করা হবে। প্রতিটা entry-তে —
> **লক্ষণ (হুবহু error) → আসল কারণ → ফিক্স → কেন কাজ করে → এককালীন না বারবার।**

---

## সূচি
1. [SQL Server port 1433 — "forbidden by its access permissions"](#১-sql-server-port-1433--forbidden-by-its-access-permissions)
2. [Build "file locked by AiPlatform.Api" — চলন্ত API না মরা](#২-build-file-locked-by-aiplatformapi--চলন্ত-api-না-মরা)
3. [Claude Browser pane-এ animation/chart "ভাঙা" — rAF fire হয় না](#৩-claude-browser-pane-এ-animationchart-ভাঙা--raf-fire-হয়-না)
4. [KB doc বদলানোর পরেও AI পুরোনো কথা বলে — Qdrant-এ বাসি chunk](#৪-kb-doc-বদলানোর-পরেও-ai-পুরোনো-কথা-বলে--qdrant-এ-বাসি-chunk)
5. [Qdrant port 6333/6334 — আবার সেই "forbidden by its access permissions"](#৫-qdrant-port-63336334--আবার-সেই-forbidden-by-its-access-permissions)

---

## ১. SQL Server port 1433 — "forbidden by its access permissions"

**লক্ষণ** (`docker compose up -d`):
```
Error response from daemon: ports are not available: exposing port TCP 0.0.0.0:1433 -> 127.0.0.1:0:
listen tcp 0.0.0.0:1433: bind: An attempt was made to access a socket in a way forbidden by its access permissions.
```

> ⚠️ এটা সাধারণ "port already in use" নয়। "forbidden by its access permissions" = **Windows নিজেই** port-টা reserve করে রেখেছে, অন্য কোনো app নয়।

**আসল কারণ:** Hyper-V / WinNAT একটা dynamic TCP port range **সংরক্ষণ** করে, আর 1433 সেই range-এর ভেতরে পড়ে গেছে। এই reservation প্রতি reboot-এ এলোমেলোভাবে বদলায় — তাই আগে চলছিল, container বন্ধ থাকার ফাঁকে port ছাড়া পেল, Windows গিলে ফেলল।

**নির্ণয়** (normal PowerShell) — 1433 কি excluded range-এ?
```powershell
netsh interface ipv4 show excludedportrange protocol=tcp
```
আউটপুটে যদি কোনো range-এর `Start Port ≤ 1433 ≤ End Port` হয় (যেমন `1365  1464`), তাহলে এটাই সমস্যা।

**ফিক্স** — **Administrator PowerShell** (Start → PowerShell-এ ডান ক্লিক → "Run as administrator"):
```powershell
net stop winnat
netsh int ipv4 add excludedportrange protocol=tcp startport=1433 numberofports=1
net start winnat
```
তারপর:
```powershell
cd D:\Saiful\AiPlatform
docker compose up -d
docker ps
```

**কেন কাজ করে:**
- `net stop winnat` — Windows NAT থামায় → dynamic reservation ছেড়ে দেয়।
- `netsh ... add excludedportrange` — 1433-কে **administered exclusion** বানায়। মানে Windows-কে বলা "এই port আমার, dynamic allocation-এ কখনো নিও না"। explicit bind (Docker যেটা করে) তখনো কাজ করে। তালিকায় `*` চিহ্ন = administered।
- `net start winnat` — NAT ফিরিয়ে আনে।

**⚠️ সাবধানতা:** `net stop winnat` কয়েক সেকেন্ডের জন্য Docker-এর networking ফেলে দেয় — চলন্ত container-এর network হারাতে পারে, তাই পরে `docker compose up -d` আবার চালাও।

**এককালীন?** হ্যাঁ। administered exclusion reboot-এও টেকে, সমস্যা আর ফিরবে না।

**পার্শ্ব-নোট:** integration test (Testcontainers) এই সমস্যায় পড়ে না — ওরা random high port ব্যবহার করে, fixed 1433 নয়। তাই `dotnet test` চললেও `docker compose up` ফেল করতে পারে।

---

## ২. Build "file locked by AiPlatform.Api" — চলন্ত API না মরা

**লক্ষণ** (`dotnet build`/`dotnet run` চলমান API-র সময়):
```
error MSB3027: Could not copy "...\obj\Debug\net10.0\apphost.exe" to "bin\Debug\net10.0\AiPlatform.Api.exe".
Exceeded retry count of 10. Failed. The file is locked by: "AiPlatform.Api (22852)"
```

**আসল কারণ:** আগের `dotnet run` এখনো চলছে, `.exe` ধরে রেখেছে। build নতুন binary ওখানে কপি করতে পারছে না। প্রায়ই ঘটে যখন background-এ API চালিয়ে আবার build/run করি।

**Windows-এর ফাঁদ:** bash-এর `pkill -f "AiPlatform.Api"` এখানে **কাজ করে না** — process নামে ধরা যায় না, বা signal ঠিকমতো পৌঁছায় না। তাই "মেরেছি" মনে করলেও process বেঁচে থাকে, পরের build ওই একই lock error দেয়।

**ফিক্স** (PowerShell — এটাই নির্ভরযোগ্য):
```powershell
Get-Process -Name "AiPlatform.Api" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
```
তারপর আবার build/run।

**নির্ণয়** — সত্যিই মরেছে কিনা:
```powershell
Get-Process -Name "AiPlatform.Api" -ErrorAction SilentlyContinue
```
কিছু না দেখালে পরিষ্কার। PID ধরে মারতে চাইলে: `Stop-Process -Id <pid> -Force`।

**কেন কাজ করে:** `Stop-Process -Force` Windows API দিয়ে সরাসরি process টার্মিনেট করে (bash signal-এর উপর নির্ভর করে না), তাই `.exe` lock ছাড়ে।

**এককালীন?** না — যতবার background API চালিয়ে rebuild করবে ততবার লাগতে পারে। অভ্যাস: rebuild-এর আগে উপরের Stop-Process চালাও, অথবা আগের `dotnet run` টার্মিনাল Ctrl+C দিয়ে বন্ধ করো।

---

## ৩. Claude Browser pane-এ animation/chart "ভাঙা" — rAF fire হয় না

**লক্ষণ:** Claude Code-এর Browser pane-এ app খুললে — recharts bar/donut আঁকা হয় না (খালি `<g>`), framer-motion entrance animation `opacity: 0`-তে আটকে থাকে, `computer{action:"screenshot"}` ৩০ সেকেন্ডে timeout। Console-এ কোনো error নেই।

**আসল কারণ:** ওই pane-এর renderer-এ `requestAnimationFrame` callback **কখনোই চলে না** (প্রমাণ: rAF-ভিত্তিক counter অনন্তকাল ঝোলে)। rAF-নির্ভর সবকিছু — chart entry animation, framer-motion, screenshot compositor — তাই "ভাঙা" দেখায়। **কোডের bug নয়, pane-এর artifact।**

**ফিক্স / করণীয়:**
- Animation/visual verify ওই pane-এ **কোরো না** — Playwright (আসল Chromium) দিয়ে করো: e2e test বা এক-অফ screenshot script (`chromium.launch()` → `page.screenshot()`)।
- DOM/text/network verify pane-এ ঠিকই চলে (`read_page`, `javascript_tool`, `read_network_requests`)।
- Chart-এ `isAnimationActive={false}` রাখা ভালো এমনিতেও — rAF-throttled environment-এ (background tab) chart animation-pending-এ আটকে থাকে না।

**কেন কাজ করে:** Playwright-এর Chromium মাথা-সহ/headless দুই মোডেই rAF চালায়; pane-এর renderer কোনো কারণে compositor tick পায় না।

**এককালীন?** না — Browser pane দিয়ে animation verify করতে গেলেই আবার পড়বে। মনে রাখো: pane = DOM যাচাই, Playwright = চোখের যাচাই।

---

## ৪. KB doc বদলানোর পরেও AI পুরোনো কথা বলে — Qdrant-এ বাসি chunk

**লক্ষণ:** `KnowledgeBaseSeed.Documents`-এর কোনো লাইন মুছে/বদলে API restart করার পরেও `qdrant`
retriever পুরোনো লেখাটাই ফেরত দেয়। কোনো error নেই — উত্তর শুধু ভুল/সেকেলে। `keyword` ও `embedding`
retriever-এ সমস্যা হয় না (ওরা প্রতি startup-এ memory-তে নতুন করে গড়ে)।

**আসল কারণ:** Qdrant collection **Docker volume-এ থেকে যায়**, process-এর সাথে মরে না।
`QdrantRetriever` startup-এ seed chunk গুলো upsert করে — id একই হলে overwrite হয়, কিন্তু
**মুছে ফেলা** লাইনের পুরোনো point কেউ ডিলিট করে না, ওটা collection-এ পড়েই থাকে ও search-এ আসে।

⚠️ **collection-এর নাম `knowledge-base` নয়** — ওটা payload-এর `source` মান। আসল নাম
`CollectionNaming.ForEmbedding` বানায়, embedding provider/model অনুযায়ী: `aiplatform_rag`,
`aiplatform_rag_bge-m3`, `aiplatform_rag_ollama`… তাই নাম hardcode না করে সবগুলোতে চালাও।

**ফিক্স** — শুধু seed chunk মোছে (upload করা document অক্ষত, ওদের `source` = ফাইলের নাম):
```powershell
foreach ($c in (Invoke-RestMethod "http://localhost:6333/collections").result.collections.name | Where-Object { $_ -like "aiplatform_rag*" }) {
  $r = Invoke-RestMethod -Method Post -Uri "http://localhost:6333/collections/$c/points/delete?wait=true" -ContentType "application/json" -Body '{"filter":{"must":[{"key":"source","match":{"value":"knowledge-base"}}]}}'
  "$c -> $($r.result.status)"
}
```
প্রতি লাইনে `-> completed` আসা উচিত। তারপর API restart — নতুন doc আবার index হবে।

**কেন কাজ করে:** filter শুধু `source = "knowledge-base"` point মোছে; upload করা document-এর
`source` = ফাইলের নাম, তাই ওরা বাদ যায়। পুরো collection ড্রপ করলে upload করা সব document-ও যেত।

**গোনা** (কমান্ডের আগে/পরে, `points/delete` → `points/count` আর body-তে `,"exact":true`):
2026-07-25-এ পাওয়া গিয়েছিল `aiplatform_rag` = ৬৫, `aiplatform_rag_bge-m3` = ১০,
`aiplatform_rag_ollama` = ৫ — অথচ seed doc তখন ছিল ৫টা। ৬৫ = ১৩ বার restart-এর জমা কপি।

**এককালীন?** না — যতবার `KnowledgeBaseSeed`-এর কোনো লাইন **মুছবে বা বদলাবে** ততবার। শুধু **নতুন
লাইন যোগ** করলে লাগে না। (2026-07-25 থেকে point id = chunk লেখার MD5, তাই অন্তত duplicate জমে না।)

---

## ৫. Qdrant port 6333/6334 — আবার সেই "forbidden by its access permissions"

**লক্ষণ** (`docker compose up -d`) — বাকি container ওঠে, শুধু Qdrant পড়ে যায়:
```
Error response from daemon: ports are not available: exposing port TCP 0.0.0.0:6334 -> 127.0.0.1:0:
listen tcp 0.0.0.0:6334: bind: An attempt was made to access a socket in a way forbidden by its access permissions.
```

**আসল কারণ:** §১-এর হুবহু একই জিনিস, শুধু port আলাদা — WinNAT-এর dynamic range এবার
Qdrant-এর port গিলেছে। 2026-09-23-এ range ছিল `6271–6370`, অর্থাৎ **6333 আর 6334 দুটোই**
ভেতরে। error শুধু প্রথমটা দেখায়, তাই একটা ঠিক করলে পরেরটায় আটকাবে — **দুটো একসাথে** করো।

**নির্ণয়** (normal PowerShell):
```powershell
netsh interface ipv4 show excludedportrange protocol=tcp
```
কোনো range-এ `Start ≤ 6333` এবং `6334 ≤ End` হলে এটাই সমস্যা।

**ফিক্স** — **Administrator PowerShell**:
```powershell
net stop winnat
netsh int ipv4 add excludedportrange protocol=tcp startport=6333 numberofports=2
net start winnat
```
তারপর normal PowerShell-এ:
```powershell
cd D:\Saiful\AiPlatform
docker compose up -d
docker ps
```

**কেন কাজ করে:** §১-এর ব্যাখ্যাই প্রযোজ্য — `numberofports=2` মানে 6333 ও 6334 দুটোকেই
administered exclusion বানানো।

**ততক্ষণ কী চলবে:** Qdrant ছাড়াও app চলে। `keyword`, `embedding`, `hybrid` retriever মেমরিতে
কাজ করে; শুধু `qdrant` retriever অচল। ERP tool গুলো SQL Server-এ, তাই অক্ষত।

**এককালীন?** হ্যাঁ, এই মেশিনে। কিন্তু **নতুন মেশিনে আবার পড়বে** — reserved range প্রতি
মেশিনে আলাদা। দ্বিতীয় device setup-এ এটা ধরে নিয়েই এগোও (`docs/second-device-setup.md` §৫)।

---

<!-- নতুন entry উপরের ফরম্যাটে নিচে যোগ করো; সূচিতেও লাইন যোগ করো। -->
