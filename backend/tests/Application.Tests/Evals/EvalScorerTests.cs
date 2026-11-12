using AiPlatform.Application.Features.Evals;

namespace AiPlatform.Application.Tests.Evals;

public class EvalScorerTests
{
    private static EvalCase Case(IReadOnlyList<string> keywords, bool refuse = false) =>
        new("id", "q", "hybrid", keywords, refuse);

    [Fact]
    public void AllKeywordsPresent_Passes()
    {
        var result = EvalScorer.Score(Case(["bKash", "Nagad"]), "আমরা bKash ও Nagad গ্রহণ করি।");
        Assert.True(result.Passed);
    }

    [Fact]
    public void MissingKeyword_Fails_WithReason()
    {
        var result = EvalScorer.Score(Case(["bKash", "ওয়ারেন্টি"]), "আমরা bKash গ্রহণ করি।");
        Assert.False(result.Passed);
        Assert.Contains("ওয়ারেন্টি", result.FailReason);
    }

    [Fact]
    public void ExpectRefusal_AndModelRefuses_Passes()
    {
        var result = EvalScorer.Score(Case([], refuse: true), "দুঃখিত, এই তথ্যটি আমার কাছে নেই।");
        Assert.True(result.Passed);
    }

    [Fact]
    public void ExpectRefusal_ButModelAnswers_Fails()
    {
        var result = EvalScorer.Score(Case([], refuse: true), "ঢাকার আবহাওয়া আজ রৌদ্রোজ্জ্বল।");
        Assert.False(result.Passed);
    }

    [Fact]
    public void UnexpectedRefusal_Fails()
    {
        var result = EvalScorer.Score(Case(["bKash"]), "দুঃখিত, তথ্য নেই।");
        Assert.False(result.Passed);
    }

    [Fact]
    public void Aggregate_ComputesPassRate()
    {
        var report = EvalScorer.Aggregate(
        [
            new EvalCaseResult("a", true, "", null),
            new EvalCaseResult("b", false, "", "x"),
            new EvalCaseResult("c", true, "", null),
            new EvalCaseResult("d", true, "", null),
        ]);

        Assert.Equal(4, report.Total);
        Assert.Equal(3, report.Passed);
        Assert.Equal(0.75, report.PassRate);
    }
}
