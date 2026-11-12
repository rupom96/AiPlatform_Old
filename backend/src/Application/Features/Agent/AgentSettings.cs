namespace AiPlatform.Application.Features.Agent;

// appsettings "Agent"। feature-toggle + safety + tool vector retrieval।
public class AgentSettings
{
    public const string SectionName = "Agent";

    public bool Enabled { get; init; } = true;
    public int MaxIterations { get; init; } = 5;

    // Tool RAG — descriptions → আলাদা Qdrant collection; prompt-এ top-k subset।
    public bool ToolRetrievalEnabled { get; init; } = true;
    public int ToolTopK { get; init; } = 8;
    public string ToolsCollection { get; init; } = "aiplatform_tools";
}
