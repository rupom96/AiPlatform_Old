namespace AiPlatform.Application.Common.Models;

// ডকুমেন্ট খোঁজার পদ্ধতির নাম — UI dropdown, API ও IRetriever.Name সবাই এই একই মান ব্যবহার করে।
// Default (null/"default") = আগের আচরণ অপরিবর্তিত: vector search ("qdrant"), ব্যর্থ হলে পরের fallback।
// বাকিগুলো ঐচ্ছিক, তুলনা করে দেখার জন্য।
public static class RetrievalMethods
{
    public const string Default = "default";
    public const string Vector = "qdrant";
    public const string Hybrid = "qdrant-hybrid";
    public const string Rerank = "qdrant-rerank";
    public const string HybridRerank = "qdrant-hybrid-rerank";

    // API-তে যা পাঠানো যায় (Vector-ও Default-এর মতোই, তাই আলাদা করে দেখানো হয় না)।
    public static readonly IReadOnlyList<string> Selectable = [Default, Hybrid, Rerank, HybridRerank];

    public static bool IsValid(string? method) =>
        string.IsNullOrWhiteSpace(method)
        || method.Equals(Vector, StringComparison.OrdinalIgnoreCase)
        || Selectable.Contains(method, StringComparer.OrdinalIgnoreCase);

    // Default/খালি = কিছু বেছে নেওয়া হয়নি (আগের পথ)।
    public static bool IsDefault(string? method) =>
        string.IsNullOrWhiteSpace(method)
        || method.Equals(Default, StringComparison.OrdinalIgnoreCase)
        || method.Equals(Vector, StringComparison.OrdinalIgnoreCase);
}
