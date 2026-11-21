using AiPlatform.Application.Common.Models;

namespace AiPlatform.Application.Common.Interfaces;

// Cross-encoder reranker: প্রশ্ন আর প্রতিটা chunk *একসাথে পড়ে* নতুন করে সাজায় (vector search শুধু আলাদা আলাদা
// embedding মেলায়, তাই কাছাকাছি-কিন্তু-ভুল chunk উপরে উঠতে পারে)। Score = relevance (০–১, বেশি = বেশি প্রাসঙ্গিক)।
// ব্যর্থ হলে (বন্ধ/সার্ভার নেই) exception ছোড়ে — caller নীরবে ভুল ক্রম ধরে নেবে না, fallback সে-ই বেছে নেয়।
public interface IReranker
{
    Task<IReadOnlyList<RetrievedChunk>> RerankAsync(
        string query, IReadOnlyList<RetrievedChunk> candidates, int topN, CancellationToken cancellationToken);
}
