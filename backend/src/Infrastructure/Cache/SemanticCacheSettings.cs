namespace AiPlatform.Infrastructure.Cache;

// feature toggle সহ — config দিয়ে on/off (boilerplate নীতি)।
public class SemanticCacheSettings
{
    public const string SectionName = "SemanticCache";

    public bool Enabled { get; init; } = true;
    public double SimilarityThreshold { get; init; } = 0.95;
    public string Collection { get; init; } = "semantic_cache";
}
