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

    // Tool RAG — descriptions → আলাদা Qdrant collection; prompt-এ top-k subset।
    public bool ToolRetrievalEnabled { get; init; } = true;
    public int ToolTopK { get; init; } = 8;
    public string ToolsCollection { get; init; } = "aiplatform_tools";
}
