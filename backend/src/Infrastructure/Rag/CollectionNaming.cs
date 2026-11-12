using System.Text.RegularExpressions;

namespace AiPlatform.Infrastructure.Rag;

// Embedding model-ভেদে Qdrant collection নাম। ভিন্ন model = ভিন্ন vector space + dimension
// (nomic 768 ≠ bge-m3 1024) → একই collection-এ মেশানো যায় না। gemini (default) = legacy নাম
// অপরিবর্তিত (break nothing); অন্য যেকোনো model = model-নাম suffix (dimension collision এড়ায়,
// model বদলালে re-ingest লাগে — স্বাভাবিক, আলাদা vector space)।
public static partial class CollectionNaming
{
    public static string ForEmbedding(string baseName, string embeddingProvider, string embeddingModel)
        => embeddingProvider.Equals("gemini", StringComparison.OrdinalIgnoreCase)
            ? baseName
            : $"{baseName}_{Sanitize(embeddingModel)}";

    // Qdrant collection নাম-এ শুধু alphanumeric/_/- নিরাপদ; বাকি (: . / ইত্যাদি) → _
    private static string Sanitize(string model) => UnsafeChars().Replace(model, "_");

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex UnsafeChars();
}
