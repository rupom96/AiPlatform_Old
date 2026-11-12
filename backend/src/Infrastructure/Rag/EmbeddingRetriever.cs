using AiPlatform.Application.Common.Interfaces;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Text;

namespace AiPlatform.Infrastructure.Rag;

// seed doc গুলো chunk → embed (একবার, lazy) → query embed → cosine similarity দিয়ে top-N।
public class EmbeddingRetriever(IEmbeddingProvider embeddingProvider) : IRetriever
{
    public string Name => "embedding";

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private List<(string Chunk, float[] Vector)>? _corpus;

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topN, CancellationToken cancellationToken)
    {
        // query-র embedding corpus-এর উপর নির্ভর করে না — একসাথে চালাই।
        // corpus তৈরি থাকলে (২য় request থেকে) এটা সঙ্গে সঙ্গে ফেরে, তাই খরচ শুধু cold start-এ বাঁচে।
        var corpusTask = EnsureCorpusAsync(cancellationToken);
        var queryTask = embeddingProvider.EmbedAsync(query, EmbeddingTaskType.RetrievalQuery, cancellationToken);
        await Task.WhenAll(corpusTask, queryTask);

        var corpus = await corpusTask;
        var queryVector = await queryTask;

        return corpus
            .Select(c => new RetrievedChunk(c.Chunk, VectorMath.CosineSimilarity(queryVector, c.Vector), "knowledge-base"))
            .OrderByDescending(c => c.Score)
            .Take(topN)
            .ToList();
    }

    // corpus embedding একবারই বানায় (double-checked lock)
    private async Task<List<(string Chunk, float[] Vector)>> EnsureCorpusAsync(CancellationToken cancellationToken)
    {
        if (_corpus is not null) return _corpus;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_corpus is not null) return _corpus;

            var chunks = KnowledgeBaseSeed.Documents.SelectMany(d => TextChunker.Chunk(d)).ToList();

            // এক এক করে embed করলে প্রতিটার ~500ms যোগ হতো (৫ chunk = ~2.5s cold start)।
            // সব একসাথে পাঠালে সময় = সবচেয়ে ধীরটার সময়, যোগফল নয়।
            var vectors = await Task.WhenAll(chunks.Select(chunk =>
                embeddingProvider.EmbedAsync(chunk, EmbeddingTaskType.RetrievalDocument, cancellationToken)));

            _corpus = [.. chunks.Zip(vectors)];
            return _corpus;
        }
        finally
        {
            _initLock.Release();
        }
    }
}
