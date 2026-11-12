namespace AiPlatform.Application.Features.Evals;

// deterministic scoring — LLM-judge ছাড়া (keyword grounding + refusal check)।
// ponytail: keyword/refusal heuristic যথেষ্ট প্রথম eval; nuance লাগলে পরে LLM-judge scorer যোগ করা যাবে।
public static class EvalScorer
{
    // model grounding মেনে "তথ্য নেই" ধরনের উত্তর দিয়েছে কি না (system prompt এমন বলতে বলে)।
    public static bool IsRefusal(string answer) => answer.Contains("নেই", StringComparison.Ordinal);

    public static EvalCaseResult Score(EvalCase c, string answer)
    {
        var refused = IsRefusal(answer);

        if (c.ExpectRefusal)
            return refused
                ? new EvalCaseResult(c.Id, true, answer, null)
                : new EvalCaseResult(c.Id, false, answer, "expected refusal but model answered");

        if (refused)
            return new EvalCaseResult(c.Id, false, answer, "model refused unexpectedly");

        var missing = c.ExpectedKeywords
            .Where(k => !answer.Contains(k, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return missing.Count == 0
            ? new EvalCaseResult(c.Id, true, answer, null)
            : new EvalCaseResult(c.Id, false, answer, $"missing keywords: {string.Join(", ", missing)}");
    }

    public static EvalReport Aggregate(IReadOnlyList<EvalCaseResult> results)
    {
        var passed = results.Count(r => r.Passed);
        var rate = results.Count == 0 ? 0 : Math.Round((double)passed / results.Count, 3);
        return new EvalReport(results.Count, passed, rate, results);
    }
}
