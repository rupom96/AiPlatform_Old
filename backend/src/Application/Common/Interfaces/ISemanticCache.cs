namespace AiPlatform.Application.Common.Interfaces;

// প্রশ্ন-embedding similarity দিয়ে cache: কাছাকাছি প্রশ্ন এলে cached উত্তর ফেরত (LLM call এড়ায়)।
public interface ISemanticCache
{
    bool IsEnabled { get; }

    // similarity ≥ threshold এমন cached উত্তর থাকলে ফেরত, নইলে null।
    Task<string?> GetAsync(float[] queryEmbedding, CancellationToken cancellationToken);

    // প্রশ্ন-embedding + উত্তর cache-এ রাখে।
    Task SetAsync(float[] queryEmbedding, string answer, CancellationToken cancellationToken);
}
