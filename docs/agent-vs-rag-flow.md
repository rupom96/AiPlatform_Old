# Agent (SQL) vs RAG (Document) — কীভাবে উত্তর আসে

> Beginner-friendly। এই প্রজেক্টে LLM **নিজে Database খোলে না**।  
> দুটো আলাদা রাস্তা আছে — ডায়াগ্রাম দেখেই বোঝা যাবে কোন প্রশ্নে কোন রাস্তা।

সম্পর্কিত: [architecture.md](architecture.md) · [erp-integration-plan.md](erp-integration-plan.md)

---

## ০. এক নজরে — কোন প্রশ্ন কোন রাস্তা?

```mermaid
flowchart TD
    Q["তুমি প্রশ্ন করলে"] --> T{প্রশ্নের ধরন?}

    T -->|"বিক্রি / স্টক / অর্ডার<br/>উদাহরণ: highest sold product কোনটা?"| A["রাস্তা A — Agent + Tools"]
    T -->|"আপলোড করা ডক / নীতিমালা<br/>উদাহরণ: ছুটি কতদিন?"| R["রাস্তা B — RAG"]

    A --> SQL[("SQL Server ERP DB<br/>SalesOrder · Product · Stock…")]
    R --> VDB[("Qdrant Vector DB<br/>chunk + embedding")]

    SQL --> LLM1["LLM শুধু উত্তর সাজায়"]
    VDB --> LLM2["LLM chunk পড়ে উত্তর লেখে"]

    LLM1 --> ANS["তোমার কাছে বাংলা উত্তর"]
    LLM2 --> ANS
```

| | রাস্তা A — Agent | রাস্তা B — RAG |
|---|---|---|
| কখন | সংখ্যা, টেবিল, ERP | লেখা ডকুমেন্টের অর্থ |
| ডেটা কোথায় | SQL Server (`Erp` DB) | Qdrant (+ SQL-এ শুধু ফাইল মেটা) |
| LLM কী করে | কোন **টুল** ডাকবে ঠিক করে | আনা **chunk** পড়ে উত্তর লেখে |
| UI | Chat → **Agent mode ON** | **RAG** পেজ (বা RAG API) |
| API | `POST /api/v1/agent` | `POST /api/v1/rag` |

---

## ১. ভুল ধারণা ভাঙো (খুব জরুরি)

```mermaid
flowchart LR
    subgraph wrong["❌ এমন হয় না"]
        L1["LLM"] -->|"নিজে গিয়ে SQL লেখে"| DB1[("Database")]
        L1 -->|"নিজে Qdrant খোলে"| Q1[("Qdrant")]
    end

    subgraph right["✅ এই প্রজেক্টে যা হয়"]
        L2["LLM"] -->|"টুল ডাকো / এই টেক্সট পড়ো"| APP["তোমার .NET কোড"]
        APP -->|"EF Core query"| DB2[("SQL ERP")]
        APP -->|"vector search"| Q2[("Qdrant")]
        APP -->|"টুলের ফল / chunk"| L2
    end
```

**মনে রাখো:** LLM = মগজ (বোঝা + বাক্য সাজানো)।  
**হাত** = Agent Tool কোড। **চশমা** = RAG-এ খুঁজে আনা টেক্সট।

---

# রাস্তা A — Agent + SQL (ERP প্রশ্ন)

উদাহরণ প্রশ্ন: **“highest sold product কোনটা দেখাও”**

## A1. পুরো যাত্রা (sequence)

```mermaid
sequenceDiagram
    autonumber
    actor U as তুমি (Browser)
    participant FE as React ChatPage<br/>(Agent mode ON)
    participant API as Api<br/>POST /api/v1/agent
    participant H as RunAgentCommandHandler
    participant LLM as LLM<br/>(gemini / groq / ollama…)
    participant Tool as GetSalesTool<br/>(C# কোড)
    participant ERP as SQL Server<br/>AiPlatformErp

    U->>FE: "highest sold product কোনটা?"
    FE->>API: Bearer JWT + message + provider
    API->>H: RunAgentCommand

    Note over H,LLM: System prompt + টুল তালিকা<br/>(get_sales, get_stock, …)

    H->>LLM: প্রশ্ন + available tools
    LLM-->>H: tool_call → get_sales(…)

    Note over H,Tool: LLM DB-তে যায়নি —<br/>শুধু বলেছে কোন টুল চালাতে

    H->>Tool: Execute(arguments)
    Tool->>ERP: EF Core LINQ / SQL<br/>SalesOrderDetails + Product …
    ERP-->>Tool: rows (product, qty, amount…)
    Tool-->>H: পরিষ্কার JSON/টেক্সট সারাংশ

    H->>LLM: "টুলের ফল এটা — এখন উত্তর দাও"
    LLM-->>H: বাংলায় সাজানো উত্তর
    H-->>FE: answer + toolsUsed + latency
    FE-->>U: উত্তর + toolsUsed badge
```

## A2. ভেতরে কী কী টুকরো জড়ো হয়

```mermaid
flowchart TB
    subgraph ui["1️⃣ Frontend"]
        CHAT["ChatPage"]
        TOG["Agent toggle = ON"]
        CHAT --> TOG
        TOG --> REQ["POST /agent<br/>না /chat/stream"]
    end

    subgraph backend["2️⃣ Backend Agent loop"]
        VAL["FluentValidation"]
        LOOP["Agentic loop<br/>(MaxIterations ≈ 5)"]
        RESOLVE["IChatProviderResolver<br/>→ IToolCallingProvider"]
        VAL --> LOOP --> RESOLVE
    end

    subgraph llm_box["3️⃣ LLM এর কাজ"]
        THINK["প্রশ্ন পড়ে সিদ্ধান্ত"]
        PICK["টুল বাছা: get_sales"]
        WRITE["শেষে মানুষের ভাষায় উত্তর"]
        THINK --> PICK --> WRITE
    end

    subgraph tools["4️⃣ Tools = আসল DB হাত"]
        GS["get_sales → বিক্রি"]
        GST["get_stock → স্টক"]
        GW["get_warranty → ওয়ারেন্টি"]
        CSO["create_sales_order → ড্রাফট অর্ডার"]
        CT["current_time"]
        SK["search_knowledge_base<br/>(KB টেক্সট — ERP নয়)"]
    end

    subgraph data["5️⃣ Data"]
        ERP[("Erp DbContext<br/>SalesOrders / Products<br/>StockBalances / …")]
        APPDB[("App DbContext<br/>users / usage — এখানে বিক্রি নেই")]
    end

    REQ --> VAL
    RESOLVE --> THINK
    PICK --> GS
    GS --> ERP
    GST --> ERP
    GW --> ERP
    CSO --> ERP
    ERP --> WRITE
```

## A3. “jumbled objects” নিয়ে কী হয়?

```mermaid
flowchart LR
    RAW["SQL rows<br/>অনেক কলাম, অনেক সারি"] --> TOOL["GetSalesTool<br/>filter · group · sort · limit"]
    TOOL --> CLEAN["ছোট পরিষ্কার ফল<br/>নাম, পরিমাণ, টাকা…"]
    CLEAN --> LLM["LLM পড়ে<br/>বাংলা বাক্য বানায়"]
```

- কাঁচা পুরো টেবিল LLM-কে দেওয়া হয় **না**।
- টুলই হিসাব/ফিল্টার করে → LLM শুধু পড়ে ও বলে।

## A4. এই রাস্তায় Qdrant লাগে কি?

```mermaid
flowchart TD
    Q["highest sold product?"] --> A["Agent"]
    A --> SQL[("SQL ERP ✅")]
    A -.->|না| QDR[("Qdrant ❌ এ প্রশ্নে লাগে না")]
```

**না।** বিক্রি/স্টক = স্ট্রাকচার্ড টেবিল → SQL Tool। Vector search এখানে অপ্রয়োজনীয়।

---

# রাস্তা B — RAG (ডকুমেন্ট আপলোড + প্রশ্ন)

উদাহরণ: নীতিমালা `.txt` আপলোড → পরে জিজ্ঞাসা **“ছুটি কতদিন?”**

RAG = **R**etrieval **A**ugmented **G**eneration  
অর্থ: আগে খুঁজে টেক্সট আনো → পরে LLM দিয়ে উত্তর লেখো।

## B1. অংশ দুইটা — Upload আর Ask আলাদা সময়

```mermaid
flowchart LR
    subgraph phase1["фаза ১ — আপলোড / ইনজেস্ট<br/>(একবার বা যখন নতুন ফাইল)"]
        UP["Upload .txt/.md"] --> CHUNK["Chunk"]
        CHUNK --> EMB1["Embedding"]
        EMB1 --> QD[("Qdrant-এ সংরক্ষণ")]
    end

    subgraph phase2["фаза ২ — প্রশ্ন<br/>(যখন তুমি জিজ্ঞাসা করো)"]
        ASK["প্রশ্ন"] --> EMB2["প্রশ্নের Embedding"]
        EMB2 --> SEARCH["Qdrant-এ মিল খোঁজা"]
        SEARCH --> CTX["Top chunks = context"]
        CTX --> GEN["LLM grounded উত্তর + citation"]
    end

    phase1 -.->|"পরে"| phase2
```

## B2. Upload → Ingestion (বিস্তারিত sequence)

```mermaid
sequenceDiagram
    autonumber
    actor U as তুমি
    participant FE as DocumentsPage
    participant API as DocumentsController
    participant SQL as SQL Server<br/>(App DB)
    participant RS as Redis Streams
    participant W as IngestionBackgroundService
    participant EMB as Embedding provider<br/>(gemini / ollama bge-m3)
    participant Q as Qdrant

    U->>FE: ফাইল বাছো (.txt / .md)
    FE->>API: POST /api/v1/documents<br/>+ Idempotency-Key
    API->>SQL: Document row<br/>Status=Pending, TenantId
    API->>RS: XADD (documentId)
    API-->>FE: 202 + documentId
    Note over FE: তুমি মুক্ত — status poll করতে পারো

    RS->>W: XREADGROUP
    W->>SQL: Status = Processing
    W->>W: ফাইল পড়ো → ছোট chunk-এ ভাগ

    loop প্রতিটি chunk
        W->>EMB: embed(chunk text)
        EMB-->>W: vector (সংখ্যার তালিকা)
        W->>Q: upsert point<br/>(deterministic point-id)
        W->>SQL: ProcessedChunks++
    end

    W->>SQL: Status = Completed
    W->>RS: XACK
    Note over RS,W: crash → unacked message reclaim<br/>একই point-id = duplicate নয় (idempotent)
```

## B3. Upload এর পর ডেটা কোথায় থাকে?

```mermaid
flowchart TB
    FILE["policy.txt"] --> META["SQL App DB<br/>Document: id, name, status,<br/>tenant, soft-delete…<br/>= মেটা / অগ্রগতি"]
    FILE --> CHUNKS["Chunk 1 · Chunk 2 · Chunk 3…"]
    CHUNKS --> VECS["Embedding vectors"]
    VECS --> QDR[("Qdrant collection<br/>aiplatform_rag…<br/>= খোঁজার যোগ্য টেক্সট")]
```

- **SQL** = ফাইলের রেকর্ড ও স্ট্যাটাস (বিক্রির হিসাব নয়)।
- **Qdrant** = অর্থ-মিলিয়ে খোঁজার জন্য ভেক্টর + chunk টেক্সট।

## B4. প্রশ্ন → উত্তর (RAG query sequence)

```mermaid
sequenceDiagram
    autonumber
    actor U as তুমি
    participant FE as RagPage
    participant API as POST /api/v1/rag
    participant H as RagQueryCommandHandler
    participant R as IRetriever<br/>(keyword / embedding / qdrant / hybrid)
    participant Q as Qdrant
    participant EMB as Embedding
    participant LLM as Chat LLM

    U->>FE: "ছুটি কতদিন?" + method + provider
    FE->>API: Bearer JWT
    API->>H: RagQueryCommand

    alt method = keyword
        H->>R: KnowledgeBaseSeed-এ শব্দ মিল
    else method = embedding / qdrant
        H->>EMB: embed(প্রশ্ন)
        H->>R: vector search
        R->>Q: nearest chunks
        Q-->>R: top-k + score
    else method = hybrid
        H->>R: keyword + embedding
        R->>R: RRF দিয়ে merge/rerank
    end

    R-->>H: relevant chunks<br/>[1] [2] [3]…

    Note over H,LLM: Prompt: English instruction +<br/>"উত্তর বাংলায়" + শুধু এই context

    H->>LLM: grounded prompt + chunks
    LLM-->>H: উত্তর + [n] citation
    H-->>FE: answer + sources + timing
    FE-->>U: উত্তর + সোর্স তালিকা
```

## B5. Embedding মানে কী? (সরল ছবি)

```mermaid
flowchart LR
    T1["টেক্সট:<br/>বার্ষিক ছুটি ১৪ দিন"] --> V1["ভেক্টর:<br/>[0.12, -0.44, …]"]
    T2["প্রশ্ন:<br/>ছুটি কতদিন?"] --> V2["ভেক্টর:<br/>[0.11, -0.40, …]"]
    V1 --> SIM["কোসাইন সিমিলারিটি<br/>কাছাকাছি = মিল বেশি"]
    V2 --> SIM
    SIM --> HIT["Chunk বেছে নেওয়া"]
```

LLM এখানে SQL `SELECT` চালায় না — **অর্থের কাছাকাছি টেক্সট** আনে।

## B6. Hybrid method কী করে?

```mermaid
flowchart TD
    Q["প্রশ্ন"] --> K["Keyword retriever<br/>শব্দ মিল"]
    Q --> E["Embedding retriever<br/>অর্থ মিল"]
    K --> L1["লিস্ট A"]
    E --> L2["লিস্ট B"]
    L1 --> RRF["Reciprocal Rank Fusion<br/>দুই লিস্ট মিলিয়ে সাজাও"]
    L2 --> RRF
    RRF --> TOP["Final top chunks"]
    TOP --> LLM["LLM"]
```

নোট: RRF কোনো model দিয়ে ডক “পড়ে” না — শুধু দুই র‍্যাঙ্ক লিস্ট মেশায়। (ভবিষ্যৎ cross-encoder reranker আলাদা জিনিস — TODO backlog।)

---

# পাশাপাশি তুলনা (এক পাতায়)

```mermaid
flowchart TB
    subgraph A["Agent — highest sold product"]
        direction TB
        A1["Chat + Agent ON"] --> A2["LLM: get_sales ডাকো"]
        A2 --> A3["C# Tool"]
        A3 --> A4[("SQL ERP")]
        A4 --> A5["পরিষ্কার ফল"]
        A5 --> A6["LLM উত্তর লেখে"]
    end

    subgraph B["RAG — ছুটি কতদিন?"]
        direction TB
        B0["আগে: upload → chunk → embed → Qdrant"]
        B1["RagPage প্রশ্ন"] --> B2["Retriever খুঁজে chunk আনে"]
        B2 --> B3[("Qdrant / KB")]
        B3 --> B4["context prompt"]
        B4 --> B5["LLM উত্তর + citation"]
        B0 -.-> B1
    end
```

| প্রশ্ন | LLM কি DB টেবিলে যায়? | Vector DB? | মূল কম্পোনেন্ট |
|---|---|---|---|
| highest sold product | না — Tool যায় | না | `RunAgent` + `GetSalesTool` + `ErpDbContext` |
| ছুটি কতদিন (uploaded doc) | না | হ্যাঁ Qdrant | upload worker + `RagQuery` + retriever |
| সাধারণ গল্প/চাট | না | সাধারণত না | `POST /chat` বা `/chat/stream` (memory/cache থাকতে পারে) |

---

# কোড ম্যাপ (খুঁজতে গেলে)

| ধাপ | ফাইল / জায়গা |
|---|---|
| Agent API | `backend/src/Api/Controllers/AgentController.cs` |
| Agent loop | `backend/src/Application/Features/Agent/RunAgentCommandHandler.cs` |
| Sales tool | `backend/src/Infrastructure/Agent/GetSalesTool.cs` |
| Stock tool | `backend/src/Infrastructure/Agent/GetStockTool.cs` |
| ERP DB | `backend/src/Infrastructure/Persistence/` + `Domain/Entities/Erp/` |
| RAG API | `backend/src/Api/Controllers/RagController.cs` |
| RAG handler | `backend/src/Application/Features/Rag/RagQueryCommandHandler.cs` |
| Upload | `DocumentsController` + `UploadDocumentCommandHandler` |
| Ingestion queue | Redis Streams + `IngestionBackgroundService` |
| Vector store | `Infrastructure` Qdrant (`IVectorStore`) |
| Chat Agent UI | `frontend/src/features/chat/ChatPage.tsx` (agent toggle) |
| RAG UI | `frontend/src/features/rag/RagPage.tsx` |
| Docs UI | `frontend/src/features/documents/DocumentsPage.tsx` |

---

# নিজে হাতে দেখার ছোট চেকলিস্ট

1. **Agent/SQL:** Chat → Agent ON → “জুলাই ২০২৬-এর বিক্রি কত?” → উত্তরের নিচে `toolsUsed` (যেমন `get_sales`) দেখো।  
2. **RAG:** Documents-এ `samples/sample-kb-bn.txt` আপলোড → Completed হোক → RAG পেজে ছুটি/দাম জিজ্ঞাসা → citation/source দেখো।  
3. দুটো মিশিয়ে ভাবো না — **এক প্রশ্ন = এক রাস্তা**।

---

# টুল catalog (Tool RAG) — ডক collection থেকে আলাদা

টুলের `Description` **কোডে** থাকে (`Infrastructure/Agent/*Tool.cs`)।  
App start (+ প্রতি agent call) → name+description embed → Qdrant collection **`aiplatform_tools`** (docs-এর `aiplatform_rag` নয়)।

- টুল সংখ্যা ≤ `Agent:ToolTopK` → prompt-এ সব টুল (তবু sync হয়)।  
- টুল বেশি → প্রশ্ন দিয়ে top-k retrieve → শুধু সেগুলো LLM মেনুতে।  
- **নতুন টুল:** `ITool` ক্লাস + DI register + **restart** → অটো upsert। Doc upload লাগে না।

Config: `Agent:ToolRetrievalEnabled`, `ToolTopK`, `ToolsCollection`।

---

# এক লাইনের সারাংশ

> **সংখ্যা/অর্ডার/স্টক** → Agent টুল → **SQL ERP**।  
> **ডকুমেন্টের লেখা** → Upload+Embed → **Qdrant** → RAG → LLM।  
> **অনেক টুল** → descriptions → আলাদা Qdrant collection → top-k মেনু।  
> **LLM কখনো একা DB ব্রাউজ করে না** — অ্যাপ তাকে টুল বা context ধরিয়ে দেয়।
