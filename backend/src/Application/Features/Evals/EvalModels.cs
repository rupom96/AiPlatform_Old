namespace AiPlatform.Application.Features.Evals;

// এক eval case: প্রশ্ন + কোন method + সফল উত্তরে যে keyword থাকা লাগবে (grounding),
// অথবা ExpectRefusal=true হলে model-এর "তথ্য নেই" বলা উচিত (off-topic grounding test)।
public record EvalCase(
    string Id,
    string Question,
    string Method,
    IReadOnlyList<string> ExpectedKeywords,
    bool ExpectRefusal = false);

// এক case চালানোর ফল।
public record EvalCaseResult(string Id, bool Passed, string Answer, string? FailReason);

// পুরো run-এর report — pass rate + প্রতি case-এর ফল।
public record EvalReport(int Total, int Passed, double PassRate, IReadOnlyList<EvalCaseResult> Results);
