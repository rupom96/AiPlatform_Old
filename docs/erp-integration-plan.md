# ERP AI Integration Plan

> **2026-07-20 — কাজ শুরু হলো।** Ollama integration শেষ, hybrid routing live। schema চূড়ান্ত (নিচে §Schema), `ErpDbContext` + entity বানানো চলছে।
>
> **2026-07-25 — Warranty যোগ।** `ProductWarranty` table (১৬তম) + `get_warranty` tool। কেন এক table, তিনটা নয় — §Warranty।



> User (2026-07-18) reminder: sample ERP database নিয়ে RAG + tool-calling **দুটোই** implement করব।
> লক্ষ্য উদাহরণ: "জুলাই মাসের মোট বিক্রি কত?" (tool → DB query) + "করিম ট্রেডার্সের জন্য ১০ বস্তা
> সিমেন্ট, ৳৮০০০ এর invoice বানাও" (tool → invoice create)। **এখন না — Ollama integration আগে।**

## কী লাগবে (AI ক্ষমতা বানানো নেই — সব আছে; কাজ = ERP-তে জোড়া)

RAG (Phase 3) + tool-calling agent (7h) + multi-tenancy (7d) + guardrails (6d) + memory (6f) — সব তৈরি।
নতুন কাজ = tool গুলো **আসল/sample ERP database-এ যুক্ত করা** + irreversible action-এ confirm।

## যেসব ফাইলে হাত পড়বে (কাজ শুরুর সময় এখান থেকে দেখব)

### A. RAG দিয়ে ERP data Q&A (নিয়ম/policy/manual — text)
- `backend/src/Infrastructure/Rag/KnowledgeBaseSeed.cs` — অথবা document upload দিয়ে ERP doc ঢুকবে (নতুন seed নয়, upload flow-ই যথেষ্ট)
- বিদ্যমান RAG pipeline অপরিবর্তিত — শুধু ERP ডকুমেন্ট feed
- ⚠️ structured row (salary/invoice টেবিল) RAG-এ দুর্বল → ওগুলো tool-calling-এ

### B. Tool-calling — ERP-র সংখ্যা ও কাজ (নতুন ITool impl)
নতুন tool ফাইল (demo tool-এর আয়নায় — `Infrastructure/Agent/CurrentTimeTool.cs`, `KnowledgeBaseSearchTool.cs` দেখো):
- `backend/src/Infrastructure/Agent/` — নতুন ERP tools:
  - `GetTotalSalesTool.cs` (month → DB SUM query) — read tool
  - `GetEmployeeSalaryTool.cs` (name/id → row lookup) — read tool, ⚠️ permission-scoped
  - `CreateInvoiceTool.cs` (customer, items, amount → insert) — **write tool, confirm লাগবে**
- প্রতিটা `ITool` implement করে (`Application/Common/Interfaces/ITool.cs`)
- DI register: `Infrastructure/DependencyInjection.cs` (demo tool যেভাবে আছে)
- MCP bridge (ঐচ্ছিক): `Api/Mcp/AiPlatformMcpTools`-এ যোগ (7h-ii প্যাটার্ন)

### C. Sample ERP database
- নতুন EF entity/DbContext বা আলাদা read-only connection — sample schema (customers, products, invoices, employees, salaries)
- সিদ্ধান্ত পরে: AiPlatform-এর AppDbContext-এ ঢুকবে নাকি আলাদা "ErpDbContext"/আলাদা DB (সম্ভবত আলাদা — ERP নিজের data, auth থেকে বিচ্ছিন্ন)
- Migration লাগবে; seed data (কিছু customer/invoice/employee)

### D. Guardrails (write action safety)
- irreversible tool (invoice create/delete) → agent execute করার আগে **user confirm** (frontend + backend flag)
- বিদ্যমান `IPromptGuard` (6d) input-এ; এটা **action-level** নতুন গার্ড
- audit log (7d-ii) — কোন AI-action কে করল

### E. Frontend
- চ্যাট UI-তে agent mode (7h `/agent` endpoint আছে) — ERP প্রশ্ন ওখানে
- tool-call চলাকালীন "invoice বানাচ্ছি... confirm?" UI

## ক্রম
1. ~~Sample ERP DB + entity~~ ✅ 2026-07-22 (১৫ table, ErpDbContext, seed)
2. ~~Read tools~~ ✅ 2026-07-22 — `get_sales` + `get_stock`; ✅ 2026-07-25 — `get_warranty` (+ `ProductWarranty` table)
3. RAG-এ ERP doc feed (A) — বাকি
4. ~~Write tool~~ ✅ 2026-07-25 — `create_sales_order` (খসড়া-only; confirm-flow লাগেনি, §Write tool)
5. ~~Frontend agent UX~~ ✅ 2026-07-22 (Chat page-এ Agent toggle + toolsUsed badge + timing)

## Tool design নীতি (2026-07-22, শেখা)

**প্রতি প্রশ্নের ধরনে আলাদা tool নয় — এক tool + ঐচ্ছিক filter।** `get_total_sales_by_customer`,
`get_total_sales_by_product`… এভাবে গেলে ২০টা tool হতো (*tool explosion*), আর LLM প্রায়-একরকম
নামের ভিড়ে ভুল বাছত। নিয়ম:

> একই টেবিল, শুধু ভিন্ন ছাঁকনি → **একই tool, নতুন parameter**।
> ভিন্ন প্রশ্নের ধরন, ভিন্ন উত্তরের আকার → **নতুন tool**।

| Tool | কী জানায় | Parameter |
|---|---|---|
| `get_sales` | কত বিক্রি হয়েছে | fromDate, toDate (আবশ্যক) + customerName, productName, location, groupBy, topN |
| `get_stock` | এখন কী কত আছে | productName, location, belowQuantity, includeSerials (সব ঐচ্ছিক) |
| `get_warranty` | warranty আছে কি না, থাকলে কবে শেষ | serialNo / invoiceNo / productName (অন্তত একটা) |
| `create_sales_order` | **write** — খসড়া order বানায় | customerName, location, items[] (সব আবশ্যক) + orderDate |

সবগুলোর গঠন ইচ্ছাকৃতভাবে এক রকম (একই validation, একই JSON আকার, ভুলে throw নয় — `{"error"}` ফেরত
যাতে LLM শুধরে আবার ডাকতে পারে)। আচরণ আলাদা হলে LLM-ও গুলিয়ে ফেলে।

**⚠️ EF ফাঁদ (দুবার কামড়েছে):** query-র `Select`-এ **positional record constructor** ব্যবহার করলে
EF SQL-এ নামাতে পারে না ("could not be translated") — **object-initializer সহ class** লাগে।

**সীমা:** fixed tool দিয়ে সাধারণ ERP প্রশ্নের ~৮০-৯০% মেটে। "গত ছয় মাসে ৩ বারের বেশি কেনা
customer-দের brand-ভিত্তিক লাভ" ধরনের প্রশ্নে হয় নতুন report tool, নয় text-to-SQL লাগবে।
**text-to-SQL এখন নয়** — ভুল join = ভুল সংখ্যা, tenant ফাঁস, ভারী query; ছোট model খারাপ SQL লেখে।

## Schema — চূড়ান্ত (2026-07-20, ১৫ table; 2026-07-25-এ ProductWarranty যোগ হয়ে ১৬)

sample ERP = **IT পণ্যের ব্যবসা** (computer, laptop, accessories)। multi-tenant (Company → Location), approval-driven stock.

### Convention
- সব PK/FK = `bigint identity`; টাকা = `decimal(18,2)`
- প্রতি table-এ `CreatedAt datetime`, `CreatedBy bigint` (ErpUser-এর id, plain column — ১৫টা cascade path এড়াতে FK নয়)
- **`Code` শুধু**: Company, Location, Supplier, Customer, ErpUser, `PurchaseOrder.PurchaseOrderNo`, `SalesOrder.SalesOrderNo`, `SalesOrder.InvoiceNo` — Product/detail/serial-এ Code নেই
- Code pattern: `SUP-{Company.Code}-{Location.Code}-{Year}-{000001}` — Company+Location wise unique
- Detail/Serial table তাদের header থেকে tenancy পায় (নিজেদের CompanyId/LocationId নেই)
- **Product location-wise আলাদা row** — একই laptop HO আর CB-তে দুই ProductId

### Table তালিকা
| # | Table | ভূমিকা |
|---|---|---|
| 1 | Company | tenant root |
| 2 | Location | branch (HO/MB/CB) |
| 3 | ErpUser | ERP-র কর্মী; `ApprovedBy`/`CreatedBy` reference (auth নেই) |
| 4 | Product | IT পণ্য, `IsSerialized` flag |
| 5 | Supplier | Purchase-এর party |
| 6 | Customer | Sales-এর party |
| 7-8-9 | PurchaseOrder → Detail → Serial | stock **IN** |
| 10-11-12 | SalesOrder → Detail → Serial | stock **OUT** |
| 13 | CurrentStock | **ledger** — প্রতি approved movement-এ এক row |
| 14 | CurrentStockDetail | গুদামে এই মুহূর্তে থাকা প্রতিটা serial |
| 15 | StockBalance | **summary** — Product+Location wise চলতি ব্যালেন্স |
| 16 | ProductWarranty | product-এ কী warranty আসে (type + মাস) — 2026-07-25 |

### Stock movement (শুধু approval-এ ঘটে)
```
Purchase approved:  INSERT CurrentStock (Quantity = +qty, Cost = UnitPrice, ReferenceNo = PurchaseOrderNo)
                    StockBalance.QuantityOnHand += qty
                    প্রতি PurchaseOrderSerial -> INSERT CurrentStockDetail
Sales approved:     InvoiceNo assign
                    INSERT CurrentStock (Quantity = -qty, Cost = কেনার খরচ, ReferenceNo = SalesOrderNo)
                    StockBalance.QuantityOnHand -= qty
                    প্রতি SalesOrderSerial -> DELETE CurrentStockDetail (serial গুদাম ছাড়ল)
Non-serialized:     শুধু quantity +/- , serial row নেই
Unapproved:         stock-এ কোনো প্রভাব নেই
```
- invariant: `StockBalance.QuantityOnHand == SUM(CurrentStock.Quantity)` (একই Product+Location)
- **`CurrentStock.Cost` সবসময় কেনার খরচ** (বিক্রয়মূল্য নয়) — এই column stock **valuation**-এর জন্য। বিক্রয়মূল্য `SalesOrderDetail.UnitPrice`-এ আছে; লাভ = দুইয়ের পার্থক্য। বিক্রয়মূল্য বসালে গুদাম খালি হয়েও stock value **negative** দেখাত।
- একই পণ্য ভিন্ন দামে কেনা হলে: **serialized** → `CurrentStockDetail` দিয়ে ঐ serial-এর হুবহু cost; **non-serialized** → average cost

## Write tool — `create_sales_order` (2026-07-25)

**Tool কখনো approve করে না।** এটাই মূল guardrail, আলাদা confirm-flow ছাড়াই।

schema-তে approve-ই সেই মুহূর্ত যখন stock কমে, ledger row বসে আর `InvoiceNo` assign হয়। tool শুধু
**unapproved SalesOrder** বানায় — তাই AI ভুল বুঝলেও গুদামের হিসাব অক্ষত, খসড়া মুছে দিলেই শেষ।
approve মানুষ ERP-তে করে। irreversible কাজটা AI-র হাতেই দেওয়া হয়নি।

তাই নাম `create_invoice` **নয়** — invoice নম্বর approve-এর আগে জন্মায়ই না। যে কাজ tool করে না
সেই নাম দিলে LLM-ও ভুল প্রতিশ্রুতি দিত ("ইনভয়েস তৈরি হয়েছে")। description-এ "invoice" শব্দটা
আছে, তাই "invoice বানাও" বললেও routing ঠিক থাকে।

যেসব ক্ষেত্রে row না বসিয়ে **error** ফেরে (প্রতিটার পেছনে কারণ আছে):

| অবস্থা | কেন থামে |
|---|---|
| `unitPrice` নেই | দাম আবিষ্কার = টাকার ক্ষতি; জিজ্ঞেস করতে বলা হয় |
| পণ্য/customer নাম একাধিকে মেলে | নিজে বেছে নিলে ভুল পার্টির নামে order বসত |
| stock-এর চেয়ে বেশি চাওয়া | ওই খসড়া কখনো approve হতো না — এখনই ধরিয়ে দেওয়া ভালো |
| quantity ≤ ০, line ২০-র বেশি | সাধারণ input guard |

- **Serialized পণ্যে গুদামের আসল serial বসে** (পুরনো আগে, seeder যেভাবে করে) — নইলে approve-এ
  "Serial(s) not in stock" throw করত। ⚠️ এটা reservation নয়: একই serial অন্য খসড়াতেও যেতে পারে,
  সংঘর্ষ হলে approve সশব্দে ব্যর্থ হবে।
- `CreatedBy` = seed system user, কারণ AppUser → ErpUser ম্যাপিং এখনো নেই (§খোলা প্রশ্ন)।
- **যা এখনো নেই:** loop থামিয়ে user-confirm (§D-র মূল ভাবনা)। খসড়া reversible বলে সেটা ছাড়াই
  নিরাপদ। tool যদি কখনো approve/delete করে, তখন confirm-flow **আবশ্যক** হয়ে যাবে।

## Warranty (2026-07-25)

Product-এ warranty column বসেনি, আলাদা table হয়েছে — কারণ **এক পণ্যে একাধিক warranty** থাকে
(Dell XPS 13: Service ২৪ মাস + Parts ১২ মাস)। column দিয়ে সেটা ধরা যেত না।

Industry-তে warranty তিন স্তরে থাকে; আমরা **শুধু প্রথমটা** বানিয়েছি:

| স্তর | Table | অবস্থা |
|---|---|---|
| Offer — product-এ কী warranty আসে | `ProductWarranty` | ✅ আছে |
| Instance — বিক্রীত unit-এর start/end, extended warranty, ownership transfer | `WarrantyRegistration` | ❌ বাদ |
| Claim — claim log, status, RMA | `WarrantyClaim` | ❌ বাদ |

Instance স্তর বাদ কারণ সেটা **derive করা যায়** — মেয়াদ শেষ = `SalesOrder.OrderDate + DurationMonths`,
আর `SalesOrderSerial → SalesOrderDetail → SalesOrder` chain আগে থেকেই আছে। row রাখলে duplicate
data, আর order date বদলালে stale। Extended warranty বিক্রি হলে, installation date থেকে warranty
শুরু হলে, বা claim history লাগলে — তখন ঐ দুই table যোগ করতে হবে।

- **শুরুর তারিখ = `SalesOrder.OrderDate`**, `ApprovalDateTime` নয়। ApprovalDateTime seeder চালানোর
  সময় বসে, আসল বিক্রির সময় নয় — জানুয়ারির বিক্রিরও approval আজকের তারিখে দেখাত।
- **`DurationMonths` null = আজীবন**; row না থাকা = ঐ ধরনের warranty নেই। দুটো ভিন্ন উত্তর।
- **Product location-wise আলাদা row**, তাই একই পণ্যের warranty প্রতি branch-এ আলাদা row (১০ spec × ২ branch = ২০)।
- **মেয়াদের হিসাব C#-এ, LLM-এ নয়** — "২৪ মাস পরে কোন তারিখ, সেটা পেরিয়েছে কি না" ছোট model
  আত্মবিশ্বাসের সাথে ভুল বলে। tool `expiresOn` / `isExpired` / `daysRemaining` তৈরি করে পাঠায়।

**Doc না DB** — এই ভাগটা না মানলে AI ভুল সংখ্যা বলবে:

| | কোথায় | কে দেয় |
|---|---|---|
| product-wise কত মাস, কবে শেষ | DB | `get_warranty` |
| claim প্রক্রিয়া, কী কভার হয় না, কী কাগজ লাগে | knowledge base doc | `search_knowledge_base` |

তাই `KnowledgeBaseSearchTool`-এর description-এ স্পষ্ট লেখা আছে যে সেখানে product-specific মেয়াদ
নেই — নইলে LLM policy doc থেকে সংখ্যা "মনে করে" বলে দেয়।

## Seed data (2026-07-22)

`ErpSeeder` — **deterministic** (fixed seed `20260722`) ও **idempotent** (Company থাকলে skip)।
চলে startup-এ, শুধু `Erp:SeedOnStartup=true` হলে (Development-এ true, নইলে false — integration test/production যেন না ছোঁয়)।

⚠️ Seeder stock নিজে হাতে লেখে না — `ErpStockService.Approve*` ডাকে। তাই seed data ঠিক ততটাই সামঞ্জস্যপূর্ণ যতটা আসল approval-এ হতো।

কী ঢোকে: ১ Company (APC), ২ Location (HO/CB), ২ ErpUser, ১৬ Product (৮ × ২ branch), ৪ Supplier, ৬ Customer, ২৮ PurchaseOrder, ৫৬ SalesOrder, ১৯১ ledger row — সবই ২০২৬ জানুয়ারি→জুলাই।

Warranty ২০ row (2026-07-25)। ইচ্ছাকৃত বৈচিত্র্য, যাতে AI-কে সব কেসে পরীক্ষা করা যায়: একাধিক
warranty (XPS), আজীবন (SanDisk, `DurationMonths` null), কিছুই নেই (HDMI Cable)।

⚠️ Warranty seed **আলাদা করে idempotent** (`SeedProductWarrantiesAsync`) — table পরে যোগ হয়েছে,
তাই `if (Companies.Any()) return;` শাখাতেও এটা চলে; নইলে আগে seed হওয়া DB-তে কখনো ঢুকত না।

**যাচাইয়ের জন্য আসল উত্তর** (AI এগুলোই বলা উচিত):

| মাস | Order | মোট বিক্রি |
|---|---|---|
| জানুয়ারি | ৮ | ১৬,৮৪,৭৪৫ |
| ফেব্রুয়ারি | ৮ | ১৫,৯০,৪৬৩ |
| মার্চ | ৮ | ১৬,০৭,১০১ |
| এপ্রিল | ৮ | ১৩,৬০,৪৮১ |
| মে | ৮ | ১২,১৩,৯৮৪ |
| জুন | ৮ | ১৪,১০,৮৬৭ |
| **জুলাই** | **৮** | **১৭,৪৩,৫৬৮** |

নতুন করে seed করতে: ERP database drop → API চালাও।

## সিদ্ধান্ত: ErpUser ↔ AiPlatform AppUser (2026-07-20)

**hard FK কখনোই নয়** — (ক) আলাদা DbContext, EF cross-context FK enforce করতে পারে না; (খ) পরে আসল ERP DB বসালে তার টেবিলে আমাদের Identity-র FK যোগ করা যাবে না (swap-ability নষ্ট); (গ) ERP-র বেশিরভাগ কর্মী AiPlatform-এ login-ই করে না।

**link লাগবে write tool-এ** (`create_invoice`) — তখন `CreatedBy`/`ApprovedBy` কে বসবে + ঐ ব্যক্তি কোন Company/Location দেখতে পাবে, দুটোই জানা লাগবে।

**পরিকল্পিত সমাধান: `AppUser.ErpUserId` (nullable long, FK constraint ছাড়া)** — নিজের user table-এ নিজের column, ERP schema অক্ষত। *ERP টেবিলে `AppUserId` বসানো হবে না।*

⏳ **এখন বানানো হয়নি — ইচ্ছাকৃত।** read tool-এ ("জুলাইয়ের বিক্রি") "কে করল" লাগে না। step 4 (write tool)-এ এক column + এক migration দিলেই হবে; এখন বানালে অব্যবহৃত পড়ে থাকত।

## নীতি (মনে রাখা)
- Ollama integration **আগে** শেষ হবে, তারপর এটা
- read tool আগে, write tool শেষে (data নিরাপত্তা)
- write action = সবসময় confirm (irreversible)
- ছোট step, কিছু ভাঙা যাবে না — Ollama-র মতোই
