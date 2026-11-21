using AiPlatform.Application.Features.Agent;
using Xunit;

namespace AiPlatform.Application.Tests.Agent;

public class ToolCallTextTests
{
    private static readonly string[] Tools = ["add_to_date", "get_sales", "search_knowledge_base"];

    [Theory]
    [InlineData("add_to_date(months=2)")]
    [InlineData("get_sales(groupBy=\"product\", month=\"2026-10\")")]
    [InlineData("  ADD_TO_DATE (months=2) - নোটিশ দুই মাস")]
    [InlineData("আজ থেকে দুই মাস: add_to_date(months=2) এবং এটি গণনা করা হয়েছে।")]
    public void WrittenToolCall_IsDetected(string content) =>
        Assert.True(ToolCallText.LooksLikeToolCall(content, Tools));

    [Theory]
    [InlineData("শেষ কর্মদিবস হবে ২০২৭-০১-২১ (বৃহস্পতিবার)।")]          // বন্ধনী আছে, tool-এর নাম নেই
    [InlineData("আমি add_to_date টুল ব্যবহার করেছি।")]                    // নাম আছে, কিন্তু call নয়
    [InlineData("my_get_sales(1) is something else")]                       // অন্য শব্দের অংশ
    [InlineData("")]
    [InlineData(null)]
    public void NormalAnswers_AreNotMistakenForToolCalls(string? content) =>
        Assert.False(ToolCallText.LooksLikeToolCall(content, Tools));

    [Fact]
    public void NoTools_NeverMatches() =>
        Assert.False(ToolCallText.LooksLikeToolCall("add_to_date(months=2)", []));
}
