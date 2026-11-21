namespace AiPlatform.Application.Features.Agent;

// appsettings "Agent"। feature-toggle + safety + tool vector retrieval।
public class AgentSettings
{
    public const string SectionName = "Agent";

    public bool Enabled { get; init; } = true;
    public int MaxIterations { get; init; } = 5;

    // এক বার্তায় অনেক প্রশ্ন (১০–২০) — প্রশ্ন আলাদা করে প্রতিটার আলাদা agent run (ছোট model-এ এক loop-এ সব হয় না)।
    public bool SplitQuestions { get; init; } = true;
    public int MaxQuestionsPerMessage { get; init; } = 20;
    // একসাথে কয়টা প্রশ্ন চলবে; LLM server-এর (Ollama/vLLM) সমান্তরাল সক্ষমতার সাথে মেলাও।
    public int MaxParallelQuestions { get; init; } = 4;

    // প্রতিটা প্রশ্নে ডকুমেন্ট খোঁজা LLM-এর আগে নিজেই চলে (retrieval-first RAG): মডেলকে "খুঁজব কিনা" ভাবতে হয় না।
    // নইলে reasoning বন্ধ ছোট model কঠিন প্রশ্নে tool না ডেকেই "তথ্য নেই" বলত। retrieval সস্তা (~১০০ms)।
    public bool PreRetrieveDocuments { get; init; } = true;

    // সেরা passage-এর similarity এর নিচে হলে passage ঢোকানো হয় না (ERP প্রশ্ন — "জুলাইয়ের বিক্রি", "স্টক কত" —
    // ডকুমেন্টের সাথে মেলে না, কিন্তু passage ঢুকলে ছোট model tool না ডেকে "নথিতে নেই" বলত)। ⚠️ score-এর মাপ embedding
    // model-ভেদে আলাদা (qwen3-embedding: ERP ≤০.৪২, ডকুমেন্ট ≥০.৫৮ → ০.৪৫)। তাই default ০ = বন্ধ; model বদলালে মেপে বসাও।
    public double PreRetrieveMinScore { get; init; }

    // একই gate, কিন্তু Rerank পদ্ধতি চললে reranker-এর relevance score দেখে (vector cosine-এর বদলে) — অনেক নির্ভুল:
    // মাপা: ERP প্রশ্ন ≤০.০০৩, ডকুমেন্ট প্রশ্ন (উত্তর-যোগ্য) ≥০.০০৬৭ (সাধারণত ≥০.১৫) → ০.০০৫। "সাপ্তাহিক ছুটি" প্রশ্নে vector
    // cosine ০.৪১–০.৪৪ (<০.৪৫, ভুল করে আটকাত) অথচ reranker ০.৪৬। ০ = বন্ধ (তখন PreRetrieveMinScore-ই চলে)।
    public double PreRetrieveMinRerankScore { get; init; }

    // "আজ" কোন টাইমজোনে ধরা হবে (prompt-এর তারিখ ও add_to_date-র default)। UTC ধরলে বাংলাদেশে রাত ১২টা–ভোর ৬টায় agent
    // গতকালের তারিখ ধরত ("আজ resign করলে শেষ দিন কবে?" এক দিন ভুল)। IANA নাম (Asia/Dhaka); না চিনলে UTC।
    public string TimeZone { get; init; } = "Asia/Dhaka";

    // Tool RAG — descriptions → আলাদা Qdrant collection; prompt-এ top-k subset।
    public bool ToolRetrievalEnabled { get; init; } = true;
    public int ToolTopK { get; init; } = 8;
    public string ToolsCollection { get; init; } = "aiplatform_tools";
}
