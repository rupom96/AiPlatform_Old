using System.Diagnostics;
using AiPlatform.Application.Common.Models;
using AiPlatform.Application.Common.Results;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace AiPlatform.Application.Features.Agent;

// এক বার্তার অনেক প্রশ্ন আলাদা agent run-এ চালিয়ে উত্তর এক করে (ক্রম বজায় রেখে)।
// প্রতিটা প্রশ্নের নিজস্ব DI scope: DbContext thread-safe নয়, আর tool গুলো scoped — scope ভাগ করলে সমান্তরালে ভাঙত।
// handler সরাসরি ডাকি (MediatR pipeline নয়): validation/audit মূল অনুরোধে একবারই হয়েছে, প্রতি প্রশ্নে নয়।
public class MultiQuestionRunner(IServiceScopeFactory scopeFactory, AgentSettings settings)
{
    public async Task<Result<AgentResponse>> RunAsync(
        QuestionSplit split, RunAgentCommand request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var gate = new SemaphoreSlim(Math.Max(1, settings.MaxParallelQuestions));

        var results = await Task.WhenAll(
            split.Questions.Select(question => RunOneAsync(question, request, gate, cancellationToken)));

        stopwatch.Stop();
        return Combine(split, results, stopwatch.ElapsedMilliseconds);
    }

    private async Task<Result<AgentResponse>> RunOneAsync(
        string question, RunAgentCommand request, SemaphoreSlim gate, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider
                .GetRequiredService<IRequestHandler<RunAgentCommand, Result<AgentResponse>>>();
            return await handler.Handle(request with { Message = question, SkipSplit = true }, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // সব প্রশ্ন ব্যর্থ হলে প্রথম error; নইলে সফলগুলোর উত্তর, ব্যর্থগুলোর জায়গায় ছোট বার্তা — একটা প্রশ্নের জন্য বাকি উত্তর হারায় না।
    private static Result<AgentResponse> Combine(
        QuestionSplit split, IReadOnlyList<Result<AgentResponse>> results, long wallMs)
    {
        if (results.All(r => r.IsFailure))
            return results[0].Error;

        var sections = split.Questions.Select((question, index) =>
            $"{index + 1}. {question}\n" +
            (results[index].IsSuccess
                ? results[index].Value.Answer
                : $"(এই প্রশ্নের উত্তর আনা যায়নি: {results[index].Error.Description})"))
            .ToList();

        if (split.Skipped > 0)
            sections.Add($"(মোট {split.Questions.Count + split.Skipped}টি প্রশ্ন ছিল; একসাথে সর্বোচ্চ " +
                         $"{split.Questions.Count}টির উত্তর দেওয়া হয়। বাকি {split.Skipped}টি আলাদা করে পাঠান।)");

        var succeeded = results.Where(r => r.IsSuccess).Select(r => r.Value).ToList();
        return new AgentResponse(
            string.Join("\n\n", sections),
            succeeded.SelectMany(r => r.ToolsUsed).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            wallMs,
            MergeHits(succeeded.Select(r => r.ToolHits)),
            MergeHits(succeeded.Select(r => r.DocumentHits)),
            succeeded[0].Provider,
            succeeded[0].Model,
            CommonMethod(succeeded));
    }

    // সব প্রশ্ন একই পদ্ধতিতে খুঁজলে সেটা; কিছু প্রশ্ন ফল্লব্যাক করলে (পদ্ধতি মেলে না) null — মিথ্যা "সব rerank চলেছে" বলি না।
    // যে প্রশ্নে ডকুমেন্ট খোঁজাই হয়নি (ERP) সেটা হিসাবে ধরি না।
    private static string? CommonMethod(IEnumerable<AgentResponse> responses)
    {
        var methods = responses.Select(r => r.RetrievalMethodUsed).Where(m => m is not null).Distinct().ToList();
        return methods.Count == 1 ? methods[0] : null;
    }

    // একই label-এর সর্বোচ্চ score রেখে, বেশি score আগে। কিছুই না থাকলে null (UI-তে ফাঁকা তালিকা দেখায় না)।
    private static IReadOnlyList<ScoredHit>? MergeHits(IEnumerable<IReadOnlyList<ScoredHit>?> perQuestion)
    {
        var merged = perQuestion
            .Where(hits => hits is not null)
            .SelectMany(hits => hits!)
            .GroupBy(hit => hit.Label)
            .Select(group => group.OrderByDescending(hit => hit.Score).First())
            .OrderByDescending(hit => hit.Score)
            .ToList();

        return merged.Count > 0 ? merged : null;
    }
}
