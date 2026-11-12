namespace AiPlatform.Application.Common.Interfaces;

// Embedding task-এর ধরন (retrieval query vs document — provider-ভেদে ভিন্ন হতে পারে)।
public enum EmbeddingTaskType
{
    RetrievalQuery,
    RetrievalDocument
}

// Text → vector। Phase 3 RAG এটা ব্যবহার করবে।
public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, EmbeddingTaskType taskType, CancellationToken cancellationToken);
}
