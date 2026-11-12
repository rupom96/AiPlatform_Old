# সাময়িকভাবে লুকানো জিনিস

এই ফাইলে সেগুলোই আছে যেগুলো **মুছে ফেলা হয়নি, শুধু comment করে রাখা হয়েছে** —
পরে ফিরিয়ে আনতে হবে। প্রতিটা entry-তে: কেন লুকানো, কোন ফাইলের কোন লাইন, কিভাবে ফেরাবে।

নিয়ম: এখানে কিছু যোগ করলে কোডের comment-এও `docs/hidden-features.md` লিখে রাখো,
যেন comment দেখলেই উৎস খুঁজে পাওয়া যায়।

---

## ১. Dashboard page (frontend-only)

**লুকানোর তারিখ:** 2026-11-02
**কেন:** এখন দেখাতে চাই না। **Backend অক্ষত** — `GET /usage`, evals runner, `DashboardPage.tsx`,
`dashboardApi.ts`, `UsageCharts.tsx` সব ফাইল আগের মতোই আছে, শুধু UI থেকে পথ বন্ধ।

**যা comment করা হয়েছে:**

| ফাইল | লাইন | কী |
|---|---|---|
| `frontend/src/App.tsx` | 13 | `DashboardPage` import |
| `frontend/src/App.tsx` | 35 | `<Route path="/dashboard" …>` |
| `frontend/src/components/AppShell.tsx` | 5 | `BarChart3` icon import |
| `frontend/src/components/AppShell.tsx` | 28 | sidebar nav item |
| `frontend/src/features/home/HomePage.tsx` | 3 | `BarChart3` icon import |
| `frontend/src/features/home/HomePage.tsx` | 13 | home page-এর card |

icon import-ও comment করতে হয়েছে, নইলে unused import TypeScript build ভাঙত।

**ফেরানোর নিয়ম:** উপরের ৬ জায়গার comment তুলে দাও। নতুন কিছু লিখতে হবে না —
মূল লাইন হুবহু comment-এর ভেতরেই আছে।

---

## ২. cerebras ও mistral provider (frontend dropdown)

**লুকানোর তারিখ:** 2026-11-02
**কেন:** দুটোই account-এর অবস্থার কারণে অচল, কোডের দোষ নয় —

- **cerebras** → `402 payment required` (quota/billing শেষ)
- **mistral** → `429 rate limited`

**যা comment করা হয়েছে:** `frontend/src/lib/providers.ts` লাইন ৭–৮।

```ts
// export const PROVIDERS = ['cerebras', 'groq', 'gemini', 'mistral', 'ollama'] as const
export const PROVIDERS = ['groq', 'gemini', 'ollama'] as const
```

এক লাইন বদলালেই Chat / RAG / Settings / Dashboard — চার জায়গার dropdown-ই বদলায়
(তালিকা এক জায়গায় রাখা আছে, সেটারই লাভ)।

**Backend অক্ষত:** `backend/src/Api/appsettings.json`-এ `cerebras` ও `mistral` দুটোই
রয়ে গেছে (`Llm:Providers`), fallback chain-এও cerebras প্রথমে আছে। API-তে
`"provider": "mistral"` পাঠালে এখনো কাজ করার চেষ্টা করবে — শুধু UI থেকে বেছে নেওয়া যাবে না।

**ফেরানোর নিয়ম:** লাইন ৭-এর comment তুলে লাইন ৮ মুছে দাও। তার আগে যাচাই করো:

```bash
Invoke-RestMethod -Method Post -Uri "http://localhost:5076/api/v1/chat" -ContentType "application/json" -Body '{"message":"ping","provider":"cerebras"}'
```

502 এলে provider এখনো অচল — ফেরানোর সময় হয়নি।

**⚠️ একটা খুঁট:** default provider `localStorage`-এ `provider` key-তে জমা থাকে
(`features/settings/uiSlice.ts:18`)। কারো ওখানে আগে থেকে `mistral` বা `cerebras` জমা
থাকলে dropdown ফাঁকা দেখাবে। Settings থেকে একবার নতুন provider বেছে নিলেই ঠিক হয়ে যায়।

---

## ৩. RAG retriever: keyword / embedding / hybrid (frontend dropdown)

**লুকানোর তারিখ:** 2026-11-05
**কেন:** তিনটাই `KnowledgeBaseSeed.Documents` (কোডে হাতে লেখা ৯টা বাংলা নোট) ছাড়া কিছু দেখে না —
**আপলোড করা ডকুমেন্ট ওদের কাছে অদৃশ্য।** তাই নিজের ফাইল নিয়ে প্রশ্ন করলে ওরা সবসময়
"তথ্য নেই" বলে, আর মনে হয় ingestion ভেঙেছে।

| retriever | কোথা থেকে পড়ে | আপলোড করা doc দেখে? |
|---|---|---|
| `keyword` | [KeywordRetriever.cs:20](../backend/src/Infrastructure/Rag/KeywordRetriever.cs) → seed | ❌ |
| `embedding` | [EmbeddingRetriever.cs:43](../backend/src/Infrastructure/Rag/EmbeddingRetriever.cs) → seed | ❌ |
| `hybrid` | keyword + embedding, দুটোই seed | ❌ |
| **`qdrant`** | Qdrant collection (seed + আপলোড দুটোই) | ✅ |

**যা comment করা হয়েছে:** `frontend/src/features/rag/RagPage.tsx` লাইন ২০–২৩।

```ts
// const METHODS = ['keyword', 'embedding', 'qdrant', 'hybrid']
const METHODS = ['qdrant']
```

সাথে default-ও বদলেছে: `useState('embedding')` → `useState(METHODS[0])`। আগেরটা থাকলে
dropdown-এ এমন একটা মান বসত যা তালিকায় নেই, ফলে ফাঁকা দেখাত।

**Backend অক্ষত:** চারটা retriever-ই রেজিস্টার করা আছে
([DependencyInjection.cs:154](../backend/src/Infrastructure/DependencyInjection.cs))।
API-তে `"method": "hybrid"` পাঠালে এখনো চলবে — শুধু UI থেকে বেছে নেওয়া যাবে না।
`AiPlatform.http`-এর চারটা method-ই আগের মতো কাজ করে।

**ফেরানোর আগে যা ঠিক করা দরকার:** `hybrid`-কে কাজের করতে হলে তার দ্বিতীয় উপাদান
`EmbeddingRetriever` → `QdrantRetriever` করতে হবে ([HybridRetriever.cs:10](../backend/src/Infrastructure/Rag/HybridRetriever.cs))।
তখন hybrid = keyword (বিরল শব্দ/কোড ধরে) + qdrant (অর্থ ধরে), যা আসল উদ্দেশ্য ছিল —
আর সেটাই qdrant-এর চেয়ে ভালো হবে। `DependencyInjection.cs`-এ `QdrantRetriever`-কে
concrete singleton হিসেবেও রেজিস্টার করতে হবে (এখন শুধু `IRetriever` হিসেবে আছে)।

`keyword` ও `embedding` শেখার যন্ত্র — একই প্রশ্নে শব্দ-মিল আর অর্থ-মিলের পার্থক্য
চোখে দেখার জন্য। ফেরালে ওই উদ্দেশ্যেই।

**ফেরানোর নিয়ম:** লাইন ২২-এর comment তুলে ২৩ মুছে দাও। default লাইনটা `METHODS[0]`-ই
থাক — ওটা তালিকার সাথে নিজে থেকে মিলে যায়।

---

## (৪) ollama — Local / GPU সার্ভার toggle

**তারিখ:** 2026-11-11
**কেন:** কোম্পানি একটা GPU সার্ভার (trial) নিয়েছে, সেখানে ollama-তে `qwen2.5:7b` ও
`llama3.1:8b` আছে। Chat/RAG-এ dropdown-এ "ollama" একটাই থাকে, পাশের toggle ঠিক করে
কোন host-এ যাবে।

এটা **লুকানো feature নয়** — চালু আছে। এখানে লিখে রাখলাম কারণ host বদলালে কী কী
ছুঁতে হবে তা এক জায়গায় থাকা দরকার।

| কী | কোথায় |
|---|---|
| দুই provider config | `backend/src/Api/appsettings.json` → `Llm:Providers:ollama` ও `ollama-server` |
| toggle → provider নাম | `frontend/src/lib/providers.ts` → `effectiveProvider()` |
| UI toggle | `ChatPage.tsx`, `RagPage.tsx` (দুটোতেই) |
| model তালিকা | `GET /llm/models?provider=…` → `OllamaModelCatalog` |

**host বদলাতে** (কোড ছোঁয়া ছাড়া):
```
Llm__Providers__ollama-server__BaseUrl = http://NEW-HOST:PORT/v1/chat/completions
```

**গুরুত্বপূর্ণ:**
- toggle ঘোরালে model তালিকা **নতুন করে** আনা হয় — দুই host-এ আলাদা model থাকতে পারে।
  `currentData` ব্যবহার করা হয়েছে (`data` নয়), নইলে আগের host-এর তালিকা দেখাত।
- তালিকা খালি এলে UI লাল লেখায় বলে "পৌঁছানো যাচ্ছে না" — চুপ করে থাকে না।
- model তালিকার call-এ **৫ সেকেন্ডের** আলাদা timeout (`OllamaModelCatalog.ListTimeout`)।
  "llm" HttpClient-এর ৫ মিনিট এখানে খাটে না — host অফ থাকলে dropdown ঝুলে থাকত।
- provider নাম configure করা key হিসেবেই খোঁজা হয়, URL হিসেবে নয় → SSRF সম্ভব নয়
  (test: `OllamaModelCatalogTests`)।
