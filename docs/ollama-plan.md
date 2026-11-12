# Ollama Plan — local LLM setup, model, training, integration (বাংলা)

> লক্ষ্য: north star-এর "hosted→local swap" প্রমাণ — Ollama দিয়ে **নিজের মেশিনে** LLM চালিয়ে
> AiPlatform-এর chat/RAG/evals চালানো, কোনো API key/খরচ ছাড়া। Handler/UI-তে হাত পড়বে না —
> শুধু নতুন provider impl + config। এটাই abstraction-এর দাঁত-পরীক্ষা।

---

## সূচি
1. [Ollama কী, কেন](#১-ollama-কী-কেন)
2. [Setup (Windows)](#২-setup-windows)
3. [Model setup — কোনটা নামাবো, কীভাবে](#৩-model-setup)
4. [নিজের মতো model বানানো — Modelfile (training-এর সহজ রূপ)](#৪-modelfile--নিজের-মতো-model)
5. [আসল fine-tuning (ছোট model train)](#৫-আসল-fine-tuning)
6. [AiPlatform-এ integration plan](#৬-aiplatform-এ-integration-plan)
7. [সীমাবদ্ধতা ও প্রত্যাশা](#৭-সীমাবদ্ধতা-ও-প্রত্যাশা)

---

## ১. Ollama কী, কেন

**Ollama** = নিজের কম্পিউটারে LLM চালানোর সবচেয়ে সহজ রাস্তা। Docker যেমন container চালায়, Ollama তেমন model চালায় — `ollama pull` → `ollama run`, শেষ।

আমাদের জন্য গুরুত্বপূর্ণ যে কারণে:
- **API key নেই, খরচ নেই, rate limit নেই** — evals/dev-এ হাজার call ফ্রি
- **Offline** চলে; data মেশিনের বাইরে যায় না (privacy)
- **OpenAI-compatible API** দেয় (`http://localhost:11434/v1/...`) — আমাদের `OpenAiCompatibleChatProvider` প্রায় হুবহু reuse হবে
- Embedding model-ও চালায় → RAG-ও পুরো local হতে পারে

---

## ২. Setup (Windows)

1. https://ollama.com/download → Windows installer নামাও → install (service হিসেবে চলে, tray icon)
2. যাচাই (নতুন টার্মিনাল):
   ```powershell
   ollama --version
   ```
3. Ollama API চলছে কিনা:
   ```powershell
   Invoke-RestMethod http://localhost:11434/api/version
   ```
4. GPU থাকলে (NVIDIA) নিজে থেকেই ব্যবহার করবে; না থাকলে CPU-তে চলবে (ধীর, কিন্তু চলে)।

> 💡 Ollama default-এ শুধু localhost-এ শোনে — বাইরে থেকে access লাগলে `OLLAMA_HOST=0.0.0.0` env var (আমাদের দরকার নেই, API একই মেশিনে)।

### 🖥️ এই মেশিনের কথা (HP EliteBook 840 G8 — office laptop)
- **CPU:** i5-1145G7 (4-core/8-thread) · **RAM:** 16GB (15.7 usable) · **GPU:** Intel Iris Xe **integrated — dedicated GPU নেই**
- ⇒ **CPU-only inference।** Iris Xe Ollama-তে কার্যত কাজে লাগে না; সব CPU-তে চলবে → ধীর (কয়েক token/সেকেন্ড), কিন্তু **চলবে**।
- **PC পুড়বে না — শূন্য ঝুঁকি।** LLM inference = শুধু CPU load (video render/বড় compile-এর মতোই)। গরম হবে, fan ঘুরবে; কিন্তু thermal throttling **নিজেই** চিপ বাঁচায় (গরম হলে speed কমায়, hardware নষ্ট হয় না)। office laptop নিরাপদ।
- **আসল সীমা = RAM**, তাপ নয়। docker stack (SQL Server ~2GB + Redis + Qdrant + Aspire) + API + frontend + IDE একসাথে চললে RAM টাইট → **3b-ই সর্বোচ্চ নিরাপদ default**। 7b+ শুধু docker বন্ধ রেখে একলা টেস্টে (নইলে RAM swap → হ্যাং)।
- সতর্কতা: model চালানোর সময় RAM নজরে রাখো (Task Manager)। swap শুরু হলে (disk 100%) → ছোট model-এ নামো।

---

## ৩. Model setup

### কীভাবে
```powershell
ollama pull <model>     # নামাও (একবারই)
ollama run <model>      # টার্মিনালে চ্যাট করে টেস্ট
ollama list             # কী কী নামানো আছে
ollama rm <model>       # মুছে ফেলা
```

### কোনটা নামাবো (এই মেশিনে — CPU-only, 16GB)

| কাজ | Model | সাইজ (RAM) | এই মেশিনে |
|---|---|---|---|
| **Chat (মূল/default)** | `qwen2.5:3b` | ~2.5GB | ✅ docker stack চললেও নিরাপদ। multilingual (বাংলা তুলনামূলক ভালো) + **tool-calling** (agent-এর জন্য) |
| Chat (smoke/title-gen) | `llama3.2:1b` | ~1.5GB | ✅ দ্রুততম, খুব হালকা |
| Chat (বেশি মান) | `qwen2.5:7b` / `llama3.1:8b` | ~5-6GB | ⚠️ **শুধু docker বন্ধ রেখে** একলা টেস্টে; নইলে RAM swap → হ্যাং |
| **Embedding — বাংলা RAG** | `bge-m3` | ~1.2GB, 1024-dim | ✅ **এটাই ব্যবহার করো** — multilingual, বাংলায় ঠিক discriminate করে |
| Embedding — English-only | `nomic-embed-text` | ~0.5GB, 768-dim | ⚠️ বাংলায় **দুর্বল** (সব ~0.83-এ জট, ranking ভুল) — শুধু ইংরেজি corpus-এ |

> এই মেশিনে সিদ্ধান্ত (2026-07-20 আপডেট): chat local default **`llama3.1:8b`**, embedding **bge-m3** (বাংলা)।
> কঠিন বাংলা auto-route হয়ে **groq**-এ যায় (hybrid routing, নিচে দেখো)। `gemma3:4b`/`qwen3:4b` বাদ দেওয়া হয়েছে।
>
> ⚠️ **শেখা (2026-07-20):** local model বাংলা তুলনা — `qwen2.5:7b` বাংলায় দুর্বল (Chinese char leak), `llama3.1:8b` বাংলা পরিষ্কার। তাই local chat default = `llama3.1:8b`।
>
> ⚠️ **শেখা (2026-07-19):** chat model বাংলা *বলতে* পারা ≠ embedding model বাংলা *বুঝে ভাগ করা*।
> nomic-embed-text দিয়ে বাংলা RAG-এ প্রাসঙ্গিক chunk retrieve হয় না (সব score ~0.83, discrimination নেই)।
> `bge-m3` (multilingual) দিয়ে ঠিক হয় — প্রাসঙ্গিক chunk উপরে ওঠে। **embedding model corpus-ভাষার সাথে মেলাও।**

শুরুতে (এই প্রজেক্টের জন্য):
```powershell
ollama pull llama3.1:8b      # chat (বাংলা local default)
ollama pull bge-m3           # embedding (বাংলা RAG)
```

টেস্ট:
```powershell
ollama run qwen2.5:3b "বাংলায় এক লাইনে বলো: RAG কী?"
```

> ⚠️ **RAM rule of thumb:** model file-এর সাইজ + ~1-2GB। ৮GB RAM মেশিনে 3b-ই সীমা; ১৬GB হলে 7b-8b আরাম।
> ⚠️ ছোট model = হ্যালুসিনেশন বেশি, নির্দেশ কম মানে। Groq/Gemini-র মানের আশা কোরো না — সেটাই trade-off।

---

## ৪. Modelfile — নিজের মতো model

**"Training" এর সহজ ৯০% case এটাই** — model-এর ওজন (weights) বদলানো না, কিন্তু আচরণ বদলানো: system prompt + parameter বেক করে নিজের নামের model বানানো।

`Modelfile` (text file):
```dockerfile
FROM qwen2.5:3b

# আমাদের app-এর ধাঁচ বেক করা — প্রতি request-এ পাঠাতে হয় না
SYSTEM """You are AiPlatform's assistant. Instructions and structure in English,
but always produce free-text output in Bengali (বাংলা). Be concise and factual.
If you don't know, say so — do not invent."""

PARAMETER temperature 0.3
PARAMETER num_ctx 4096
```

বানানো + চালানো:
```powershell
ollama create aiplatform-chat -f .\Modelfile
ollama run aiplatform-chat "আজকের আবহাওয়া নিয়ে কী জানো?"
```

এখন config-এ model নাম `aiplatform-chat` দিলেই আমাদের হাইব্রিড prompt-ধাঁচ default হয়ে যায়।

> 💡 কখন Modelfile, কখন আসল fine-tune? আচরণ/টোন/format বদলাতে চাইলে → Modelfile-ই যথেষ্ট।
> Model-কে **নতুন জ্ঞান বা নতুন দক্ষতা** শেখাতে চাইলে → fine-tune (নিচে) — তবে জ্ঞানের জন্য
> আমাদের কাছে এর চেয়ে ভালো টুল আছেই: **RAG** (ডকুমেন্ট দাও, শিখাতে হয় না)।

---

## ৫. আসল fine-tuning

সোজা কথা: **Ollama নিজে train করে না** — সে শুধু চালায়। Train করতে হয় বাইরে, তারপর ফল Ollama-তে import।

### পথটা (LoRA — practical way)
1. **Data বানাও**: ৫০০-৫০০০ জোড়া instruction→response (JSONL)। মান > পরিমাণ।
   ```json
   {"instruction": "গ্রাহকের বার্তা বিশ্লেষণ করো: ...", "output": "{\"sentiment\": \"negative\", ...}"}
   ```
2. **LoRA fine-tune** — পুরো model না, ছোট adapter train হয় (সস্তা, দ্রুত):
   - সহজতম টুল: **Unsloth** (free Google Colab-এর T4 GPU-তেই 3b model চলে) — নিজের GPU লাগে না
   - বিকল্প: axolotl, LLaMA-Factory
3. **GGUF-এ export** (Unsloth-এ built-in: `save_pretrained_gguf`)
4. **Ollama-তে import**:
   ```dockerfile
   FROM ./my-finetuned-model.gguf
   ```
   ```powershell
   ollama create aiplatform-tuned -f .\Modelfile
   ```
5. Config-এ model নাম বদলাও — ব্যস, app-এ নতুন brain।

### কবে করব
এখন **না** — আগে integration (§৬)। Fine-tune তখনই অর্থবহ যখন:
- নির্দিষ্ট কাজে (যেমন analyzer-এর JSON output) ছোট model বারবার ভুল করছে, এবং
- ভালো ৫০০+ example জমেছে (আমাদের LlmUsage/evals data থেকেই বানানো যাবে)

এটা backlog-এ আলাদা item হবে: *"Analyzer-এর জন্য LoRA fine-tuned 3b model"* — দারুণ learning project।

---

## ৬. AiPlatform-এ integration plan

### যা প্রমাণ করতে চাই
Config-এ provider যোগ + ২টা ছোট Infrastructure ফাইল = পুরো app (chat, stream, RAG, evals, agent) local LLM-এ চলে। **Application layer-এ শূন্য change।**

### ধাপ A — chat provider (সবচেয়ে ছোট রাস্তা)
- Ollama-র OpenAI-compatible endpoint: `http://localhost:11434/v1/chat/completions`
- appsettings-এ:
  ```json
  "ollama": { "Type": "openai", "BaseUrl": "http://localhost:11434/v1/chat/completions", "Model": "qwen2.5:3b" }
  ```
- **একটাই বাধা**: `ChatProviderResolver` key-ছাড়া provider skip করে (hosted-এর জন্য সঠিক নিয়ম)। ছোট fix: local provider চেনার উপায় — `ProviderConfig`-এ `RequiresApiKey` flag (default true), ollama-তে false। Resolver-এ এক লাইনের শর্ত বদল।
- Bearer header-এ dummy value গেলে Ollama পাত্তা দেয় না — সমস্যা নেই।

### ধাপ B — embedding provider (RAG local)
- Ollama-র native embed API: `POST /api/embed` `{ "model": "nomic-embed-text", "input": [...] }`
- নতুন ফাইল: `Infrastructure/Llm/OllamaEmbeddingProvider.cs` (`IEmbeddingProvider` impl — Gemini-রটার আয়নায়)
- Config-এ embedding provider বাছাইয়ের সুইচ (এখন Gemini hardcoded-ish — এখানে ছোট config যোগ)
- ⚠️ **dimension আলাদা** (nomic=768 vs gemini) → Qdrant-এ **আলাদা collection** লাগবে, নইলে মিলবে না। Collection নামে embedding-model suffix রাখা সবচেয়ে সরল।

### ধাপ C — চোখে দেখা
- Provider dropdown-এ (Chat/RAG/Dashboard) `ollama` যোগ — frontend-এ PROVIDERS list
- Pricing config-এ ollama = 0 (দাম নেই) → dashboard-এ cost $0 দেখাবে, token count ঠিকই আসবে

### ধাপ D — verify (দাঁত-পরীক্ষা)
1. Chat: `provider=ollama` → বাংলা উত্তর ✓
2. SSE stream ✓
3. RAG (`method=embedding`, ollama embeddings) → upload→ask, citation ✓
4. Evals: `POST /evals/run?provider=ollama` → pass rate (ছোট model, কম আসবে — সেটাই তথ্য)
5. Agent tool-calls (qwen2.5 tools পারে) — না পারলে জানা limitation হিসেবে নোট
6. **Swap-প্রমাণ**: `Llm:DefaultProvider=ollama` করে পুরো app ঘুরে দেখা — hosted কোথাও লাগছে না

আনুমানিক কাজ: ধাপ A+C ছোট; B মাঝারি (নতুন provider + collection ব্যাপার); D টেস্টিং।

---

## ৭. সীমাবদ্ধতা ও প্রত্যাশা

| বিষয় | বাস্তবতা |
|---|---|
| গতি | GPU ছাড়া 3b-ও ধীর (কয়েক token/সেকেন্ড হতে পারে) — streaming থাকায় UX টিকবে |
| মান | 3b ≪ Groq-এর 70b — বাংলা মাঝে মাঝে ভাঙা হবে, JSON format ভুল হবে বেশি |
| Tool-calling | ছোট model-এ নড়বড়ে — agent টেস্টে ধৈর্য |
| Docker | Ollama compose-এ ঢোকানো **করব না** এখন — host-এ native চালানোই GPU/সরলতার জন্য ভালো; API container থেকে host-এর Ollama ধরতে `host.docker.internal` (পরে দরকার হলে) |
| CI | CI-তে ollama টেস্ট যোগ করব না — model নামানো ভারী; local-first |

---

## Hybrid routing (2026-07-20 চালু)

`ChatCommandHandler` — request-এ `provider` না দিলে + routing on থাকলে `HeuristicModelRouter` complexity দেখে auto-select করে। Config: `appsettings.json` → `Llm:Routing`।

| ইনপুট | রুট → provider | কেন |
|---|---|---|
| সহজ (ছোট, keyword নেই) | `ollama` (`llama3.1:8b`) local | সস্তা, দ্রুত, offline |
| English > `WordThreshold` (৩০) শব্দ | `groq` | reasoning ভারী |
| বাংলা > `BanglaWordThreshold` (১২) শব্দ | `groq` | local বাংলায় দুর্বল, তাই নিচু threshold |
| `ComplexKeywords` match (explain/ব্যাখ্যা/বিশ্লেষণ…) | `groq` | depth signal |

- বাংলা detect: Unicode Bengali block (U+0980–U+09FF) — একটা অক্ষর থাকলেই বাংলা।
- কোন model call হলো: response-এর `provider`/`model` field দেখো।
- Test: `HeuristicModelRouterTests` (৬টা কেস)।

---

*পরের ধাপ: এই plan ধরে ধাপ A থেকে শুরু — যাও বললেই।*
