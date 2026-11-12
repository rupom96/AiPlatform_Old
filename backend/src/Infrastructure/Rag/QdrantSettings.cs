namespace AiPlatform.Infrastructure.Rag;

public class QdrantSettings
{
    public const string SectionName = "Qdrant";

    public string Url { get; init; } = "http://localhost:6333";
    public string Collection { get; init; } = "aiplatform_rag";
}
